using PadDim;
static class CalibrationTests
{
    static void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); }
    public static void Run()
    {
        Check(!DesktopInput.HasRelativeMouseActivity(0, 0, 0), "Stationary mouse report does not reset idle timer");
        Check(DesktopInput.HasRelativeMouseActivity(0, 1, 0), "Actual mouse movement resets idle timer");
        Check(DesktopInput.HasRelativeMouseActivity(1, 0, 0), "Mouse button transition resets idle timer");
        Check(DesktopInput.HasRelativeMouseActivity(0x400, 0, 0), "Mouse wheel resets idle timer");
        var axes = new SettlingAxisActivity(7864);
        int[] placeholder = [32768,32768,32768,32768,32768,32768,0,0];
        int[] real = [32512,32768,33289,0,0,31232,0,0];
        axes.Sample(placeholder, 0);
        axes.Sample(real, 50);
        axes.Sample(real, 500);
        Check(axes.IsCalibrating, "SN30 Pro placeholder is not immediately learned");
        Check(!axes.Sample(real, 1000) && !axes.IsCalibrating, "Stable actual SN30 Pro axes become neutral");
        Check(!axes.Sample(real, 2000), "SN30 Pro resting axes do not keep activity alive");
        int[] moved = (int[])real.Clone(); moved[0] = 50000;
        Check(axes.Sample(moved, 2100) && axes.ActiveAxes.SequenceEqual([0]), "Real stick movement identifies active axis");
        Check(axes.Sample(moved, 30000), "Held stick is never automatically recalibrated away");
        Check(!axes.Sample(real, 30100), "Released stick returns to neutral");
        var late = new SettlingAxisActivity(7864);
        late.Sample(placeholder, 0); late.Sample(real, 900);
        late.Sample(real, 1000);
        Check(late.IsCalibrating, "Late initial report waits for stabilization");
        late.Sample(real, 1400);
        Check(!late.IsCalibrating, "Late report learns after stable interval");
        var manual = new SettlingAxisActivity(7864);
        manual.Sample(real, 5000); manual.Sample(real, 6000);
        Check(!manual.IsCalibrating && !manual.Sample(real, 7000), "Manual reacquisition also uses stable neutral");
    }
}
