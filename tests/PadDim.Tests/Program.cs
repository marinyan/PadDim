using PadDim;

static void Check(bool condition, string description)
{ if (!condition) throw new Exception(description); Console.WriteLine($"PASS {description}"); }
var idle = new IdlePolicy(1000);
Check(!idle.ShouldDim(10999, 10, true, true), "Wait for entire timeout");
Check(idle.ShouldDim(11000, 10, true, true), "Dim at timeout");
Check(!idle.ShouldDim(11000, 10, true, false), "Input failure prevents dimming");
Check(!idle.ShouldDim(11000, 10, false, true), "Pause prevents dimming");
idle.Record(12000, "gamepad");
Check(!idle.ShouldDim(12000, 10, true, true), "Gamepad input restores activity");
Check(idle.ShouldDim(22000, 10, true, true), "Timeout restarts after input");
var axes = new AxisActivity(4000);
Check(!axes.Sample([32767, 0, 65535]), "Learn centered sticks and edge-resting triggers");
Check(!axes.Sample([33000, 200, 65400]), "Ignore analog jitter");
Check(axes.Sample([45000, 0, 65535]), "Detect stick movement");
Check(axes.Sample([45000, 0, 65535]), "Held stick remains active");
Check(!axes.Sample([32767, 0, 65535]), "Released stick becomes neutral");
Check(axes.Sample([32767, 8000, 65535]), "Detect trigger movement");
axes.Reset();
Check(!axes.Sample([100, 200, 300]), "Recalibrate another neutral position");

Check(BrightnessSession.DimValue(10, 210, 180, 20) == 44, "Scale relative brightness above device minimum");
Check(BrightnessSession.DimValue(0, 100, 10, 20) == 2, "Dim even when current brightness is below selected percentage");
Check(BrightnessSession.DimValue(0, 100, 40, 50) == 20, "50 percent halves the original brightness");
Check(BrightnessSession.DimValue(0, 100, 40, 100) == 40, "100 percent preserves original brightness");
Check(BrightnessSession.DimValue(10, 100, 40, 0) == 10, "Zero percent respects device minimum");
Check(BrightnessSession.DimValue(10, 100, 10, 50) == 10, "Already at minimum remains at minimum");
Check(BrightnessSession.DimValue(0, 100, 33, 50) == 16, "Relative value rounds to an integer device step");
Check(BrightnessSession.FadeValue(80, 20, 0.5) == 50, "Fade follows interpolated brightness");
var target = new FakeTarget(80);
var session = new BrightnessSession();
long time = 0;
session.Dim([target], 20, () => true, 1000, ms => time += ms, () => time);
Check(target.Values.Count > 2 && target.Values[^1] == 16, "Fade reaches relative target in multiple steps");
Check(target.Values.Zip(target.Values.Skip(1)).All(pair => pair.First >= pair.Second), "Fade only decreases brightness");
int writes = target.Values.Count;
session.Restore();
Check(target.Values.Count == writes + 1 && target.Values[^1] == 80, "Restore is one immediate write without fade-in");
Check(target.Disposed && session.PendingCount == 0, "Restored target releases resources");
var cancelled = new FakeTarget(90);
time = 0;
session.Dim([cancelled], 20, () => time < 350, 1000, ms => time += ms, () => time);
Check(cancelled.Values[^1] > 18, "Activity cancels an in-progress fade");
session.Restore();
Check(cancelled.Values[^1] == 90, "Cancelled fade restores original brightness");
var failing = new FakeTarget(70) { FailNext = true };
Check(session.Dim([failing], 20, () => true).Count == 1 && session.PendingCount == 1, "Failed write keeps original for recovery");
failing.FailNext = true;
Check(session.Restore().Count == 1 && session.PendingCount == 1 && !failing.Disposed, "Failed restore retains target for retry");
Check(session.Restore().Count == 0 && failing.Values[^1] == 70, "Retry restores preserved original");
var untouched = new FakeTarget(60);
session.Dim([untouched], 20, () => false);
Check(untouched.Values.Count == 0 && untouched.Disposed, "Cancellation before writes leaves display untouched");

