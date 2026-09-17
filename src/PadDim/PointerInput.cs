using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PadDim;

// Observe changes, not the reported age: injected input timestamps need not be monotonic.
public sealed class SystemInputActivity
{
    private uint? previous;
    public bool Sample(uint tick, bool enabled)
    {
        bool changed = enabled && previous.HasValue && previous.Value != tick;
        previous = enabled ? tick : null;
        return changed;
    }
    public void Reset() => previous = null;
}

[Flags]
public enum DesktopActivity { None = 0, Mouse = 1, Keyboard = 2, Touch = 4, Pen = 8 }

public sealed class PointerActivity
{
    private Point? lastPosition;
    public DesktopActivity Mouse(int message, Point position, nuint extra)
    {
        bool moved = lastPosition is null || lastPosition != position;
        lastPosition = position;
        bool action = message == 0x200 ? moved : message is 0x201 or 0x202 or 0x204 or 0x205 or 0x207 or 0x208 or 0x20a or 0x20b or 0x20c or 0x20e;
        if (!action) return DesktopActivity.None;
        if (((ulong)extra & 0xffffff00UL) == 0xff515700UL)
            return ((ulong)extra & 0x80) != 0 ? DesktopActivity.Touch : DesktopActivity.Pen;
        return DesktopActivity.Mouse; // RDP may forward touch as an ordinary mouse event.
    }
    public static DesktopActivity Keyboard(int message) => message is 0x100 or 0x101 or 0x104 or 0x105 ? DesktopActivity.Keyboard : DesktopActivity.None;
}

// Keep the hooks off the UI thread: monitor I/O or modal dialogs must not cause
// Windows to remove them for exceeding LowLevelHooksTimeout. No input is suppressed.
public sealed class PointerInput : IDisposable
{
    private delegate nint Hook(int code, nint message, nint data);
    private readonly Hook mouseCallback, keyboardCallback;
    private readonly PointerActivity policy = new();
    private readonly Thread thread;
    private readonly ManualResetEventSlim ready = new();
    private uint threadId;
    private nint mouseHook, keyboardHook;
    private int pending;
    private volatile bool healthy, stopping;
    private long heartbeat;
    private string error = "";
    public bool Healthy => healthy && thread.IsAlive && Environment.TickCount64 - Interlocked.Read(ref heartbeat) < 5000;
    public PointerInput()
    {
        mouseCallback = OnMouse; keyboardCallback = OnKeyboard;
        thread = new Thread(Run) { IsBackground = true, Name = "PadDim pointer input" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!ready.Wait(TimeSpan.FromSeconds(5)) || !healthy)
        { Dispose(); throw new InvalidOperationException("タッチ・リモート入力の監視を開始できません。" + error); }
    }
    public DesktopActivity Poll() => (DesktopActivity)Interlocked.Exchange(ref pending, 0);
    private nint OnMouse(int code, nint message, nint data)
    {
        if (code >= 0)
        {
            try
            {
                var value = Marshal.PtrToStructure<MouseData>(data);
                Interlocked.Or(ref pending, (int)policy.Mouse((int)message, value.Position, value.Extra));
            }
            catch { healthy = false; }
        }
        return CallNextHookEx(0, code, message, data);
    }
    private nint OnKeyboard(int code, nint message, nint data)
    {
        // Only event presence is needed; never read or store which key was pressed.
        if (code >= 0) Interlocked.Or(ref pending, (int)PointerActivity.Keyboard((int)message));
        return CallNextHookEx(0, code, message, data);
    }
    private void Install()
    {
        Unhook();
        mouseHook = SetWindowsHookEx(14, mouseCallback, GetModuleHandle(null), 0);
        keyboardHook = SetWindowsHookEx(13, keyboardCallback, GetModuleHandle(null), 0);
        healthy = mouseHook != 0 && keyboardHook != 0;
        if (!healthy) error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
    }
    private void Unhook()
    {
        if (mouseHook != 0) { UnhookWindowsHookEx(mouseHook); mouseHook = 0; }
        if (keyboardHook != 0) { UnhookWindowsHookEx(keyboardHook); keyboardHook = 0; }
    }
    private void Run()
    {
        try
        {
            threadId = GetCurrentThreadId();
            using var window = new MessageWindow(); // Establish the thread's message queue before signalling ready.
            Install();
            Interlocked.Exchange(ref heartbeat, Environment.TickCount64);
            ready.Set();
            using var timer = new System.Windows.Forms.Timer { Interval = 1000 };
            int ticks = 0;
            timer.Tick += (_, _) =>
            {
                Interlocked.Exchange(ref heartbeat, Environment.TickCount64);
                if (++ticks >= 30 || !healthy) { ticks = 0; Install(); }
                if (stopping) Application.ExitThread();
            };
            timer.Start();
            if (!stopping) Application.Run();
        }
        catch (Exception ex) { error = ex.Message; ready.Set(); }
        finally { healthy = false; Unhook(); }
    }
    public void Dispose()
    {
        stopping = true;
        if (threadId != 0) PostThreadMessage(threadId, 0x12, 0, 0);
        thread.Join(TimeSpan.FromSeconds(2));
    }
    private sealed class MessageWindow : NativeWindow, IDisposable
    {
        public MessageWindow() => CreateHandle(new CreateParams { Parent = -3 });
        public void Dispose() => DestroyHandle();
    }
    [StructLayout(LayoutKind.Sequential)] private struct MouseData { public Point Position; public uint Data, Flags, Time; public nuint Extra; }
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int id, Hook callback, nint module, uint thread);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostThreadMessage(uint thread, uint message, nint wParam, nint lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
