using System.Runtime.InteropServices;

namespace PadDim;

public sealed class Dimmer : IDisposable
{
    private readonly List<Shade> shades = [];
    private readonly HardwareDimmer hardware;
    private readonly System.Windows.Forms.Timer fadeTimer = new() { Interval = 30 };
    private bool requested;
    private long fadeStart;
    private int fadeDuration;
    private double targetOpacity;
    public bool IsDimmed => shades.Count > 0 || hardware.IsDimmed;
    public string Status => hardware.Status;
    public Dimmer(bool persistentRecovery = true)
    {
        hardware = new(persistentRecovery);
        fadeTimer.Tick += (_, _) =>
        {
            double progress = fadeDuration <= 0 ? 1 : Math.Clamp((Environment.TickCount64 - fadeStart) / (double)fadeDuration, 0, 1);
            foreach (var shade in shades) shade.Opacity = targetOpacity * progress;
            if (progress >= 1) fadeTimer.Stop();
        };
    }
    public void RestoreForShutdown() { Restore(retry: true); hardware.RestoreForShutdown(); }
    public void Dim(int percent, bool useHardware, int brightnessPercent, int fadeMilliseconds, bool useOverlayWithHardware = false)
    {
        if (requested) return;
        requested = true;
        if (useHardware) hardware.Request(true, brightnessPercent, fadeMilliseconds);
        if (useHardware && !useOverlayWithHardware) return;
        try
        {
            fadeStart = Environment.TickCount64; fadeDuration = fadeMilliseconds;
            targetOpacity = Math.Clamp(percent, 1, 90) / 100d;
            foreach (var screen in Screen.AllScreens)
            {
                var shade = new Shade(screen.Bounds, percent);
                if (fadeDuration > 0) shade.Opacity = 0;
                shades.Add(shade);
                shade.Show();
            }
            if (fadeDuration > 0) fadeTimer.Start();
        }
        catch { Restore(); throw; }
    }
    public void Restore(bool retry = false)
    {
        requested = false;
        fadeTimer.Stop();
        hardware.Request(false, retry: retry);
        foreach (var shade in shades) shade.Dispose();
        shades.Clear();
    }
    public void Dispose() { Restore(); fadeTimer.Dispose(); hardware.Dispose(); }
    private sealed class Shade : Form
    {
        public Shade(Rectangle bounds, int percent)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds; BackColor = Color.Black; Opacity = Math.Clamp(percent, 1, 90) / 100d;
            ShowInTaskbar = false; TopMost = true; AutoScaleMode = AutoScaleMode.None;
        }
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x00000020 | 0x00000080; return p; }
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x84) { m.Result = -1; return; } // HTTRANSPARENT
            base.WndProc(ref m);
        }
    }
}
