using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ValheimCliBridge
{
    public sealed class WindowsInput : IInputBackend
    {
        [StructLayout(LayoutKind.Sequential)] public struct MouseInput { public int x, y; public uint data, flags, time; public UIntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] public struct KeyboardInput { public ushort key, scan; public uint flags, time; public UIntPtr extra; }
        [StructLayout(LayoutKind.Explicit)] public struct InputUnion { [FieldOffset(0)] public MouseInput mouse; [FieldOffset(0)] public KeyboardInput keyboard; }
        [StructLayout(LayoutKind.Sequential)] public struct NativeInput { public uint type; public InputUnion value; }
        [StructLayout(LayoutKind.Sequential)] private struct Point { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] private struct Rect { public int left, top, right, bottom; }
        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, NativeInput[] inputs, int size);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref Point point);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
        private readonly uint process = (uint)Process.GetCurrentProcess().Id;
        public bool Supported => Environment.OSVersion.Platform == PlatformID.Win32NT;
        public bool Foreground { get { if (!Supported) return false; GetWindowThreadProcessId(GetForegroundWindow(), out var id); return id == process; } }
        public bool PanicPressed => Supported && Down(123);
        public bool ModifiersPressed => Supported && (Down(16) || Down(17) || Down(18) || Down(91) || Down(92));
        private static bool Down(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
        private void Send(NativeInput input, bool release = false)
        {
            if (!release && (!Foreground || PanicPressed)) throw new InvalidOperationException("Game lost foreground or F12 pressed");
            if (SendInput(1, new[] { input }, Marshal.SizeOf(typeof(NativeInput))) != 1) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        public void Key(ushort key, bool down) => Send(new NativeInput { type = 1, value = new InputUnion { keyboard = new KeyboardInput { key = key, flags = down ? 0u : 2u } } }, !down);
        public void Button(string button, bool down)
        {
            var flag = button == "left" ? (down ? 2u : 4u) : button == "right" ? (down ? 8u : 16u) : (down ? 32u : 64u);
            Send(new NativeInput { value = new InputUnion { mouse = new MouseInput { flags = flag } } }, !down);
        }
        public void Move(Request request)
        {
            if (request.pointerX.HasValue)
            {
                var window = GetForegroundWindow();
                if (!Foreground || !GetClientRect(window, out var rect)) throw new InvalidOperationException("Cannot locate game viewport");
                var point = new Point { x = (int)(request.pointerX.Value * Math.Max(0, rect.right - 1)), y = (int)(request.pointerY.Value * Math.Max(0, rect.bottom - 1)) };
                if (!ClientToScreen(window, ref point)) throw new InvalidOperationException("Cannot locate game viewport");
                var width = GetSystemMetrics(78); var height = GetSystemMetrics(79);
                if (width < 2 || height < 2) throw new InvalidOperationException("Invalid desktop bounds");
                Send(new NativeInput { value = new InputUnion { mouse = new MouseInput { x = (int)((point.x - GetSystemMetrics(76)) * 65535L / (width - 1)), y = (int)((point.y - GetSystemMetrics(77)) * 65535L / (height - 1)), flags = 0xC001 } } });
            }
            else if ((request.mouseX ?? 0) != 0 || (request.mouseY ?? 0) != 0)
                Send(new NativeInput { value = new InputUnion { mouse = new MouseInput { x = request.mouseX ?? 0, y = request.mouseY ?? 0, flags = 1 } } });
            if ((request.scroll ?? 0) != 0) Send(new NativeInput { value = new InputUnion { mouse = new MouseInput { data = unchecked((uint)(request.scroll.Value * 120)), flags = 0x800 } } });
        }
    }
}
