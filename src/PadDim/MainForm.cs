using Microsoft.Win32;

namespace PadDim;
public sealed class MainForm : Form
{
    private readonly MinutesInput timeout = new();
    private readonly NumericUpDown darkness = new() { Minimum = 5, Maximum = 90, Increment = 5, Width = 120 };
    private readonly NumericUpDown deadzone = new() { Minimum = 1, Maximum = 40, Width = 120 };
    private readonly NumericUpDown brightness = new() { Minimum = 0, Maximum = 100, Width = 120 };
    private readonly NumericUpDown fadeSeconds = new() { Minimum = 0, Maximum = 60, Width = 120 };
    private readonly ComboBox mode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 360 };
    private readonly CheckBox temporarilyDisabled = new() { Text = "一時的に無効化", AutoSize = true };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(640, 0) };
    private readonly TextBox devices = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 50 };
    private readonly System.Windows.Forms.Timer recoveryTimer = new() { Interval = 200 };
    private readonly Button recoveryButton = new()
    {
        Name = "recoveryButton", Text = "強制復旧", AutoSize = true,
        Padding = new Padding(6, 3, 6, 3), Enabled = false,
        AccessibleDescription = "保存してある元の輝度への復元を再試行します。"
    };
    private readonly NotifyIcon tray;
    private readonly Dimmer dimmer;
    private readonly IdlePolicy idle = new(Environment.TickCount64);
    private readonly FullscreenGuard fullscreen = new();
    private readonly InputMonitor input;
    private Settings settings;
    private bool quitting;
    private long nextUi;
    private long previewUntil;
    private long previewStartAt;
    private bool sessionLocked;

    private bool hideInitialShow;
    public MainForm(bool startInTray = false, bool persistentRecovery = true)
    {
        dimmer = new(persistentRecovery);
        hideInitialShow = startInTray;
        Text = "PadDim — ゲームパッド対応 自動減光";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        Font = new Font("Yu Gothic UI", 10);
        ClientSize = new Size(840, Math.Min(900, Screen.PrimaryScreen!.WorkingArea.Height - 100)); MinimumSize = new Size(760, 650);
        StartPosition = FormStartPosition.CenterScreen;
        try { settings = Settings.Load(); }
        catch (Exception ex) { settings = new(); MessageBox.Show($"設定を読み込めないため初期値で起動します。\n{ex.Message}", "PadDim"); }
        timeout.Value = settings.IdleSeconds / 60m; darkness.Value = settings.DimPercent; deadzone.Value = settings.DeadzonePercent;
        brightness.Value = settings.BrightnessRatioPercent; fadeSeconds.Value = settings.FadeSeconds;
        mode.Items.AddRange(["モニター本体の輝度（DDC/CI・WMI）", "半透明の黒い画面を重ねる", "本体輝度 ＋ 黒いオーバーレイ"]);
        mode.SelectedIndex = settings.UseHardwareBrightness ? settings.UseOverlayWithHardware ? 2 : 0 : 1;
        brightness.Enabled = mode.SelectedIndex != 1; darkness.Enabled = mode.SelectedIndex != 0;
        mode.SelectedIndexChanged += (_, _) => { brightness.Enabled = mode.SelectedIndex != 1; darkness.Enabled = mode.SelectedIndex != 0; };
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        Controls.Add(scroll);
        Controls.Add(new Label
        {
            Name = "versionLabel",
            Text = $"v{typeof(MainForm).Assembly.GetName().Version?.ToString(3)}",
            Dock = DockStyle.Bottom, Height = 24,
            TextAlign = ContentAlignment.MiddleRight,
            Padding = new Padding(0, 0, 12, 0),
            Font = new Font(Font.FontFamily, 8), ForeColor = SystemColors.GrayText
        });
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(22), ColumnCount = 1, RowCount = 13 };
        layout.RowStyles.Clear();
        for (int i = 0; i < 13; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        devices.MinimumSize = new Size(0, 140);
        scroll.Controls.Add(layout);
        layout.Controls.Add(new Label { Text = "操作が止まったら、画面を静かに暗く。", Font = new Font(Font.FontFamily, 17, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 0, 0, 16) });
        layout.Controls.Add(temporarilyDisabled);
        layout.Controls.Add(Row("減光までの無操作時間（分）", timeout));
        layout.Controls.Add(Row("減光方式", mode));
        layout.Controls.Add(Row("元の輝度に対する割合（%）", brightness));
        layout.Controls.Add(Row("黒い画面の濃さ（%）", darkness));
        layout.Controls.Add(Row("フェードアウト時間（秒）", fadeSeconds));
        layout.Controls.Add(Row("スティックの遊び（%）", deadzone));
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 8) };
        buttons.Controls.Add(Button("設定を保存", (_, _) => SaveSettings()));
        buttons.Controls.Add(Button("減光を試す（5秒保持）", (_, _) => { Restore("プレビュー待機"); previewStartAt = Environment.TickCount64 + 300; }));
        buttons.Controls.Add(Button("3秒後に中立位置を再取得", (_, _) => CalibrateLater()));
        recoveryButton.Click += (_, _) =>
        {
            // Guard against a completed asynchronous restore since the last UI refresh.
            if (!dimmer.IsDimmed) { recoveryButton.Enabled = false; return; }
            previewStartAt = 0;
            Restore("強制復旧");
            dimmer.Restore(retry: true);
            recoveryButton.Enabled = false;
        };
        buttons.Controls.Add(recoveryButton);
        layout.Controls.Add(buttons);
        layout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(740, 0), Text = "減光時だけフェードアウトし、操作するとフェードなしで元の明るさに戻します。\n本体輝度は対応画面のみ変更します。DDC/CI設定がある画面では有効にしてください。\n×で常駐、終了は通知領域から。DirectInputの接続・中立位置取得時は手を離してください。", Margin = new Padding(0, 4, 0, 12) });
        layout.Controls.Add(status);
        layout.Controls.Add(new Label { Text = "入力の監視状況（同じ機器が両APIに表示される場合があります）", AutoSize = true, Margin = new Padding(0, 14, 0, 6) });
        layout.Controls.Add(devices);
        input = new InputMonitor(Handle);
        ApplySensitivity();
        var menu = new ContextMenuStrip();
        menu.Items.Add("設定を開く", null, (_, _) => ShowSettings());
        var pause = new ToolStripMenuItem("一時停止") { CheckOnClick = true };
        pause.CheckedChanged += (_, _) => temporarilyDisabled.Checked = pause.Checked;
        temporarilyDisabled.CheckedChanged += (_, _) =>
        {
            pause.Checked = temporarilyDisabled.Checked;
            previewStartAt = 0;
            Restore(temporarilyDisabled.Checked ? "一時的に無効化" : "監視を再開");
        };
        menu.Items.Add(pause);
        menu.Items.Add("終了", null, (_, _) => { quitting = true; Close(); });
        tray = new NotifyIcon { Icon = Icon, Text = "PadDim — 入力を監視中", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => ShowSettings();
        timer.Tick += (_, _) => TickInput();
        SystemEvents.DisplaySettingsChanged += DisplayChanged;
        SystemEvents.SessionSwitch += SessionChanged;
        SystemEvents.PowerModeChanged += PowerChanged;
        timer.Start();
        // Keep recovery available even if the input monitor has stopped with an error.
        recoveryTimer.Tick += (_, _) => recoveryButton.Enabled = dimmer.IsDimmed;
        recoveryTimer.Start();
        TickInput();
    }
    private long calibrateAt;
    private void CalibrateLater() { Restore("3秒後に中立位置を取得します"); calibrateAt = Environment.TickCount64 + 3000; }
    private void ApplySensitivity() { input.AxisThreshold = (int)deadzone.Value * 65535 / 100; input.StickDeadzone = (int)deadzone.Value * 32767 / 100; input.Calibrate(); }
    private void SaveSettings()
    {
        timeout.CommitEdit();
        var updated = new Settings { IdleSeconds = (int)Math.Round(timeout.Value * 60m, MidpointRounding.AwayFromZero), DimPercent = (int)darkness.Value, DeadzonePercent = (int)deadzone.Value, UseHardwareBrightness = mode.SelectedIndex != 1, UseOverlayWithHardware = mode.SelectedIndex == 2, BrightnessRatioPercent = (int)brightness.Value, FadeSeconds = (int)fadeSeconds.Value };
        try { updated.Save(); settings = updated; ApplySensitivity(); Restore("設定を保存しました"); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "設定の保存に失敗しました"); }
    }
    private void TickInput()
    {
        try
        {
            long now = Environment.TickCount64;
            if (calibrateAt != 0 && now >= calibrateAt) { input.Calibrate(); calibrateAt = 0; Restore("中立位置を再取得"); }
            string? source = input.Poll(now);
            if (source is not null) Restore(source);
            bool monitoring = !temporarilyDisabled.Checked;
            if (fullscreen.Update(FullscreenMonitor.Read(), idle, now))
            {
                previewStartAt = 0;
                Restore(fullscreen.Reason);
            }
            if (!input.Healthy || sessionLocked || !monitoring) Restore(!input.Healthy ? "入力取得不可 — 減光を保留" : "一時停止");
            if (previewStartAt != 0 && now >= previewStartAt)
            {
                previewStartAt = 0;
                if (input.Healthy && !sessionLocked && monitoring && !fullscreen.BlocksDimming)
                { previewUntil = now + (int)fadeSeconds.Value * 1000 + 5000; dimmer.Dim((int)darkness.Value, mode.SelectedIndex != 1, (int)brightness.Value, (int)fadeSeconds.Value * 1000, mode.SelectedIndex == 2); }
            }
            if (previewUntil != 0 && now >= previewUntil) Restore("プレビュー終了");
            if (previewUntil == 0 && idle.ShouldDim(now, settings.IdleSeconds, monitoring && !sessionLocked && !fullscreen.BlocksDimming && calibrateAt == 0, input.Healthy)) dimmer.Dim(settings.DimPercent, settings.UseHardwareBrightness, settings.BrightnessRatioPercent, settings.FadeSeconds * 1000, settings.UseOverlayWithHardware);
            if (now >= nextUi)
            {
                nextUi = now + 250;
                status.Text = $"{(!monitoring ? "一時停止" : fullscreen.BlocksDimming ? fullscreen.Reason : dimmer.IsDimmed ? "減光中" : "監視中")}   |   無操作 {idle.IdleMilliseconds(now) / 1000} 秒\n最後の操作 / 状態: {idle.Source}";
                string details = input.Status + Environment.NewLine + Environment.NewLine + dimmer.Status;
                if (devices.Text != details) devices.Text = details;
            }
        }
        catch (Exception ex)
        {
            Restore("監視エラー"); timer.Stop(); temporarilyDisabled.Checked = true;
            temporarilyDisabled.Enabled = false;
            tray.ContextMenuStrip!.Items[1].Enabled = false;
            devices.Text = $"監視を停止しました。再起動してください。\r\n{ex}";
            status.Text = "監視エラー — 減光を解除しました";
            ShowSettings();
        }
    }
    private void Restore(string source) { dimmer.Restore(); previewUntil = 0; idle.Record(Environment.TickCount64, source); }
    private void ShowSettings() { Restore("設定画面"); Show(); WindowState = FormWindowState.Normal; Activate(); }
    protected override void SetVisibleCore(bool value)
    {
        if (value && hideInitialShow) { hideInitialShow = false; value = false; }
        base.SetVisibleCore(value);
    }
    private void OnUi(Action action) { if (!IsDisposed && IsHandleCreated) BeginInvoke(action); }
    private void DisplayChanged(object? sender, EventArgs e) => OnUi(() => Restore("ディスプレイ構成変更"));
    private void SessionChanged(object sender, SessionSwitchEventArgs e) => OnUi(() => { sessionLocked = e.Reason == SessionSwitchReason.SessionLock; Restore("セッション変更"); });
    private void PowerChanged(object sender, PowerModeChangedEventArgs e) => OnUi(() => Restore("電源状態変更"));
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!quitting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        base.OnFormClosing(e);
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0011) // WM_QUERYENDSESSION: start restoration while drivers are available.
        {
            timer.Stop();
            Restore("Windowsの終了準備");
        }
        if (m.Msg == 0x0016) // WM_ENDSESSION
        {
            if (m.WParam != 0) dimmer.RestoreForShutdown();
            else { Restore("Windowsの終了キャンセル"); timer.Start(); }
        }
        base.WndProc(ref m);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Stop(); timer.Dispose(); recoveryTimer.Stop(); recoveryTimer.Dispose(); dimmer.Dispose();
            SystemEvents.DisplaySettingsChanged -= DisplayChanged;
            SystemEvents.SessionSwitch -= SessionChanged;
            SystemEvents.PowerModeChanged -= PowerChanged;
            tray.Visible = false; tray.Dispose(); input.Dispose();
        }
        base.Dispose(disposing);
    }
    private static FlowLayoutPanel Row(string text, Control control)
    {
        var row = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 7, 0, 0) };
        row.Controls.Add(new Label { Text = text, Width = 280, AutoSize = false, Height = 30, TextAlign = ContentAlignment.MiddleLeft }); row.Controls.Add(control); return row;
    }
    private static Button Button(string text, EventHandler handler)
    { var button = new Button { Text = text, AutoSize = true, Padding = new Padding(6, 3, 6, 3) }; button.Click += handler; return button; }
}
