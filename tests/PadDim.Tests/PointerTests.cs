using PadDim;

static class PointerTests
{
    static void Check(bool value, string text) { if (!value) throw new Exception(text); Console.WriteLine("PASS " + text); }
    public static void Run()
    {
        var ticks = new SystemInputActivity();
        Check(!ticks.Sample(100, false) && !ticks.Sample(200, false), "Disabled compatibility detection ignores controller idle tick noise");
        Check(!ticks.Sample(200, true), "Enabling compatibility detection establishes a baseline");
        Check(ticks.Sample(201, true), "Native remote touch tick change is detected");
        Check(!ticks.Sample(201, true), "No touch means the idle timer can advance");
        Check(ticks.Sample(199, true), "Nonmonotonic injected timestamp still counts as activity");
        ticks.Reset();
        Check(!ticks.Sample(uint.MaxValue, true) && ticks.Sample(0, true), "Tick wraparound is detected");
        ticks.Sample(1, false);
        Check(!ticks.Sample(2, true), "Reenabling does not replay input while disabled");
        Check(!Settings.FromJson("{}").UseSystemInputTime, "Existing settings preserve controller-compatible detection");
        Check(Settings.FromJson(System.Text.Json.JsonSerializer.Serialize(new Settings { UseSystemInputTime = true })).UseSystemInputTime, "Touch compatibility setting survives restart");
        var policy = new PointerActivity();
        var point = new Point(100, 200);
        Check(policy.Mouse(0x201, point, 0xff515780) == DesktopActivity.Touch, "Touch tap is recognized");
        Check(policy.Mouse(0x202, point, 0xff515780) == DesktopActivity.Touch, "Touch release is recognized without movement");
        Check(policy.Mouse(0x201, point, 0xff515780) == DesktopActivity.Touch, "Repeated tap at the same location restores brightness");
        Check(policy.Mouse(0x200, new Point(101,200), 0xff515780) == DesktopActivity.Touch, "Touch drag movement is recognized");
        Check(policy.Mouse(0x200, new Point(101,200), 0xff515780) == DesktopActivity.None, "Stationary synthetic mouse reports do not keep resetting timer");
        Check(policy.Mouse(0x201, point, 0xff515701) == DesktopActivity.Pen, "Pen contact is recognized");
        Check(policy.Mouse(0x201, point, 0) == DesktopActivity.Mouse, "Remote touch forwarded as mouse click is recognized");
        Check(policy.Mouse(0x20a, point, 0) == DesktopActivity.Mouse, "Remote swipe forwarded as wheel is recognized");
        Check(policy.Mouse(0x20e, point, 0) == DesktopActivity.Mouse, "Remote horizontal scroll is recognized");
        Check(policy.Mouse(0x999, point, 0) == DesktopActivity.None, "Unrelated events do not reset timer");
        Check(PointerActivity.Keyboard(0x100) == DesktopActivity.Keyboard && PointerActivity.Keyboard(0x101) == DesktopActivity.Keyboard, "Remote keyboard down and up are recognized");
        var idle = new IdlePolicy(0);
        Check(idle.ShouldDim(10000, 10, true, true), "Idle timer expires before touch");
        if (policy.Mouse(0x201, point, 0xff515780) != DesktopActivity.None) idle.Record(10000, "タッチ");
        Check(!idle.ShouldDim(19999, 10, true, true) && idle.ShouldDim(20000, 10, true, true), "Touch restarts the full idle timeout");
        using var input = new PointerInput();
        Check(input.Healthy, "Dedicated input hook thread initializes");
        input.Dispose();
        Check(!input.Healthy, "Input hook thread stops on disposal");
    }
}
