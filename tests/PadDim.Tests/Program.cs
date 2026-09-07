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

Check(BrightnessSession.DimValue(10, 210, 180, 20) == 50, "Map percentage to native brightness range");
Check(BrightnessSession.DimValue(0, 100, 10, 20) == 10, "Never brighten an already dim display");
Check(BrightnessSession.FadeValue(80, 20, 0.5) == 50, "Fade follows interpolated brightness");
var target = new FakeTarget(80);
var session = new BrightnessSession();
long time = 0;
session.Dim([target], 20, () => true, 1000, ms => time += ms, () => time);
Check(target.Values.Count > 2 && target.Values[^1] == 20, "Fade reaches target in multiple steps");
Check(target.Values.Zip(target.Values.Skip(1)).All(pair => pair.First >= pair.Second), "Fade only decreases brightness");
int writes = target.Values.Count;
session.Restore();
Check(target.Values.Count == writes + 1 && target.Values[^1] == 80, "Restore is one immediate write without fade-in");
Check(target.Disposed && session.PendingCount == 0, "Restored target releases resources");
var cancelled = new FakeTarget(90);
time = 0;
session.Dim([cancelled], 20, () => time < 350, 1000, ms => time += ms, () => time);
Check(cancelled.Values[^1] > 20, "Activity cancels an in-progress fade");
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
