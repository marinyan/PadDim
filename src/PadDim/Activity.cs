namespace PadDim;

// Pure input policy, shared with the regression test executable.
public sealed class AxisActivity(int threshold)
{
    private int[]? neutral;
    public int[] ActiveAxes { get; private set; } = [];
    public bool Sample(int[] axes)
    {
        if (neutral is null) { neutral = (int[])axes.Clone(); return false; }
        ActiveAxes = Enumerable.Range(0, axes.Length).Where(i => Math.Abs((long)axes[i] - neutral[i]) > threshold).ToArray();
        return ActiveAxes.Length != 0;
    }
    public void Reset() { neutral = null; ActiveAxes = []; }
}

// Some HID drivers initially report placeholder centers before the first real report.
public sealed class SettlingAxisActivity(int threshold)
{
    private AxisActivity? calibrated;
    private int[]? candidate;
    private long started, stableSince;
    public bool IsCalibrating => calibrated is null;
    public int[] ActiveAxes => calibrated?.ActiveAxes ?? [];
    public bool Sample(int[] values, long now)
    {
        if (calibrated is not null) return calibrated.Sample(values);
        if (candidate is null) { candidate = (int[])values.Clone(); started = stableSince = now; }
        int tolerance = Math.Clamp(threshold / 4, 1, 256);
        if (values.Where((v, i) => Math.Abs((long)v - candidate[i]) > tolerance).Any())
        { candidate = (int[])values.Clone(); stableSince = now; }
        if (now - started >= 1000 && now - stableSince >= 500)
        { calibrated = new(threshold); calibrated.Sample(values); }
        return false;
    }
}

public sealed class IdlePolicy
{
    public long LastActivity { get; private set; }
    public string Source { get; private set; } = "起動";
    public IdlePolicy(long now) => LastActivity = now;
    public void Record(long now, string source) { LastActivity = now; Source = source; }
    public long IdleMilliseconds(long now) => Math.Max(0, now - LastActivity);
    public bool ShouldDim(long now, int seconds, bool enabled, bool inputHealthy) =>
        enabled && inputHealthy && IdleMilliseconds(now) >= seconds * 1000L;
}
