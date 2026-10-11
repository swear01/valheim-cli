using System.Reflection;
using System.Text;
using ValheimCliBridge;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

static class UiTests
{
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static void Refused(Action action) { try { action(); } catch (ActionRefusedException) { return; } throw new Exception("Unsafe UI action accepted"); }
    sealed class ModSubmit : MonoBehaviour, ISubmitHandler { public int Calls; public void OnSubmit(BaseEventData data) { Calls++; } }
    sealed class ModClick : MonoBehaviour, IPointerClickHandler { }
    sealed class ModScroll : MonoBehaviour, IScrollHandler { }
    static T Attach<T>(GameObject o, T c) where T : Component { c.gameObject = o; o.Components[typeof(T)] = c; return c; }
    static Response List(int offset = 0) { var r = new Response(); GameUi.List(r, offset); return r; }
    static Request Click(string id) => new() { operation = "ui", uiAction = "click", target = id };
    public static void Run()
    {
        var previousObjects=UnityEngine.Object.Objects.ToArray(); var previousSystem=EventSystem.current;
        var previousTime=Time.realtimeSinceStartup; var previousFocus=Application.isFocused;
        var previousPlayer=Player.m_localPlayer; var previousNet=ZNet.instance; var previousStartup=FejdStartup.instance;
        var previousCamera=Camera.main; var previousProjection=RectTransformUtility.LastCamera; var previousMenu=Menu.Visible;
        try
        {
        UnityEngine.Object.Objects.Clear(); EventSystem.current = new();
        var o = new GameObject { name = "mod-added-button" };
        Attach(o, new Canvas()); var b = Attach(o, new Button()); var label = Attach(o, new Text { text = "Mod action" });
        o.Components[typeof(Component)] = null;
        EventSystem.current.Hits.Add(new() { gameObject = o });
        var clicks = 0; b.Click = () => clicks++;
        foreach(var offset in new[]{-1,100001})
        {
            var invalid=new Request{operation="ui-list",offset=offset,id=Guid.NewGuid().ToString(),token=new string('a',64)};
            try{invalid.Validate();throw new Exception("Bad offset accepted");}catch(System.IO.InvalidDataException){}
        }
        var field = new GameObject(); Attach(field,new Canvas()); Attach(field,new InputField());
        label.enabled=false; Assert(List().ui.Single().label=="","Hidden text leaked into UI label"); label.enabled=true;
        Attach(o,new InputField()); Assert(!List().ui.Single().actions.Contains("scroll"),"Unsupported input field advertised scrolling"); o.Components.Remove(typeof(InputField));
        var first = List().ui.Single();
        Assert(first.interactable && first.label == "Mod action" && first.type.Contains("Button"), "Dynamic mod button not described");
        var r = Click(first.id); r.id = Guid.NewGuid().ToString(); r.token = new string('a',64);
        Assert(Protocol.Decode(Protocol.Encode(r)).target == first.id, "UI target schema failed");
        GameUi.Execute(r); Assert(clicks == 1, "Native submit did not invoke button listener");
        Refused(() => GameUi.Execute(r)); Assert(clicks == 1, "Consumed UI ID replayed");
        r=Click(List().ui.Single().id); r.button="right"; Refused(()=>GameUi.Execute(r));
        var duplicate=new GameObject { name=o.name }; duplicate.transform.SiblingIndex=1;
        Attach(duplicate,new Canvas()); Attach(duplicate,new Button());
        Assert(List().ui.Select(c=>c.path).SequenceEqual(List().ui.Select(c=>c.path).OrderBy(path=>path,StringComparer.Ordinal)),"UI ordering is not deterministic");
        Assert(List().ui.Select(c=>c.path).Distinct().Count()==2,"Repeated button names have ambiguous hierarchy paths"); duplicate.activeInHierarchy=false;
        r = Click(List().ui.Single().id); b.interactable = false;
        Refused(() => GameUi.Execute(r)); Assert(List().ui.Single().reason == "disabled", "Disabled button bypassed");
        b.interactable = true; r = Click(List().ui.Single().id); label.text = "Delete world";
        Refused(() => GameUi.Execute(r)); Assert(clicks == 1, "Changed button invoked old action");
        r = Click(List().ui.Single().id); EventSystem.current.Hits.Clear();
        Refused(() => GameUi.Execute(r)); Assert(!List().ui.Single().interactable, "Modal obstruction bypassed");
        EventSystem.current.Hits.Add(new() { gameObject = o });
        var group = Attach(o, new CanvasGroup { alpha = 0 });
        Assert(!List().ui.Single().interactable, "Transparent UI remained available"); group.alpha = 1;
        var intermediate=new GameObject { name="componentless" }; var panel=new GameObject { name="ancestor" }; intermediate.transform.parent=panel.transform; var parentGroup=Attach(panel,new CanvasGroup { alpha=0 }); o.transform.parent=intermediate.transform;
        Assert(!List().ui.Single().interactable,"Transparent ancestor panel was ignored"); parentGroup.alpha=1; Assert(List().ui.Single().path.StartsWith("ancestor[0]/componentless[0]/"),"Fixture hierarchy lost ancestor names"); o.transform.parent=null;
        var canvas=o.GetComponent<Canvas>(); canvas.renderMode=RenderMode.WorldSpace; Camera.main=new();
        Assert(List().ui.Single().interactable && RectTransformUtility.LastCamera==Camera.main,"World-space Canvas did not use main camera fallback");
        canvas.worldCamera=new(); Assert(List().ui.Single().interactable && RectTransformUtility.LastCamera==canvas.worldCamera,"Assigned Canvas camera was replaced");
        canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=null; Assert(List().ui.Single().interactable && RectTransformUtility.LastCamera==null,"Unassigned screen-space camera no longer behaves as overlay");
        canvas.renderMode=RenderMode.ScreenSpaceOverlay; Camera.main=previousCamera;
        r = Click(List().ui.Single().id); o.activeInHierarchy = false; Refused(() => GameUi.Execute(r));
        Assert(List().ui.Length == 0, "Inactive UI included"); o.activeInHierarchy = true;
        r = Click(List().ui.Single().id); Time.realtimeSinceStartup += 31; Refused(() => GameUi.Execute(r));
        r = Click(List().ui.Single().id); List(); Refused(() => GameUi.Execute(r));
        for(var i=0;i<66;i++) { var item=new GameObject(); Attach(item,new Canvas()); Attach(item,new Button()); }
        var page=List(); Assert(page.ui.Length==64 && page.uiTotal==67 && page.uiNextOffset==64,"UI pagination failed");
        Assert(List(64).ui.Length==3 && List(64).uiNextOffset==null,"Last UI page failed");
        UnityEngine.Object.Objects.Clear();
        var canvasRoot=new GameObject(); var outerCanvas=Attach(canvasRoot,new Canvas());
        var nested=new GameObject(); nested.transform.parent=canvasRoot.transform; var innerCanvas=Attach(nested,new Canvas());
        o=new GameObject(); o.transform.parent=nested.transform; Attach(o,new Button());
        Attach(new GameObject(),new Button());
        EventSystem.current.Hits.Clear(); EventSystem.current.Hits.Add(new(){gameObject=o});
        Assert(List().ui.Length==1 && List().ui[0].interactable,"Nested Canvas discovery duplicated or lost controls, or included world objects");
        outerCanvas.enabled=innerCanvas.enabled=false; Assert(List().ui.Length==0,"Disabled Canvas controls were discovered");
        UnityEngine.Object.Objects.Clear(); o = new GameObject(); Attach(o,new Canvas()); Attach(o,new ModClick());
        EventSystem.current.Hits.Clear(); EventSystem.current.Hits.Add(new(){gameObject=o});
        var events=new List<string>();
        foreach(var type in new[]{typeof(IPointerDownHandler),typeof(IPointerUpHandler),typeof(IPointerClickHandler)}) o.Handlers[type]=d=>{Assert(((PointerEventData)d).pointerCurrentRaycast.gameObject==o && ((PointerEventData)d).position.x==50 && ((PointerEventData)d).position.y==50,"Native raycast geometry/context lost");events.Add(type.Name);};
        GameUi.Execute(Click(List().ui.Single().id));
        Assert(events.SequenceEqual(new[]{"IPointerDownHandler","IPointerUpHandler","IPointerClickHandler"}),"Custom UI event order changed");
        Attach(o,new ModScroll()); var scrolled=false;
        o.Handlers[typeof(IScrollHandler)]=d=>{var pointer=(PointerEventData)d;Assert(pointer.scrollDelta.y==-2 && pointer.position.x==50,"Native scroll geometry/delta lost");scrolled=true;};
        Refused(()=>GameUi.Execute(new(){operation="ui",uiAction="scroll",target=List().ui.Single().id}));
        GameUi.Execute(new(){operation="ui",uiAction="scroll",target=List().ui.Single().id,scroll=-2}); Assert(scrolled,"Native scroll handler not invoked");
        Attach(o,new Button());
        foreach(var mouseButton in new[]{"left","right"})
        {
            events.Clear(); r=Click(List().ui.Single().id); r.button=mouseButton; GameUi.Execute(r);
            Assert(events.SequenceEqual(new[]{"IPointerDownHandler","IPointerUpHandler","IPointerClickHandler"}),"Button component suppressed custom inventory events");
        }
        UnityEngine.Object.Objects.Clear(); o=new GameObject(); Attach(o,new Canvas()); var submit=Attach(o,new ModSubmit());
        EventSystem.current.Hits.Clear(); EventSystem.current.Hits.Add(new(){gameObject=o});
        r=Click(List().ui.Single().id); r.button="right"; Refused(()=>GameUi.Execute(r));
        r.button="left"; GameUi.Execute(r); Assert(submit.Calls==1,"Custom submit-only handler not invoked");
        var plugin = new Plugin(); using var controller = new InputController(); var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        void Field(string name,object value)=>typeof(Plugin).GetField(name,flags).SetValue(plugin,value);
        Field("input",controller); Field("controlReady",true); var permission=new BepInEx.Configuration.ConfigEntry<bool>(false);
        Field("allowControl",permission); Field("allowTeleport",new BepInEx.Configuration.ConfigEntry<bool>(false));
        Response Execute(Request value)=>(Response)typeof(Plugin).GetMethod("Execute",flags).Invoke(plugin,new object[]{value});
        Player.m_localPlayer=null; ZNet.instance=null; FejdStartup.instance=new(); Application.isFocused=true;
        var listing=Execute(new(){operation="ui-list"});
        Assert(listing.ok && listing.ui.Length==1 && !listing.uiAllowed,"Read-only menu listing failed");
        Assert(!Execute(Click(listing.ui[0].id)).ok,"UI permission bypassed"); permission.Value=true;
        Assert(Execute(Click(listing.ui[0].id)).ok,"Menu UI still requires a local character");
        var oldMenu=Menu.Visible; Menu.Visible=true;
        Player.m_localPlayer=new Player(); ZNet.instance=new ZNet();
        Assert(!Execute(new(){operation="input",moveZ=1,durationMs=200}).ok,"UI permission unblocked gameplay in menu");
        TextInput.Visible=true; Assert(!Execute(Click(List().ui.Single().id)).ok,"World text editing allowed UI activation"); TextInput.Visible=false;
        Minimap.Typing=true; Assert(!Execute(Click(List().ui.Single().id)).ok,"Map text editing allowed UI activation"); Minimap.Typing=false;
        Player.m_localPlayer.View.Owner=false;
        Assert(!Execute(Click(List().ui.Single().id)).ok,"Non-owner UI activation accepted"); Player.m_localPlayer.View.Owner=true;
        Player.m_localPlayer=null; ZNet.instance=null; Menu.Visible=oldMenu;
        var menuCapture=Execute(new(){operation="observe"}); Assert(menuCapture.state=="capture_pending","Menu screenshot refused");
        var capture=plugin.Coroutines.Last(); capture.MoveNext(); capture.MoveNext();
        Assert(Execute(new(){operation="observe"}).image!=null,"Menu screenshot not returned");
        r=Click(List().ui.Single().id); Application.isFocused=false; Assert(Execute(r).ok,"Unfocused menu activation refused"); r=Click(List().ui.Single().id);
        FejdStartup.instance=null; Assert(!Execute(r).ok,"No-scene UI accepted");
        }
        finally
        {
            EventSystem.current=previousSystem; Time.realtimeSinceStartup=previousTime; Application.isFocused=previousFocus;
            Player.m_localPlayer=previousPlayer; ZNet.instance=previousNet; FejdStartup.instance=previousStartup; Menu.Visible=previousMenu;
            Camera.main=previousCamera; RectTransformUtility.LastCamera=previousProjection;
            UnityEngine.Object.Objects.Clear(); UnityEngine.Object.Objects.AddRange(previousObjects); GameUi.Invalidate();
        }
        System.Console.WriteLine("PASS: UI discovery, mod button native submit, stale/consumed/expired IDs, disabled/hidden/obstructed UI, paging, custom events, menu permissions/background and screenshots");
    }
}
