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
    sealed class ModClick : MonoBehaviour, IPointerClickHandler { }
    static T Attach<T>(GameObject o, T c) where T : Component { c.gameObject = o; o.Components[typeof(T)] = c; return c; }
    static Response List(int offset = 0) { var r = new Response(); GameUi.List(r, offset); return r; }
    static Request Click(string id) => new() { operation = "ui", uiAction = "click", target = id };
    public static void Run()
    {
        UnityEngine.Object.Objects.Clear(); EventSystem.current = new();
        var o = new GameObject { name = "mod-added-button" };
        Attach(o, new Canvas()); var b = Attach(o, new Button()); var label = Attach(o, new Text { text = "Mod action" });
        EventSystem.current.Hits.Add(new() { gameObject = o });
        var clicks = 0; b.Click = () => clicks++;
        foreach(var offset in new[]{-1,100001})
        {
            var invalid=new Request{operation="ui-list",offset=offset,id=Guid.NewGuid().ToString(),token=new string('a',64)};
            try{invalid.Validate();throw new Exception("Bad offset accepted");}catch(System.IO.InvalidDataException){}
        }
        var first = List().ui.Single();
        Assert(first.interactable && first.label == "Mod action" && first.type.Contains("Button"), "Dynamic mod button not described");
        var r = Click(first.id); r.id = Guid.NewGuid().ToString(); r.token = new string('a',64);
        Assert(Protocol.Decode(Protocol.Encode(r)).target == first.id, "UI target schema failed");
        GameUi.Execute(r); Assert(clicks == 1, "Native submit did not invoke button listener");
        Refused(() => GameUi.Execute(r)); Assert(clicks == 1, "Consumed UI ID replayed");
        r = Click(List().ui.Single().id); b.interactable = false;
        Refused(() => GameUi.Execute(r)); Assert(List().ui.Single().reason == "disabled", "Disabled button bypassed");
        b.interactable = true; r = Click(List().ui.Single().id); label.text = "Delete world";
        Refused(() => GameUi.Execute(r)); Assert(clicks == 1, "Changed button invoked old action");
        r = Click(List().ui.Single().id); EventSystem.current.Hits.Clear();
        Refused(() => GameUi.Execute(r)); Assert(!List().ui.Single().interactable, "Modal obstruction bypassed");
        EventSystem.current.Hits.Add(new() { gameObject = o });
        var group = Attach(o, new CanvasGroup { alpha = 0 });
        Assert(!List().ui.Single().interactable, "Transparent UI remained available"); group.alpha = 1;
        r = Click(List().ui.Single().id); o.activeInHierarchy = false; Refused(() => GameUi.Execute(r));
        Assert(List().ui.Length == 0, "Inactive UI included"); o.activeInHierarchy = true;
        r = Click(List().ui.Single().id); Time.realtimeSinceStartup += 31; Refused(() => GameUi.Execute(r));
        r = Click(List().ui.Single().id); List(); Refused(() => GameUi.Execute(r));
        for(var i=0;i<66;i++) { var item=new GameObject(); Attach(item,new Canvas()); Attach(item,new Button()); }
        var page=List(); Assert(page.ui.Length==64 && page.uiTotal==67 && page.uiNextOffset==64,"UI pagination failed");
        Assert(List(64).ui.Length==3 && List(64).uiNextOffset==null,"Last UI page failed");
        UnityEngine.Object.Objects.Clear(); o = new GameObject(); Attach(o,new Canvas()); Attach(o,new ModClick());
        EventSystem.current.Hits.Clear(); EventSystem.current.Hits.Add(new(){gameObject=o});
        var events=new List<string>();
        foreach(var type in new[]{typeof(IPointerDownHandler),typeof(IPointerUpHandler),typeof(IPointerClickHandler)}) o.Handlers[type]=_=>events.Add(type.Name);
        GameUi.Execute(Click(List().ui.Single().id));
        Assert(events.SequenceEqual(new[]{"IPointerDownHandler","IPointerUpHandler","IPointerClickHandler"}),"Custom UI event order changed");
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
        Player.m_localPlayer=null; ZNet.instance=null; Menu.Visible=oldMenu;
        var menuCapture=Execute(new(){operation="observe"}); Assert(menuCapture.state=="capture_pending","Menu screenshot refused");
        var capture=plugin.Coroutines.Last(); capture.MoveNext(); capture.MoveNext();
        Assert(Execute(new(){operation="observe"}).image!=null,"Menu screenshot not returned");
        r=Click(List().ui.Single().id); Application.isFocused=false; Assert(!Execute(r).ok,"Unfocused menu activation accepted"); Application.isFocused=true;
        FejdStartup.instance=null; Assert(!Execute(r).ok,"No-scene UI accepted");
        EventSystem.current=null; UnityEngine.Object.Objects.Clear(); GameUi.Invalidate();
        System.Console.WriteLine("PASS: UI discovery, mod button native submit, stale/consumed/expired IDs, disabled/hidden/obstructed UI, paging, custom events, menu permission/focus and screenshots");
    }
}
