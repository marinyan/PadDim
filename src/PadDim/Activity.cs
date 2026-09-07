namespace PadDim;

// Pure input policy, shared with the regression test executable.
public sealed class AxisActivity(int threshold)
{
    private int[]? neutral;
    public bool Sample(int[] axes)
    {
        if (neutral is null) { neutral = (int[])axes.Clone(); return false; }
        return axes.Where((value, i) => Math.Abs((long)value - neutral[i]) > threshold).Any();
    }
    public void Reset() => neutral = null;
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
