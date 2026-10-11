using System.Reflection;
using ValheimCliBridge;

static class ConsoleTests
{
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static Request Command(string line = "help 2 4") => new() { id = Guid.NewGuid().ToString(), token = new string('a', 64), operation = "console", command = line };
    public static void Run()
    {
        Assert(Protocol.Decode(Protocol.Encode(Command())).command == "help 2 4", "Command roundtrip failed");
        foreach (var line in new[] { null, "", " help", "help ", "help\ninfo", "help\tinfo", new string('x', 513) })
        {
            try { Protocol.Decode(Protocol.Encode(Command(line))); }
            catch (InvalidDataException) { continue; }
            catch (System.Runtime.Serialization.SerializationException) { continue; }
            throw new Exception("Invalid console line accepted");
        }
        var mixed = Command(); mixed.operation = "status";
        try { mixed.Validate(); throw new Exception("Console field accepted on status"); } catch (InvalidDataException) { }
        var plugin = new Plugin();
        var permission = new BepInEx.Configuration.ConfigEntry<bool>(false);
        void Set(string name, object value) => typeof(Plugin).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(plugin, value);
        Set("allowConsole", permission); Set("allowTeleport", new BepInEx.Configuration.ConfigEntry<bool>(false)); Set("controlReady", true);
        var execute = typeof(Plugin).GetMethod("Execute", BindingFlags.NonPublic | BindingFlags.Instance);
        Response Execute() => (Response)execute.Invoke(plugin, new object[] { Command() });
        var previousConsole = Console.instance;
        var previousFocus = UnityEngine.Application.isFocused;
        Console.instance = new Console(); Console.Visible = false;
        var calls = 0;
        Console.instance.Handler = (text, silent, skip) =>
        {
            calls++;
            Assert(text == "help 2 4" && !silent && !skip, "Native command or permission flags changed");
            new Terminal().AddString("unrelated output");
            Console.instance.AddString("native result");
        };
        try
        {
            UnityEngine.Application.isFocused = true;
            Assert(!Execute().ok && calls == 0, "Default-disabled console executed");
            permission.Value = true; UnityEngine.Application.isFocused = false;
            Assert(!Execute().ok && calls == 0, "Unfocused console executed");
            UnityEngine.Application.isFocused = true; Console.instance.Enabled = false;
            Assert(!Execute().ok && calls == 0, "Disabled native console executed");
            Console.instance.Enabled = true;
            var response = Execute();
            Assert(response.ok && response.state == "submitted" && calls == 1 && response.consoleOutput.SequenceEqual(new[] { "native result" }), "Native invocation/output failed");
            Assert(!Console.Visible, "Console UI opened");
            Console.instance.Handler = (_, _, _) => { for (int i = 0; i < 40; i++) Console.instance.AddString(new string('\0', 1000)); };
            response = Execute();
            Assert(response.consoleOutputTruncated && response.consoleOutput.Length == 32 && response.consoleOutput.All(s => s.Length == 256), "Output was not bounded");
            Assert(Protocol.Encode(response).Length < Protocol.MaxFrame, "Escaped console output exceeded frame limit");
            Console.instance.Handler = (_, _, _) => Console.instance.AddString(new string('x', 255) + "😀");
            response = Execute();
            Assert(response.consoleOutput[0].Length == 255 && Protocol.Encode(response).Length < Protocol.MaxFrame, "Truncation split a surrogate pair");
            Console.instance.Handler = (_, _, _) => throw new InvalidOperationException();
            try { Execute(); throw new Exception("Native exception hidden"); } catch (TargetInvocationException) { }
            Console.instance.AddString("late output");
            Console.instance.Handler = (_, _, _) => Console.instance.AddString("not valid in the current context");
            response = Execute();
            Assert(response.state == "submitted" && response.consoleOutput.Length == 1, "Prior output leaked or native refusal misreported as applied");
            var control = new BepInEx.Configuration.ConfigEntry<bool>(true); Set("allowControl", control);
            UnityEngine.Input.Panic = true;
            typeof(Plugin).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(plugin, null);
            Assert(!permission.Value && !control.Value, "F12 did not revoke both permissions");
        }
        finally { Console.instance = previousConsole; UnityEngine.Application.isFocused = previousFocus; UnityEngine.Input.Panic = false; }
        System.Console.WriteLine("PASS: console schema, native flags, permissions/focus/F12, bounded per-call output and exception cleanup (fixtures)");
    }
}
