using System.Text;
using ValheimCliBridge;

static class Program
{
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    static Request Valid(string operation = "status") => new Request { id = Guid.NewGuid().ToString(), token = new string('a', 64), operation = operation };
    static void Invalid(string json)
    {
        try { Protocol.Decode(Encoding.UTF8.GetBytes(json)); }
        catch { return; }
        throw new Exception("Accepted invalid JSON: " + json);
    }
    static async Task Main(string[] args)
    {
        if (args.Contains("--serve"))
        {
            var dispatcher = new Dispatcher(request => new Response { id = request.id, ok = true, state = request.operation == "teleport" ? "started" : "observed", version = "0.1.0", players = new[] { "fixture-player" } });
            using var server = new Server(0, new string('a', 64), dispatcher);
            System.Console.WriteLine(server.Port);
            while (true) { dispatcher.Pump(); await Task.Delay(5); }
        }
        var valid = Valid();
        Assert(Protocol.Decode(Protocol.Encode(valid)).operation == "status", "Valid JSON rejected");
        var json = Encoding.UTF8.GetString(Protocol.Encode(valid));
        Invalid(json + "garbage");
        Invalid(json.Substring(0, json.Length - 1) + ",\"extra\":true}");
        Invalid(json.Substring(0, json.Length - 1) + ",\"__type\":\"Request:#ValheimCliBridge\"}");
        Invalid("{\"__type\":\"Request:#ValheimCliBridge\"," + json.Substring(1));
        Invalid(json.Replace("status", "pos ; spawn Troll"));
        Invalid(json.Replace("status", "teleport"));
        Invalid(json.Substring(0, json.Length - 1) + ",\"x\":\"1\"}");
        Invalid(json.Replace("\"operation\":\"status\"", "\"operation\":\"status\",\"operation\":\"teleport\""));
        Assert(Protocol.Authenticated(valid.token, valid.token), "Auth positive");
        Assert(!Protocol.Authenticated(valid.token, new string('b', 64)), "Wrong token accepted");
        foreach (var bytes in new[] { new byte[] { 0, 0, 0, 0 }, new byte[] { 0, 1, 0, 1 }, new byte[] { 0, 0, 0, 2, 1 } })
        {
            try { Protocol.ReadFrame(new MemoryStream(bytes)); throw new Exception("Accepted invalid frame"); }
            catch (InvalidDataException) { }
            catch (EndOfStreamException) { }
        }
        var runs = 0;
        var queue = new Dispatcher(r => { runs++; return new Response { id = r.id, ok = true }; });
        var expired = await Task.Run(() => queue.Run(Valid(), 20));
        queue.Pump();
        Assert(!expired.ok && runs == 0, "Timed-out action executed later");
        var work = Task.Run(() => queue.Run(Valid()));
        await Task.Delay(20);
        queue.Pump();
        Assert((await work).ok && runs == 1, "Normal operation failed");
        queue.Stop();
        Assert(!queue.Run(Valid()).ok, "Stopped queue accepted work");
        var failure = new Dispatcher(r => throw new InvalidOperationException());
        var failed = Task.Run(() => failure.Run(Valid()));
        await Task.Delay(20); failure.Pump();
        Assert(!(await failed).ok, "Exception reported success");
        var blocked = new Dispatcher(r => { Thread.Sleep(60); return new Response { id = r.id, ok = true }; });
        var uncertain = Task.Run(() => blocked.Run(Valid(), 30));
        await Task.Delay(10); blocked.Pump();
        Assert(!(await uncertain).ok, "Already-started timeout reported success");
        var plugin = new Plugin();
        var permission = new BepInEx.Configuration.ConfigEntry<bool>(false);
        typeof(Plugin).GetField("allowTeleport", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(plugin, permission);
        var execute = typeof(Plugin).GetMethod("Execute", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Response Execute(Request r) => (Response)execute.Invoke(plugin, new object[] { r });
        Assert(Execute(Valid()).ok && !Execute(Valid()).inWorld, "Menu status failed");
        var teleport = Valid("teleport"); teleport.x = 1; teleport.y = 2; teleport.z = 3;
        Assert(!Execute(teleport).ok, "No-player teleport accepted");
        Player.m_localPlayer = new Player(); ZNet.instance = new ZNet { Host = true };
        Assert(Execute(Valid("players")).players.Length == 1, "Null player object broke listing");
        Assert(!Execute(teleport).ok, "Disabled teleport accepted");
        permission.Value = true; ZNet.instance.Host = false;
        Assert(!Execute(teleport).ok, "Joining client write accepted");
        ZNet.instance.Host = true; Player.m_localPlayer.Dead = true;
        Assert(!Execute(teleport).ok, "Dead player teleport accepted");
        Player.m_localPlayer.Dead = false; Player.m_localPlayer.AcceptTeleport = false;
        Assert(!Execute(teleport).ok, "Game refusal reported success");
        Player.m_localPlayer.AcceptTeleport = true;
        var started = Execute(teleport);
        Assert(started.ok && started.state == "started" && started.destination.x == 1 && started.position.x == 0, "Teleport arrival misreported");
        Assert(!Execute(teleport).ok, "Concurrent teleport accepted");
        System.Console.WriteLine("PASS: schema, auth, framing, cancelled timeout, normal dispatch, stop, execution failure, unknown-outcome timeout, host/permission/game-refusal/arrival policies with game fixture");
    }
}
