namespace PadDim;
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length == 2 && args[0] == "--tray-smoke-test")
        {
            using var form = new MainForm(startInTray: true, persistentRecovery: false);
            using var check = new System.Windows.Forms.Timer { Interval = 300 };
            check.Tick += (_, _) =>
            {
                check.Stop();
                bool hidden = !form.Visible && form.IsHandleCreated;
                form.Show();
                bool reopened = form.Visible;
                File.WriteAllText(args[1], $"HiddenAtStartup={hidden}\nCanReopen={reopened}");
                Environment.ExitCode = hidden && reopened ? 0 : 1;
                Application.ExitThread();
            };
            check.Start();
            Application.Run(form);
            return;
        }
        if (args.Length == 2 && args[0] == "--test-brightness")
        {
            var diagnostics = new List<string>();
            var targets = BrightnessTargets.Discover(diagnostics);
            var target = targets.FirstOrDefault();
            foreach (var other in targets.Skip(1)) other.Dispose();
            if (target is null) { diagnostics.Add("FAIL: 対応画面なし"); Environment.ExitCode = 1; }
            else
            {
                var session = new BrightnessSession();
                try
                {
                    const int level = 90; // Small relative dim for the real-device diagnostic.
                    var errors = session.Dim([target], level, () => true, 1000);
                    if (errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors));
                    var check = BrightnessTargets.Discover([]);
                    try
                    {
                        var found = check.Single(t => t.Name == target.Name);
                        diagnostics.Add($"変更: {target.Original} → {found.Original} (元の輝度の約{level}%)");
                        if (found.Original >= target.Original) throw new InvalidOperationException("輝度の低下を確認できませんでした");
                    }
                    finally { foreach (var item in check) item.Dispose(); }
                }
                catch (Exception ex) { diagnostics.Add($"FAIL: {ex.Message}"); Environment.ExitCode = 1; }
                finally
                {
                    var errors = session.Restore();
                    if (errors.Count > 0) errors = session.Restore();
                    diagnostics.AddRange(errors);
                    if (errors.Count > 0) Environment.ExitCode = 1;
                }
                var restored = BrightnessTargets.Discover([]);
                try
                {
                    var found = restored.SingleOrDefault(t => t.Name == target.Name);
                    bool ok = found?.Original == target.Original;
                    diagnostics.Add($"復元: {found?.Original} / 元の値 {target.Original} — {(ok ? "PASS" : "FAIL")}");
                    if (!ok) Environment.ExitCode = 1;
                }
                finally { foreach (var item in restored) item.Dispose(); }
            }
            File.WriteAllLines(args[1], diagnostics);
            return;
        }
        if (args.Length == 2 && args[0] == "--probe-brightness")
        {
            var diagnostics = new List<string>();
            var targets = BrightnessTargets.Discover(diagnostics);
            foreach (var target in targets)
            { diagnostics.Add($"{target.Name}: 現在 {target.Original}, 範囲 {target.Minimum}–{target.Maximum}, 復元用識別子: {(string.IsNullOrWhiteSpace(target.RecoveryId) ? "取得不可" : "取得済み")}"); target.Dispose(); }
            File.WriteAllLines(args[1], diagnostics);
            return;
        }
        if (args.Length == 2 && args[0] == "--render-ui")
        {
            using var form = new MainForm(persistentRecovery: false);
            form.Show();
            Application.DoEvents();
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(args[1]);
            return;
        }
        if (args.Length == 2 && args[0] == "--smoke-test")
        {
            try
            {
                using var owner = new Form();
                using var input = new InputMonitor(owner.Handle);
                input.Poll(Environment.TickCount64);
                File.WriteAllText(args[1], $"Healthy={input.Healthy}\n{input.Status}");
            }
            catch (Exception ex) { File.WriteAllText(args[1], ex.ToString()); Environment.ExitCode = 1; }
            return;
        }
        using var mutex = new Mutex(true, "Local\\PadDim.Application", out bool first);
        bool startInTray = args.Contains("--tray");
        if (!first) { if (!startInTray) MessageBox.Show("PadDim はすでに起動しています。通知領域のアイコンから設定を開いてください。", "PadDim"); return; }
        try { Application.Run(new MainForm(startInTray)); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "PadDim — 起動エラー", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
