using System.Runtime.InteropServices;
using System.Text;
using ValheimCliBridge;

public sealed class FakeInput : IInputBackend
{
    public bool Supported { get; set; } = true;
    public bool Foreground { get; set; } = true;
    public bool PanicPressed { get; set; }
    public bool ModifiersPressed { get; set; }
    public bool FailPress, FailRelease;
    public readonly List<string> Events = new();
    public void Key(ushort key, bool down) { Events.Add($"key:{key}:{down}"); if (down && FailPress || !down && FailRelease) throw new InvalidOperationException("injected backend failure"); }
    public void Button(string button, bool down) { Events.Add($"button:{button}:{down}"); }
    public void Move(Request request) { Events.Add("move"); }
}

static class InputTests
{
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static Request Input() => new() { id = Guid.NewGuid().ToString(), token = new string('a', 64), operation = "input", keys = "W,Shift", buttons = "right", durationMs = 50 };
    static void Reject(Action action)
    {
        try { action(); } catch (Exception e) when (e is InvalidOperationException || e is InvalidDataException || e is System.Runtime.Serialization.SerializationException) { return; }
        throw new Exception("Invalid input accepted");
    }
    public static void Run()
    {
        Assert(Marshal.SizeOf(typeof(WindowsInput.NativeInput)) == (IntPtr.Size == 8 ? 40 : 28), "Incorrect SendInput ABI size");
        var valid = Input();
        Assert(Protocol.Decode(Protocol.Encode(valid)).keys == "W,Shift", "Input schema roundtrip failed");
        foreach (var keys in new[] { "Alt,Tab", "Win", "F12", "F5", "W,W", "Control,Escape", "Control,A", "W," })
        {
            var invalid = Input(); invalid.keys = keys;
            Reject(() => Protocol.Decode(Protocol.Encode(invalid)));
        }
        foreach (var duration in new[] { -1, 0, 49, 5001, int.MaxValue })
        {
            var invalid = Input(); invalid.durationMs = duration;
            Reject(() => Protocol.Decode(Protocol.Encode(invalid)));
        }
        var number = Encoding.UTF8.GetString(Protocol.Encode(valid)).Replace("\"durationMs\":50", "\"durationMs\":50.5");
        Reject(() => Protocol.Decode(Encoding.UTF8.GetBytes(number)));
        var wrongOperation = Input(); wrongOperation.operation = "stop";
        Reject(() => Protocol.Decode(Protocol.Encode(wrongOperation)));
        var pointer = Input(); pointer.pointerX = 0.5;
        Reject(() => Protocol.Decode(Protocol.Encode(pointer)));
        pointer.pointerY = double.PositiveInfinity;
        Reject(() => InputSpec.Validate(pointer));
        pointer.pointerY = 0.5; pointer.mouseX = 0;
        Reject(() => InputSpec.Validate(pointer));
        foreach (var motion in new[] { int.MinValue, -2001, 2001, int.MaxValue })
        {
            var invalid = Input(); invalid.mouseX = motion;
            Reject(() => InputSpec.Validate(invalid));
        }
        var fake = new FakeInput();
        using (var controller = new InputController(fake))
        {
            Reject(() => controller.Start(valid));
            controller.Permit(true);
            fake.Foreground = false; Reject(() => controller.Start(valid));
            fake.Foreground = true; fake.ModifiersPressed = true; Reject(() => controller.Start(valid));
            fake.ModifiersPressed = false; fake.Supported = false; Reject(() => controller.Start(valid));
            fake.Supported = true;
            Assert(fake.Events.Count == 0, "Rejected input reached native backend");
            controller.Start(valid);
            Assert(controller.Active && controller.Id == valid.id, "Input never became active");
            controller.Motion(new Request { operation = "mouse", mouseX = 120 });
            Assert(controller.Active && controller.Id == valid.id, "Mouse changed the running lease");
            Reject(() => controller.Start(Input()));
            Assert(SpinWait.SpinUntil(() => !controller.Active, 2000), "Watchdog did not release without Unity/CLI updates");
            Assert(fake.Events.Contains("key:87:False") && fake.Events.Contains("key:16:False") && fake.Events.Contains("button:right:False"), "Watchdog missed held inputs");
            Assert(controller.Reason == "completed", "Normal lease did not complete");
            valid.durationMs = 5000;
            controller.Start(valid); fake.Foreground = false; controller.Tick();
            Assert(!controller.Active && controller.Reason == "focus_lost", "Focus loss did not stop input");
            fake.Foreground = true; controller.Start(Input()); controller.Permit(false);
            Assert(!controller.Active && controller.Reason == "permission_revoked", "Revoked permission left keys held");
            controller.Permit(true); controller.Start(valid); fake.PanicPressed = true; controller.Tick();
            Assert(!controller.Active && controller.Reason == "panic", "F12 did not stop input");
            fake.PanicPressed = false; Reject(() => controller.Start(valid));
            controller.Permit(false); controller.Permit(true);
            controller.Start(valid); controller.Stop(); controller.Stop();
            Assert(!controller.Active, "Repeated stop failed");
            fake.FailPress = true;
            try { controller.Start(valid); throw new Exception("Backend failure hidden"); } catch (IOException) { }
            Assert(!controller.Active && controller.Reason == "input_failed", "Partial press did not release");
            fake.FailPress = false; controller.Start(valid); fake.FailRelease = true; controller.Stop();
            Assert(controller.Active && controller.Reason == "release_failed", "Failed release was reported as stopped");
            fake.FailRelease = false; controller.Tick();
            Assert(!controller.Active, "Watchdog did not retry failed release");
            var queued = Input(); queued.controlEpoch = controller.Epoch;
            controller.Stop(); Reject(() => controller.Start(queued));
            queued.controlEpoch = controller.Epoch; controller.Start(queued); controller.Stop();
            controller.Start(valid);
        }
        Assert(fake.Events.Last() == "key:87:False", "Disposal did not release keys");
        CheckPlugin();
        CheckEmergencyTransport();
        System.Console.WriteLine("PASS: input validation, native layout, bounded lease, busy, focus, panic, permission, rollback, release retry, disposal, screenshot lifecycle (fixtures only)");
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
        var plugin = new Plugin(); var fake = new FakeInput();
        using var controller = new InputController(fake);
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        void Field(string name, object value) => typeof(Plugin).GetField(name, flags).SetValue(plugin, value);
        var permission = new BepInEx.Configuration.ConfigEntry<bool>(false);
        Field("backend", fake); Field("input", controller); Field("allowControl", permission);
        Field("allowTeleport", new BepInEx.Configuration.ConfigEntry<bool>(false));
        Response Execute(Request r) => (Response)typeof(Plugin).GetMethod("Execute", flags).Invoke(plugin, new object[] { r });
        Player.m_localPlayer = new Player(); ZNet.instance = new ZNet { Host = false };
        Assert(!Execute(Input()).ok, "Disabled character control accepted");
        permission.Value = true;
        Assert(Execute(Input()).ok, "Joining player cannot control own character");
        controller.Stop();
        fake.FailPress = true;
        var failedInput = Input(); var failed = Execute(failedInput);
        Assert(!failed.ok && failed.id == failedInput.id && failed.state != "cancelled" && failed.error.Contains("may have started"), "Native input failure lost request ID or allowed unsafe retry");
        fake.FailPress = false;
        Console.Visible = true; Assert(!Execute(Input()).ok, "Input accepted in console"); Console.Visible = false;
        Menu.Visible = true; Assert(!Execute(Input()).ok, "Input accepted in system menu"); Menu.Visible = false;
        Chat.instance = new Chat { Focus = true }; Assert(!Execute(Input()).ok, "Input accepted in chat"); Chat.instance = null;
        Player.m_localPlayer.Dead = true; Assert(!Execute(Input()).ok, "Dead player control accepted"); Player.m_localPlayer.Dead = false;
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
        Execute(Input()); Player.m_localPlayer = new Player();
        typeof(Plugin).GetMethod("Update", flags).Invoke(plugin, null);
        Assert(!controller.Active && controller.Reason == "player_changed", "Character switch kept old input held");
        Execute(Input()); permission.Value = false;
        Assert(Execute(new Request { operation = "stop" }).state == "stopped" && !controller.Active, "Stop requires permission");
        typeof(Plugin).GetMethod("Update", flags).Invoke(plugin, null);
        Assert(!Execute(Input()).ok, "Update failed to revoke control");
        permission.Value = true; fake.PanicPressed = true;
        typeof(Plugin).GetMethod("Update", flags).Invoke(plugin, null);
        Assert(!permission.Value, "F12 did not revoke config permission");
    }
}
