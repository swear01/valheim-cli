using System.Text;
using ValheimCliBridge;
using UnityEngine;
using UnityEngine.EventSystems;

static class InputTests
{
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static Request Input() => new() { id=Guid.NewGuid().ToString(), token=new string('a',64), operation="input", moveZ=1, actions="run,block,jump", durationMs=50 };
    static void Reject(Action action)
    {
        try { action(); } catch(Exception e) when(e is InvalidOperationException || e is InvalidDataException || e is System.Runtime.Serialization.SerializationException){return;}
        throw new Exception("Invalid action accepted");
    }
    public static void Run()
    {
        var valid=Input(); Assert(Protocol.Decode(Protocol.Encode(valid)).moveZ==1,"Schema roundtrip failed");
        foreach(var action in new[]{"","W","spawn","attack,attack","jump,","Control,Space"}){var r=Input();r.actions=action;Reject(()=>Protocol.Decode(Protocol.Encode(r)));}
        foreach(var ms in new[]{0,49,5001,int.MaxValue}){var r=Input();r.durationMs=ms;Reject(()=>r.Validate());}
        foreach(var n in new[]{-1.1,1.1,double.NaN,double.PositiveInfinity}){var r=Input();r.moveX=n;Reject(()=>r.Validate());}
        foreach(var json in new[]{"\"keys\":\"W\"","\"mouseX\":1","\"buttons\":\"left\""}){
            var body=Encoding.UTF8.GetString(Protocol.Encode(valid)); Reject(()=>Protocol.Decode(Encoding.UTF8.GetBytes(body[..^1]+","+json+"}")));
        }
        foreach (var r in new[]{
            new Request{operation="look",yaw=181}, new Request{operation="look"},
            new Request{operation="action",action="slot",slot=0}, new Request{operation="action",action="inventory",slot=1},
            new Request{operation="action",action="rotate",scroll=0}, new Request{operation="ui",uiAction="drag",pointerX=0.5,pointerY=0.5},
            new Request{operation="ui",uiAction="click",pointerX=0.5}, new Request{operation="ui",uiAction="scroll",pointerX=0.5,pointerY=0.5},
            new Request{operation="ui",uiAction="click",pointerX=0.5,pointerY=1.1}
        }) {r.id=Guid.NewGuid().ToString();r.token=new string('a',64);Reject(()=>Protocol.Decode(Protocol.Encode(r)));}
        using(var controls=new InputController())
        {
            Reject(()=>controls.Start(valid));controls.Permit(true);controls.Start(valid);
            var a=controls.Sample();var b=controls.Sample();
            Assert(a.Jump && !b.Jump && a.Block && !b.Block && b.BlockHold && b.Run,"Edge/hold actions repeated or lost");
            Reject(()=>controls.Start(Input()));
            Assert(SpinWait.SpinUntil(()=>!controls.Active,2000) && controls.Reason=="completed","Lease did not expire without Unity update");
            valid.durationMs=5000;valid.moveX=1;controls.Start(valid);var d=controls.Sample();
            Assert(Math.Abs(d.X*d.X+d.Z*d.Z-1)<0.0001,"Diagonal movement was faster than normal");
            controls.Permit(false);Assert(!controls.Active,"Permission revocation failed");controls.Permit(true);
            var queued=Input();queued.controlEpoch=controls.Epoch;controls.Stop();Reject(()=>controls.Start(queued));
            controls.Start(valid);controls.Dispose();Assert(controls.Sample()==null,"Disposed control resumed");Reject(()=>controls.Start(valid));
        }
        GameInput.Validate();CheckPlugin();CheckUi();CheckEmergencyTransport();
        System.Console.WriteLine("PASS: semantic schema, lease, edge/hold timing, native permission/delay/sprint guards, game argument hook, local ownership, UI guards/events, stop epochs, placement entry, screenshot lifecycle (fixtures only)");
    }
    static void CheckEmergencyTransport()
    {
        var queue = new Dispatcher(_ => throw new Exception("Stop was queued on Unity"));
        var stops = 0;
        using var server = new Server(0, new string('a', 64), queue, _ => { }, id => { stops++; return new Response { id = id, ok = true, state = "stopped" }; });
        foreach (var token in new[] { new string('b', 64), new string('a', 64) })
        {
            using var client = new System.Net.Sockets.TcpClient("127.0.0.1", server.Port);
            client.ReceiveTimeout = 2000;
            using var stream = client.GetStream();
            var payload = Protocol.Encode(new Request { id = Guid.NewGuid().ToString(), token = token, operation = "stop" });
            stream.Write(new byte[] { 0, 0, (byte)(payload.Length >> 8), (byte)payload.Length }); stream.Write(payload);
            var result = Encoding.UTF8.GetString(Protocol.ReadFrame(stream));
            Assert(result.Contains(token[0] == 'a' ? "stopped" : "Unauthorized"), "Emergency stop transport/auth failed");
        }
        Assert(stops == 1, "Unauthenticated stop reached backend");
    }
    static void CheckPlugin()
    {
        var plugin=new Plugin();using var controller=new InputController();
        var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
        void Field(string name,object value)=>typeof(Plugin).GetField(name,flags).SetValue(plugin,value);
        var permission=new BepInEx.Configuration.ConfigEntry<bool>(false);
        Field("input",controller);Field("allowControl",permission);Field("controlReady",true);Plugin.Instance=plugin;
        Field("allowTeleport",new BepInEx.Configuration.ConfigEntry<bool>(false));
        Response Execute(Request r)=>(Response)typeof(Plugin).GetMethod("Execute",flags).Invoke(plugin,new object[]{r});
        void Update()=>typeof(Plugin).GetMethod("Update",flags).Invoke(plugin,null);
        Player.m_localPlayer=new Player();ZNet.instance=new ZNet{Host=false};Application.isFocused=true;
        Assert(!Execute(Input()).ok,"Disabled control accepted");permission.Value=true;
        var hold=Input();hold.durationMs=5000;hold.actions="attack,block,run,jump";
        Assert(Execute(hold).ok,"Joining player cannot control own character");
        var first=Hook(Player.m_localPlayer);var next=Hook(Player.m_localPlayer);
        Assert(first.Frame.Z==1 && first.Frame.Attack && first.Frame.Jump && first.Frame.Block && !first.AutoRun,"Game arguments not overridden");
        Assert(next.Frame.AttackHold && !next.Frame.Attack && !next.Frame.Jump && next.Frame.BlockHold,"Physics tick lost hold or repeated one-shot");
        Assert(Hook(new Player()).Frame.X==0.25f,"Remote player input was modified");
        Execute(new Request{operation="look",yaw=30,pitch=10});
        Assert(Player.m_localPlayer.Look.x==30 && Player.m_localPlayer.Look.y==-10 && controller.Active,"Look used wrong degrees or replaced lease");
        Execute(new Request{operation="stop"});
        Assert(Hook(Player.m_localPlayer).Frame.X==0,"Stop did not neutralize previous internal control");
        Assert(Hook(Player.m_localPlayer).Frame.X==0.25f,"Normal physical control did not resume");
        PlayerController.HasInputDelay=true;
        var delayed=Input();delayed.durationMs=5000;delayed.actions="attack,secondary,block,jump,crouch,run,dodge";
        Execute(delayed);var delayFrame=Hook(Player.m_localPlayer).Frame;
        Assert(delayFrame.Z==1 && delayFrame.Run && controller.Active,"Native input delay incorrectly stopped movement or sprint");
        Assert(!delayFrame.Attack && !delayFrame.AttackHold && !delayFrame.Secondary && !delayFrame.SecondaryHold && !delayFrame.Block && !delayFrame.BlockHold && !delayFrame.Jump && !delayFrame.Crouch && !delayFrame.Dodge,"Native input delay was bypassed");
        PlayerController.HasInputDelay=false;delayFrame=Hook(Player.m_localPlayer).Frame;
        Assert(delayFrame.AttackHold && delayFrame.SecondaryHold && delayFrame.BlockHold && !delayFrame.Attack && !delayFrame.Jump && !delayFrame.Crouch && !delayFrame.Dodge,"Delayed one-shot actions replayed or held actions did not resume");
        Player.m_localPlayer.Controller.InputAllowed=false;
        var blocked=Hook(Player.m_localPlayer).Frame;
        Assert(!controller.Active && controller.Reason=="game_input_blocked" && blocked.Z==0 && !blocked.AttackHold,"Native controller refusal did not cancel and neutralize active input");
        Assert(!Execute(Input()).ok && !Execute(new Request{operation="look",yaw=1}).ok,"Native controller refusal accepted a new input/look");
        Player.m_localPlayer.Controller.InputAllowed=true;
        Player.m_localPlayer.gameObject.Components.Remove(typeof(PlayerController));
        Assert(!Execute(Input()).ok,"Missing native controller accepted movement");
        Player.m_localPlayer.gameObject.Components[typeof(PlayerController)]=Player.m_localPlayer.Controller;
        Execute(hold);Assert(Hook(Player.m_localPlayer).Frame.Run,"Sprint did not start with stamina");
        Player.m_localPlayer.Stamina=0;Assert(!Hook(Player.m_localPlayer).Frame.Run,"Exhaustion did not stop sprint");
        Player.m_localPlayer.Stamina=10;Assert(!Hook(Player.m_localPlayer).Frame.Run,"Held sprint resumed without a new press after exhaustion");
        Execute(new Request{operation="stop"});Execute(hold);
        Assert(Hook(Player.m_localPlayer).Frame.Run,"New sprint request did not restore sprint after recovery");
        Execute(new Request{operation="stop"});Hook(Player.m_localPlayer);
        Player.m_localPlayer.ToggleBlock=true;Player.m_localPlayer.m_autoRun=true;Execute(hold);
        var toggle=Hook(Player.m_localPlayer);Assert(toggle.Frame.Block && !Player.m_localPlayer.m_autoRun,"Toggle block/autorun were not adapted");
        Player.m_localPlayer.SetControls(Vector3.zero,false,false,false,false,toggle.Frame.Block,true,false,false,false,false);
        Assert(Player.m_localPlayer.Blocking && !Hook(Player.m_localPlayer).Frame.Block,"Held toggle block repeated its toggle");
        PlayerController.HasInputDelay=true;
        Execute(new Request{operation="stop"});toggle=Hook(Player.m_localPlayer);
        Player.m_localPlayer.SetControls(Vector3.zero,false,false,false,false,toggle.Frame.Block,false,false,false,false,false);
        Assert(!Player.m_localPlayer.Blocking,"Stop left toggle blocking enabled during native input delay");Player.m_localPlayer.ToggleBlock=false;
        PlayerController.HasInputDelay=false;
        Player.m_localPlayer.View.Owner=false;Assert(!Execute(Input()).ok,"Non-owned player control accepted");Player.m_localPlayer.View.Owner=true;
        Console.Visible=true;Assert(!Execute(Input()).ok,"Console control accepted");Console.Visible=false;
        Menu.Visible=true;Assert(!Execute(Input()).ok,"Menu control accepted");Menu.Visible=false;
        TextInput.Visible=true;Assert(!Execute(Input()).ok,"Text field control accepted");TextInput.Visible=false;
        Chat.instance=new Chat{Focus=true};Assert(!Execute(Input()).ok,"Chat control accepted");Chat.instance=null;
        Player.m_localPlayer.Dead=true;Assert(!Execute(Input()).ok,"Dead control accepted");Player.m_localPlayer.Dead=false;
        InventoryGui.Visible=true;Assert(!Execute(Input()).ok,"Movement leaked into inventory");InventoryGui.Visible=false;
        Execute(hold);Application.isFocused=false;Hook(Player.m_localPlayer);Assert(!controller.Active,"Focus loss retained lease");Application.isFocused=true;
        Execute(new Request{operation="action",action="slot",slot=1});Assert(Player.m_localPlayer.Hotbar==1,"Hotbar did not use game method");
        Execute(new Request{operation="action",action="interact"});Assert(Player.m_localPlayer.Interactions==1,"Interact did not use game method");
        Player.m_localPlayer.NativeInputAllowed=false;
        foreach(var name in new[]{"interact","slot","hide","guardian","place","rotate","build-menu"})
            Assert(!Execute(new Request{operation="action",action=name,slot=name=="slot"?1:null,scroll=name=="rotate"?1:null}).ok,"Native player refusal accepted "+name);
        Assert(Player.m_localPlayer.Interactions==1,"Refused interaction changed the game");
        Execute(new Request{operation="action",action="inventory"});Assert(InventoryGui.Visible,"Inventory game method not invoked");
        var idleEpoch=controller.Epoch;Hook(Player.m_localPlayer);Hook(Player.m_localPlayer);
        Assert(controller.Epoch==idleEpoch,"Idle physics ticks invalidated queued UI actions");
        Execute(new Request{operation="action",action="inventory"});Assert(!InventoryGui.Visible,"Inventory did not close");
        Player.m_localPlayer.NativeInputAllowed=true;
        foreach(var dodge in new[]{false,true})
        {
            Player.m_localPlayer.Attacking=!dodge;Player.m_localPlayer.Dodging=dodge;
            try {GameActions.Execute(Player.m_localPlayer,new Request{action="hide"});throw new Exception("Hide bypassed attack/dodge guard");}
            catch(ActionRefusedException){}
        }
        Player.m_localPlayer.Attacking=Player.m_localPlayer.Dodging=false;
        Player.m_localPlayer.Inventory.Items.Clear();
        try { GameActions.Execute(Player.m_localPlayer,new Request{action="slot",slot=1});throw new Exception("Empty hotbar accepted"); }
        catch(ActionRefusedException e){Assert(e.Message=="Hotbar slot is empty","Expected refusal lost its cause");}
        Player.m_localPlayer.Inventory.Items.Add(new ItemDrop.ItemData());
        Player.m_localPlayer.PlaceMode=true;
        Execute(new Request{operation="action",action="build-menu"});Assert(Hud.Selector,"Build selector did not open");
        Player.m_localPlayer.NativeInputAllowed=false;PlayerController.HasInputDelay=true;
        Assert(Execute(new Request{operation="action",action="build-menu"}).ok && !Hud.Selector,"Native refusal trapped an already-open build selector");
        Player.m_localPlayer.NativeInputAllowed=true;
        try {GameActions.Execute(Player.m_localPlayer,new Request{action="build-menu"});throw new Exception("Build selector bypassed native delay");}
        catch(ActionRefusedException){}
        PlayerController.HasInputDelay=false;
        Hud.Radial=true;
        try {GameActions.Execute(Player.m_localPlayer,new Request{action="build-menu"});throw new Exception("Build selector bypassed radial guard");}
        catch(ActionRefusedException){}
        Hud.Radial=false;
        Execute(new Request{operation="action",action="place"});Assert(Player.m_localPlayer.PlacementCalls==1 && Player.m_localPlayer.Placements==0,"Placement bypassed the original game's validation");
        Player.m_localPlayer.CanPlace=true;Execute(new Request{operation="action",action="place"});Assert(Player.m_localPlayer.Placements==1,"Guarded placement path not reached");
        Execute(new Request{operation="action",action="rotate",scroll=2});Assert(Player.m_localPlayer.m_placeRotation==2,"Build rotation was not internal");
        var observation = new Request { id = Guid.NewGuid().ToString(), operation = "observe" };
        Assert(Execute(observation).state == "capture_pending" && plugin.Coroutines.Count == 1, "Capture not scheduled");
        Execute(observation); Assert(plugin.Coroutines.Count == 1, "Duplicate capture scheduled");
        var routine = plugin.Coroutines[0]; Assert(routine.MoveNext(), "Capture did not wait for end of frame");
        Assert(!routine.MoveNext(), "Capture did not finish");
        Assert(plugin.Destroyed.Count == 2 && UnityEngine.RenderTexture.Releases > 0, "Screenshot resources not released");
        var observed = Execute(observation);
        Assert(observed.image != null && observed.imageWidth == 1280 && observed.imageHeight == 720 && observed.inventory.Length == 1, "Screenshot/observation fixture failed");
        Player.m_localPlayer = new Player();
        Assert(Execute(observation).state == "capture_pending", "Old character screenshot leaked into new character");
        var releases = UnityEngine.RenderTexture.Releases; var destroys = plugin.Destroyed.Count;
        UnityEngine.Texture2D.FailEncode = true;
        var failedCapture = plugin.Coroutines.Last(); failedCapture.MoveNext(); failedCapture.MoveNext();
        UnityEngine.Texture2D.FailEncode = false;
        Assert(UnityEngine.RenderTexture.Releases == releases + 1 && plugin.Destroyed.Count == destroys + 2, "Failed capture leaked textures");
        var failure = Execute(observation);
        Assert(failure.state == "capture_failed" && failure.captureError != null, "Capture failure not reported");
        var recovery = Execute(observation);
        Assert(recovery.state == "capture_pending" && recovery.captureError == null, "Stale error blocked screenshot retry");
        var retried = plugin.Coroutines.Last(); retried.MoveNext(); retried.MoveNext();
        Assert(Execute(observation).image != null, "Screenshot did not recover after transient failure");
        Execute(hold);Player.m_localPlayer=new Player();Update();Assert(!controller.Active && controller.Reason=="player_changed","Character switch retained old controls");
        var oldCharacterAction=new Request{operation="action",action="inventory",controlEpoch=controller.Epoch};
        Player.m_localPlayer=new Player();Update();Assert(!Execute(oldCharacterAction).ok,"Idle character switch accepted an old queued action");
        Execute(hold);permission.Value=false;Execute(new Request{operation="stop"});Update();Assert(!Execute(Input()).ok,"Revoked permission accepted");
        permission.Value=true;UnityEngine.Input.Panic=true;Update();Assert(!permission.Value,"F12 failed to revoke permission");UnityEngine.Input.Panic=false;
        Plugin.Instance=null;
    }
    static (ControlFrame Frame,bool AutoRun) Hook(Player player)
    {
        var move=new Vector3(0.25f,0,0);bool attack=false,attackHold=false,secondary=false,secondaryHold=false,block=false,blockHold=false,jump=false,crouch=false,run=false,autoRun=true,dodge=false;
        GameInput.Prefix(player,ref move,ref attack,ref attackHold,ref secondary,ref secondaryHold,ref block,ref blockHold,ref jump,ref crouch,ref run,ref autoRun,ref dodge);
        return(new ControlFrame{X=move.x,Z=move.z,Attack=attack,AttackHold=attackHold,Secondary=secondary,SecondaryHold=secondaryHold,Block=block,BlockHold=blockHold,Jump=jump,Crouch=crouch,Run=run,Dodge=dodge},autoRun);
    }
    static void CheckUi()
    {
        EventSystem.current=new EventSystem();var target=new GameObject();target.Components[typeof(Canvas)]=new Canvas();
        EventSystem.current.Hits.Add(new RaycastResult{gameObject=target});
        var events=new List<string>();
        foreach(var type in new[]{typeof(IPointerClickHandler),typeof(IPointerDownHandler),typeof(IPointerUpHandler),typeof(IScrollHandler)})
            target.Handlers[type]=d=>events.Add(type.Name);
        PointerEventData received=null;target.Handlers[typeof(IPointerClickHandler)]=d=>{received=(PointerEventData)d;events.Add("click");};
        Request R(string a)=>new(){operation="ui",uiAction=a,pointerX=0.2,pointerY=0.3};
        GameUi.Execute(R("click"));Assert(events.SequenceEqual(new[]{"IPointerDownHandler","IPointerUpHandler","click"}),"UI click order incorrect");
        Assert(Math.Abs(received.position.x-319.8)<0.1 && Math.Abs(received.position.y-629.3)<0.1,"UI top-left coordinate conversion incorrect");
        events.Clear(); var scroll=R("scroll");scroll.scroll=1;GameUi.Execute(scroll);
        Assert(events.SequenceEqual(new[]{"IScrollHandler"}),"UI scroll was not dispatched internally");
        EventSystem.current.Hits.Clear();Reject(()=>GameUi.Execute(R("click")));EventSystem.current=null;
    }
}
