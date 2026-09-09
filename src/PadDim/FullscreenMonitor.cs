using System.Runtime.InteropServices;
using System.Text;

namespace PadDim;

public enum FullscreenState { None, Active, Unavailable }

public sealed class FullscreenGuard
{
    public FullscreenState State { get; private set; }
    public bool BlocksDimming => State != FullscreenState.None;
    public string Reason => State == FullscreenState.Active ? "フルスクリーン中 — 減光を保留" :
        State == FullscreenState.Unavailable ? "画面状態の取得待ち — 減光を保留" : "フルスクリーン解除 — タイマー再開";
    public bool Update(FullscreenState state, IdlePolicy idle, long now)
    {
        bool wasBlocked = BlocksDimming;
        State = state;
        if (!BlocksDimming && !wasBlocked) return false;
        idle.Record(now, Reason);
        return true;
    }
}

public static class FullscreenMonitor
{
    public static bool IsShellClass(string name) => name is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    public static bool CoversMonitor(Rectangle client, Rectangle monitor) =>
        client.Width > 0 && client.Height > 0 && monitor.Width > 0 && monitor.Height > 0 &&
        (long)client.Left <= (long)monitor.Left + 2 && (long)client.Top <= (long)monitor.Top + 2 &&
        (long)client.Right >= (long)monitor.Right - 2 && (long)client.Bottom >= (long)monitor.Bottom - 2;

    public static FullscreenState Read()
    {
        // Use physical coordinates consistently across monitors with different scaling.
        nint previous = SetThreadDpiAwarenessContext(-4); // PER_MONITOR_AWARE_V2
        try
        {
            nint window = GetForegroundWindow();
            if (window == 0) return FullscreenState.Unavailable;
            if (window == GetShellWindow() || window == GetDesktopWindow() || !IsWindowVisible(window) || IsIconic(window)) return FullscreenState.None;
            if (GetWindowThreadProcessId(window, out uint process) == 0) return FullscreenState.Unavailable;
            if (process == Environment.ProcessId) return FullscreenState.None; // Including our non-activating shades.
            var name = new StringBuilder(256);
            if (GetClassName(window, name, name.Capacity) == 0) return FullscreenState.Unavailable;
            if (IsShellClass(name.ToString())) return FullscreenState.None;
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            nint monitor = MonitorFromWindow(window, 0);
            if (monitor == 0 || !GetMonitorInfo(monitor, ref info) || !GetClientRect(window, out var client)) return FullscreenState.Unavailable;
            var topLeft = new Point(client.Left, client.Top);
            var bottomRight = new Point(client.Right, client.Bottom);
            if (!ClientToScreen(window, ref topLeft) || !ClientToScreen(window, ref bottomRight) || GetForegroundWindow() != window)
                return FullscreenState.Unavailable;
            // Client area excludes the title bar, so ordinary maximized windows do not qualify.
            return CoversMonitor(Rectangle.FromLTRB(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y),
                Rectangle.FromLTRB(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom))
                ? FullscreenState.Active : FullscreenState.None;
        }
        finally { if (previous != 0) SetThreadDpiAwarenessContext(previous); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetShellWindow();
    [DllImport("user32.dll")] private static extern nint GetDesktopWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int capacity);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetClientRect(nint window, out Rect rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ClientToScreen(nint window, ref Point point);
}