var legacy = System.Text.Json.JsonSerializer.Deserialize<Settings>("{\"BrightnessPercent\":0,\"IdleSeconds\":600}")!;
Check(legacy.BrightnessRatioPercent == 20 && legacy.IdleSeconds == 600, "Legacy absolute value resets to relative default without losing timer");
var combinedSettings = new Settings { UseHardwareBrightness = true, UseOverlayWithHardware = true, BrightnessRatioPercent = 50 };
var roundTrip = System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(combinedSettings))!;
Check(roundTrip.UseHardwareBrightness && roundTrip.UseOverlayWithHardware && roundTrip.BrightnessRatioPercent == 50, "Combined mode and relative ratio survive settings round trip");

Check(Settings.FromJson("{\"IdleSeconds\":0}").IdleSeconds == 10, "Zero stored timeout is bounded");
Check(Settings.FromJson("{\"IdleSeconds\":-100}").IdleSeconds == 10, "Negative stored timeout is bounded");
Check(Settings.FromJson("{\"IdleSeconds\":2147483647}").IdleSeconds == 86400, "Large stored timeout is bounded");
foreach (string invalid in new[] { "NaN", "\"NaN\"", "Infinity", "1e1000", "null", "-1.5" })
{
    bool rejected = false;
    try { Settings.FromJson("{\"IdleSeconds\":" + invalid + "}"); }
    catch (System.Text.Json.JsonException) { rejected = true; }
    Check(rejected, $"Reject malformed stored timeout: {invalid}");
}
Exception? controlError = null;
var controlThread = new Thread(() =>
{
    try
    {
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("ja-JP");
        using var input = new MinutesInput();
        foreach (var item in new[] { (5m, "5"), (20m, "20"), (0.5m, "0.5"), (1.25m, "1.25") })
        {
            input.Value = item.Item1;
            Check(input.Text == item.Item2, $"Minute display: {item.Item2}");
        }
        foreach (string invalid in new[] { "NaN", "Infinity", "-Infinity", "", "-", "garbage", new string('9', 100) })
        {
            input.Value = 5;
            input.Text = invalid;
            input.CommitEdit();
            Check(input.Value == 5 && input.Text == "5", $"Invalid minute entry restores previous value: {invalid}");
        }
        foreach (string low in new[] { "0", "-5", "0.001" })
        {
            input.Value = 5; input.Text = low; input.CommitEdit();
            Check((int)Math.Round(input.Value * 60m, MidpointRounding.AwayFromZero) == 10 && input.Text == "0.17", $"Minute entry bounded to minimum: {low}");
        }
        input.Text = "999999"; input.CommitEdit();
        Check(input.Value == 1440 && input.Text == "1440", "Minute entry bounded to maximum");
        foreach (int seconds in new[] { 10, 11, 29, 30, 59, 61, 300, 1200, 86399, 86400 })
        {
            input.Value = seconds / 60m; input.CommitEdit();
            Check((int)Math.Round(input.Value * 60m, MidpointRounding.AwayFromZero) == seconds, $"Existing timeout preserved: {seconds}s");
        }
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
        input.Text = "1,5"; input.CommitEdit();
        Check(input.Value == 1.5m && input.Text == "1,5", "Localized decimal separator works");
    }
    catch (Exception ex) { controlError = ex; }
});
controlThread.SetApartmentState(ApartmentState.STA);
controlThread.Start(); controlThread.Join();
if (controlError is not null) throw new Exception("Minutes input regression", controlError);

sealed class FakeTarget(uint original) : IBrightnessTarget
{
    public string Name => "Test display";
    public uint Minimum => 0;
    public uint Maximum => 100;
    public uint Original => original;
    public List<uint> Values { get; } = [];
    public bool Disposed { get; private set; }
    public bool FailNext { get; set; }
    public void Set(uint value) { if (FailNext) { FailNext = false; throw new IOException("Simulated driver failure"); } Values.Add(value); }
    public void Dispose() => Disposed = true;
}
