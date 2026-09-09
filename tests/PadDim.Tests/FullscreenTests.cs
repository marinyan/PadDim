using PadDim;

static class FullscreenTests
{
    private static void Check(bool value, string description)
    { if (!value) throw new Exception(description); Console.WriteLine("PASS " + description); }
    public static void Run()
    {
        var monitor = new Rectangle(0, 0, 1920, 1080);
        Check(FullscreenMonitor.CoversMonitor(monitor, monitor), "Borderless fullscreen blocks dimming");
        Check(!FullscreenMonitor.CoversMonitor(new(0, 30, 1920, 1010), monitor), "Maximized client excluding title bar/taskbar is not fullscreen");
        Check(!FullscreenMonitor.CoversMonitor(new(200, 100, 1280, 720), monitor), "Ordinary window does not block dimming");
        Check(FullscreenMonitor.CoversMonitor(new(-2560, -200, 2560, 1440), new(-2560, -200, 2560, 1440)), "Secondary monitor supports negative coordinates and other resolutions");
        Check(FullscreenMonitor.CoversMonitor(new(-1920, 0, 3840, 1080), monitor), "Fullscreen spanning monitors blocks dimming");
        Check(!FullscreenMonitor.CoversMonitor(new(-1920, 0, 1920, 1080), monitor), "Another monitor is not mistaken for coverage");
        Check(!FullscreenMonitor.CoversMonitor(Rectangle.Empty, monitor), "Empty client is not fullscreen");
        Check(FullscreenMonitor.CoversMonitor(new(1, 1, 1918, 1078), monitor), "Small border rounding is tolerated");
        Check(!FullscreenMonitor.CoversMonitor(new(20, 20, 1880, 1040), monitor), "Visible margins are not fullscreen");
        foreach (string name in new[] { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" })
            Check(FullscreenMonitor.IsShellClass(name), "Shell excluded: " + name);
        Check(!FullscreenMonitor.IsShellClass("Chrome_WidgetWin_1"), "Browser is eligible for fullscreen detection");
        var idle = new IdlePolicy(0);
        var guard = new FullscreenGuard();
        Check(!guard.Update(FullscreenState.None, idle, 5000) && idle.LastActivity == 0, "Windowed polling preserves idle timer");
        Check(guard.Update(FullscreenState.Active, idle, 10000) && guard.BlocksDimming, "Entering fullscreen requests brightness restoration");
        guard.Update(FullscreenState.Active, idle, 1000000);
        Check(!idle.ShouldDim(1000000, 10, !guard.BlocksDimming, true), "Long fullscreen playback never reaches dimming");
        Check(guard.Update(FullscreenState.None, idle, 1000050) && !guard.BlocksDimming, "Leaving fullscreen restarts timer");
        Check(!idle.ShouldDim(1010049, 10, true, true) && idle.ShouldDim(1010050, 10, true, true), "Full timeout is required after leaving fullscreen");
        Check(guard.Update(FullscreenState.Unavailable, idle, 1010100) && guard.BlocksDimming, "Foreground query failure restores and blocks dimming");
        guard.Update(FullscreenState.None, idle, 1010200);
        Check(idle.LastActivity == 1010200, "Foreground query recovery restarts timer");
        Check(Enum.IsDefined(FullscreenMonitor.Read()), "Native foreground monitor query executes successfully");
    }
}
