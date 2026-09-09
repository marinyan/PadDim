namespace PadDim;

// Hardware calls can take hundreds of milliseconds. Serialize them off the input/UI thread.
public sealed class HardwareDimmer : IDisposable
{
    private readonly object gate = new();
    private readonly BrightnessSession session;
    private readonly BrightnessRecovery? recovery;
    private Task worker = Task.CompletedTask;
    private bool desired;
    private int percent;
    private int fadeMilliseconds;
    private long generation;
    private bool running;
    private long retryAfter;
    private bool shuttingDown;
    private volatile int pending;
    private volatile string status = "本体輝度: 待機中（減光時に対応画面を確認）";
    public bool IsDimmed { get { lock (gate) return pending > 0 || (desired && running); } }
    public string Status => status;
    public HardwareDimmer(bool persistentRecovery = true)
    {
        if (persistentRecovery) recovery = new(Path.Combine(Path.GetDirectoryName(Settings.FilePath)!, "brightness-recovery.json"));
        session = new(recovery);
        if (persistentRecovery) Request(false, retry: true);
    }
    public void Request(bool dim, int level = 20, int fade = 3000, bool retry = false)
    {
        lock (gate)
        {
            // A later input can retry failed restoration without a dedicated menu command.
            // Rate-limit driver calls while a held button repeatedly reports activity.
            bool retryDue = !dim && pending > 0 && !running && Environment.TickCount64 >= retryAfter;
            if (!retry && !retryDue && desired == dim && (!dim || percent == level)) return;
            desired = dim; percent = level; fadeMilliseconds = fade; generation++;
            if (!running) { running = true; worker = Task.Run(Process); }
        }
    }
    private bool IsCurrent(long version) { lock (gate) return generation == version && desired; }
    private void Process()
    {
        while (true)
        {
            long version; bool dim; int level; int fade;
            lock (gate) { version = generation; dim = desired; level = percent; fade = fadeMilliseconds; }
            try
            {
                var errors = session.Restore();
                if (errors.Count == 0 && recovery is not null && recovery.Count > 0)
                {
                    status = "本体輝度: 前回終了時の輝度を復元中…";
                    var diagnostics = new List<string>();
                    errors.AddRange(recovery.Recover(BrightnessTargets.Discover(diagnostics)));
                }
                if (errors.Count != 0) status = string.Join(Environment.NewLine, errors) + "\n操作すると復元を再試行します。";
                else if (dim && IsCurrent(version))
                {
                    status = "本体輝度: 対応画面を確認・変更中…";
                    var diagnostics = new List<string>();
                    var targets = BrightnessTargets.Discover(diagnostics);
                    int count = targets.Count;
                    status = "本体輝度: フェードアウト中…";
                    errors = session.Dim(targets, level, () => IsCurrent(version), fade);
                    status = string.Join(Environment.NewLine, diagnostics.Concat(errors).Prepend(count == 0 ? "本体輝度: 対応画面がありません。DDC/CI設定・接続方式を確認してください。" : $"本体輝度: {count} 件の制御経路 / 元の輝度の約{level}%"));
                }
                else status = "本体輝度: 復元済み";
            }
            catch (Exception ex) { status = $"本体輝度エラー: {ex.Message}"; }
            try { pending = Math.Max(session.PendingCount, recovery?.Count ?? 0); }
            catch { pending = Math.Max(session.PendingCount, 1); }
            lock (gate)
            {
                if (generation != version) continue;
                retryAfter = Environment.TickCount64 + 3000;
                running = false; return;
            }
        }
    }
    public void Dispose()
    {
        Request(false, retry: true);
        Task current;
        lock (gate) current = worker;
        if (shuttingDown) current.Wait(TimeSpan.FromSeconds(2));
        else current.GetAwaiter().GetResult();
    }
    public void RestoreForShutdown()
    {
        shuttingDown = true;
        Request(false, retry: true);
        Task current;
        lock (gate) current = worker;
        // Do not indefinitely hold up shutdown on an unresponsive monitor driver.
        current.Wait(TimeSpan.FromSeconds(2));
    }
}
