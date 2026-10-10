using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace ValheimCliBridge
{
    public static class InputSpec
    {
        private static readonly HashSet<string> Actions = new HashSet<string> { "attack", "secondary", "block", "jump", "crouch", "run", "dodge" };
        public static string[] Split(string value) => value == null ? new string[0] : value.Split(',');
        public static void Validate(Request r)
        {
            var actions = Split(r.actions);
            if (actions.Any(a => !Actions.Contains(a)) || actions.Distinct().Count() != actions.Length)
                throw new InvalidDataException("Unsupported or repeated game action");
            if (!r.durationMs.HasValue || r.durationMs < 50 || r.durationMs > 5000)
                throw new InvalidDataException("Input requires duration 50..5000 ms");
            if (!Bounded(r.moveX ?? 0, 1) || !Bounded(r.moveZ ?? 0, 1)) throw new InvalidDataException("Movement must be finite and in -1..1");
            if ((r.moveX ?? 0) == 0 && (r.moveZ ?? 0) == 0 && actions.Length == 0) throw new InvalidDataException("Empty input");
        }
        public static bool Bounded(double value, double limit) => !double.IsNaN(value) && !double.IsInfinity(value) && Math.Abs(value) <= limit;
    }

    public sealed class ControlFrame
    {
        public float X, Z;
        public bool Attack, AttackHold, Secondary, SecondaryHold, Block, BlockHold, Jump, Crouch, Run, Dodge;
    }

    // The timer touches managed state only. Unity reads it on the game's control thread.
    public sealed class InputController : IDisposable
    {
        private readonly object gate = new object();
        private readonly System.Threading.Timer watchdog;
        private ControlFrame held;
        private bool permitted, disposed, first;
        private long expires, epoch;
        private string reason = "idle", id;
        public InputController() { watchdog = new System.Threading.Timer(_ => Tick(), null, 20, 20); }
        public bool Active { get { lock (gate) { Expire(); return held != null; } } }
        public string Reason { get { lock (gate) return reason; } }
        public string Id { get { lock (gate) return id; } }
        public long Epoch { get { lock (gate) return epoch; } }
        public void Permit(bool value)
        {
            lock (gate)
            {
                if (permitted && !value) { epoch++; StopLocked("permission_revoked"); }
                permitted = value;
            }
        }
        public void CheckEpoch(Request r)
        {
            lock (gate)
            {
                if (disposed || !permitted) throw new InvalidOperationException("AllowControl=true is required");
                if (r.controlEpoch.HasValue && r.controlEpoch != epoch) throw new InvalidOperationException("Action cancelled by stop or loss of control");
            }
        }
        public void Start(Request r)
        {
            InputSpec.Validate(r);
            lock (gate)
            {
                CheckEpoch(r); Expire();
                if (held != null) throw new InvalidOperationException("Input busy; observe or stop before another action");
                var a = InputSpec.Split(r.actions);
                var x = (float)(r.moveX ?? 0); var z = (float)(r.moveZ ?? 0);
                var length = Math.Sqrt(x * x + z * z);
                if (length > 1) { x /= (float)length; z /= (float)length; }
                held = new ControlFrame { X = x, Z = z, AttackHold = a.Contains("attack"), SecondaryHold = a.Contains("secondary"),
                    BlockHold = a.Contains("block"), Jump = a.Contains("jump"), Crouch = a.Contains("crouch"), Run = a.Contains("run"), Dodge = a.Contains("dodge") };
                first = true; id = r.id; reason = "running";
                expires = Stopwatch.GetTimestamp() + r.durationMs.Value * Stopwatch.Frequency / 1000;
            }
        }
        public ControlFrame Sample()
        {
            lock (gate)
            {
                Expire();
                if (held == null) return null;
                var frame = new ControlFrame { X = held.X, Z = held.Z, AttackHold = held.AttackHold, SecondaryHold = held.SecondaryHold,
                    BlockHold = held.BlockHold, Run = held.Run, Attack = first && held.AttackHold, Secondary = first && held.SecondaryHold,
                    Block = first && held.BlockHold, Jump = first && held.Jump, Crouch = first && held.Crouch, Dodge = first && held.Dodge };
                first = false;
                return frame;
            }
        }
        public void Tick() { lock (gate) { if (!disposed) Expire(); } }
        internal void ExhaustRun() { lock (gate) { if (held != null) held.Run = false; } }
        private void Expire() { if (held != null && Stopwatch.GetTimestamp() >= expires) StopLocked("completed"); }
        public void Stop(string why = "stopped") { lock (gate) { epoch++; StopLocked(why); } }
        private void StopLocked(string why) { held = null; reason = why; }
        public void Dispose() { lock (gate) { disposed = true; permitted = false; epoch++; StopLocked("unloaded"); } watchdog.Dispose(); }
    }
}
