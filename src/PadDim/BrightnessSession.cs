namespace PadDim;

public interface IBrightnessTarget : IDisposable
{
    string Name { get; }
    uint Minimum { get; }
    uint Maximum { get; }
    uint Original { get; }
    void Set(uint value);
}

// Targets are captured before any writes, so duplicate API paths preserve the same original value.
public sealed class BrightnessSession
{
    private readonly List<IBrightnessTarget> pending = [];
    public int PendingCount => pending.Count;
    public static uint DimValue(uint minimum, uint maximum, uint original, int percent)
    {
        if (maximum < minimum || original < minimum || original > maximum) throw new ArgumentOutOfRangeException(nameof(original));
        // Scale the usable range above the device minimum from this cycle's original value.
        return minimum + (uint)Math.Round((original - (double)minimum) * Math.Clamp(percent, 0, 100) / 100);
    }
    public List<string> Dim(IEnumerable<IBrightnessTarget> targets, int percent, Func<bool> stillRequested,
        int fadeMilliseconds = 0, Action<int>? delay = null, Func<long>? clock = null)
    {
        var errors = new List<string>();
        var active = new List<(IBrightnessTarget Target, uint End, uint Last)>();
        foreach (var target in targets)
        {
            if (!stillRequested()) { target.Dispose(); continue; }
            try
            {
                uint value = DimValue(target.Minimum, target.Maximum, target.Original, percent);
                if (value == target.Original) { target.Dispose(); continue; }
                // Keep original even when a failed driver call might have partially changed brightness.
                pending.Add(target);
                active.Add((target, value, target.Original));
            }
            catch (Exception ex) { errors.Add($"{target.Name}: {ex.Message}"); if (!pending.Contains(target)) target.Dispose(); }
        }
        delay ??= Thread.Sleep;
        clock ??= () => Environment.TickCount64;
        long start = clock();
        while (active.Count > 0 && stillRequested())
        {
            double progress = fadeMilliseconds <= 0 ? 1 : Math.Clamp((clock() - start) / (double)fadeMilliseconds, 0, 1);
            for (int i = active.Count - 1; i >= 0; i--)
            {
                if (!stillRequested()) return errors;
                var step = active[i];
                uint value = FadeValue(step.Target.Original, step.End, progress);
                if (value == step.Last) continue;
                try { step.Target.Set(value); active[i] = (step.Target, step.End, value); }
                catch (Exception ex) { errors.Add($"{step.Target.Name}: {ex.Message}"); active.RemoveAt(i); }
            }
            if (progress >= 1) break;
            delay(50);
        }
        return errors;
    }
    public static uint FadeValue(uint start, uint end, double progress) =>
        (uint)Math.Round(start + (end - (double)start) * Math.Clamp(progress, 0, 1));
    public List<string> Restore()
    {
        var errors = new List<string>();
        foreach (var target in pending.ToArray())
        {
            try { target.Set(target.Original); pending.Remove(target); target.Dispose(); }
            catch (Exception ex) { errors.Add($"{target.Name}: 復元できません — {ex.Message}"); }
        }
        return errors;
    }
}
