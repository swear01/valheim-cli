using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace ValheimCliBridge
{
    public static class InputSpec
    {
        public static readonly Dictionary<string, ushort> Keys = MakeKeys();
        private static Dictionary<string, ushort> MakeKeys()
        {
            var keys = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
            {
                { "Space", 32 }, { "Shift", 16 }, { "Control", 17 }, { "Tab", 9 },
                { "Escape", 27 }, { "Enter", 13 }, { "Backspace", 8 },
                { "Left", 37 }, { "Up", 38 }, { "Right", 39 }, { "Down", 40 }
            };
            foreach (var c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789") keys.Add(c.ToString(), c);
            return keys;
        }
        public static string[] Split(string value) => string.IsNullOrEmpty(value) ? new string[0] : value.Split(',');
        public static void Validate(Request r)
        {
            var keys = Split(r.keys);
            var buttons = Split(r.buttons);
            if (keys.Length > 8 || keys.Any(k => !Keys.ContainsKey(k)) || keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != keys.Length ||
                buttons.Any(b => b != "left" && b != "right" && b != "middle") || buttons.Distinct().Count() != buttons.Length)
                throw new InvalidDataException("Unsupported or repeated input");
            if (keys.Contains("Control", StringComparer.OrdinalIgnoreCase) && keys.Any(k => !new[] { "Control", "Space" }.Contains(k, StringComparer.OrdinalIgnoreCase)))
                throw new InvalidDataException("Control may only be combined with Space (dodge)");
            if (r.operation == "mouse" ? r.keys != null || r.buttons != null || r.durationMs.HasValue : !r.durationMs.HasValue || r.durationMs < 50 || r.durationMs > 5000)
                throw new InvalidDataException("Mouse takes motion only; input requires duration 50..5000 ms");
            if (Math.Abs((long)(r.mouseX ?? 0)) > 2000 || Math.Abs((long)(r.mouseY ?? 0)) > 2000 || Math.Abs((long)(r.scroll ?? 0)) > 10)
                throw new InvalidDataException("Input duration or motion outside bounds");
            if (r.pointerX.HasValue != r.pointerY.HasValue || r.pointerX.HasValue &&
                (double.IsNaN(r.pointerX.Value) || double.IsNaN(r.pointerY.Value) || r.pointerX < 0 || r.pointerX > 1 || r.pointerY < 0 || r.pointerY > 1))
                throw new InvalidDataException("Pointer requires normalized x/y in 0..1");
            if (r.pointerX.HasValue && (r.mouseX.HasValue || r.mouseY.HasValue)) throw new InvalidDataException("Choose pointer or relative mouse motion");
            if (keys.Length == 0 && buttons.Length == 0 && (r.mouseX ?? 0) == 0 && (r.mouseY ?? 0) == 0 && (r.scroll ?? 0) == 0 && !r.pointerX.HasValue)
                throw new InvalidDataException("Empty input");
        }
    }

    public interface IInputBackend
    {
        bool Supported { get; }
        bool Foreground { get; }
        bool PanicPressed { get; }
        bool ModifiersPressed { get; }
        void Key(ushort key, bool down);
        void Button(string button, bool down);
        void Move(Request request);
    }

    public sealed class InputController : IDisposable
    {
        private readonly IInputBackend backend;
        private readonly object gate = new object();
        private readonly System.Threading.Timer watchdog;
        private string[] heldKeys = new string[0], heldButtons = new string[0];
        private long expires;
        private long epoch;
        private bool active, permitted, disposed, panicLatched;
        private string reason = "idle";
        private string id;
        public InputController(IInputBackend backend)
        {
            this.backend = backend;
            watchdog = new System.Threading.Timer(_ => Tick(), null, 20, 20);
        }
        public bool Active { get { lock (gate) return active; } }
        public string Reason { get { lock (gate) return reason; } }
        public string Id { get { lock (gate) return id; } }
        public bool PanicLatched { get { lock (gate) return panicLatched; } }
        public long Epoch { get { lock (gate) return epoch; } }
        public void Permit(bool value)
        {
            lock (gate)
            {
                if (permitted && !value) epoch++;
                permitted = value;
                if (!value) { StopLocked("permission_revoked"); panicLatched = false; }
            }
        }
        public void Start(Request request)
        {
            InputSpec.Validate(request);
            lock (gate)
            {
                if (disposed || !permitted) throw new InvalidOperationException("AllowControl=true is required");
                if (request.controlEpoch.HasValue && request.controlEpoch != epoch) throw new InvalidOperationException("Input was cancelled by stop or loss of control");
                if (!backend.Supported) throw new InvalidOperationException("Character input requires Windows");
                if (backend.PanicPressed) panicLatched = true;
                if (!backend.Foreground || panicLatched || backend.ModifiersPressed) throw new InvalidOperationException("Game must be foreground, with F12 and physical modifiers released; re-enable permission after F12");
                if (active) throw new InvalidOperationException("Input busy; observe or stop before another input");
                heldKeys = InputSpec.Split(request.keys);
                heldButtons = InputSpec.Split(request.buttons);
                active = true;
                id = request.id;
                expires = Stopwatch.GetTimestamp() + request.durationMs.Value * Stopwatch.Frequency / 1000;
                try
                {
                    backend.Move(request);
                    foreach (var key in heldKeys) backend.Key(InputSpec.Keys[key], true);
                    foreach (var button in heldButtons) backend.Button(button, true);
                    reason = "running";
                }
                catch (Exception error) { epoch++; StopLocked("input_failed"); throw new IOException("Input may have started; inspect state", error); }
            }
        }
        public void Tick()
        {
            lock (gate)
            {
                if (disposed) return;
                try
                {
                    if (backend.PanicPressed)
                    {
                        if (!panicLatched) epoch++;
                        panicLatched = true;
                        StopLocked("panic");
                    }
                    else if (!active) return;
                    else if (reason == "release_failed") StopLocked("release_retry");
                    else if (!backend.Foreground) { epoch++; StopLocked("focus_lost"); }
                    else if (Stopwatch.GetTimestamp() >= expires) StopLocked("completed");
                }
                catch { epoch++; StopLocked("backend_failed"); }
            }
        }
        public void Motion(Request request)
        {
            InputSpec.Validate(request);
            lock (gate)
            {
                if (disposed || !permitted || !backend.Supported || !backend.Foreground || backend.PanicPressed || panicLatched || !active && backend.ModifiersPressed)
                    throw new InvalidOperationException("Mouse requires permission and foreground game; release physical modifiers");
                if (request.controlEpoch.HasValue && request.controlEpoch != epoch) throw new InvalidOperationException("Mouse was cancelled by stop or loss of control");
                try { backend.Move(request); }
                catch (Exception error) { epoch++; StopLocked("input_failed"); throw new IOException("Mouse may have moved; inspect state", error); }
            }
        }
        public void Stop(string why = "stopped") { lock (gate) { epoch++; StopLocked(why); } }
        private void StopLocked(string why)
        {
            if (!active) return;
            var failed = false;
            foreach (var button in heldButtons.Reverse()) { try { backend.Button(button, false); } catch { failed = true; } }
            foreach (var key in heldKeys.Reverse()) { try { backend.Key(InputSpec.Keys[key], false); } catch { failed = true; } }
            // Keep a failed release for the watchdog to retry, rather than silently leaving a key held.
            if (!failed) { active = false; heldKeys = heldButtons = new string[0]; }
            reason = failed ? "release_failed" : why;
        }
        public void Dispose()
        {
            lock (gate) { disposed = true; permitted = false; StopLocked("unloaded"); }
            watchdog.Dispose();
        }
    }
}
