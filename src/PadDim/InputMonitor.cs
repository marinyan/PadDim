using System.Runtime.InteropServices;
using Vortice.DirectInput;

namespace PadDim;

public sealed class InputMonitor : IDisposable
{
    private readonly IDirectInput8 directInput = DInput.DirectInput8Create();
    private readonly Dictionary<Guid, Pad> pads = [];
    private readonly nint window;
    private long nextScan;
    private readonly DesktopInput desktop = new();
    private bool scanHealthy = true;
    private readonly bool[] xConnected = new bool[4];
    private readonly bool[] xWasActive = new bool[4];
    public bool Healthy { get; private set; } = true;
    public bool IsCalibrating => pads.Values.Any(p => p.IsCalibrating);
    public string Status { get; private set; } = "接続を確認中";
    public int AxisThreshold { get; set; } = 4000;
    public int StickDeadzone { get; set; } = 8000;
    public InputMonitor(nint window) => this.window = window;

    public string? Poll(long now)
    {
        var activities = new List<string>();
        string? activity = null;
        Healthy = true;
        var lines = new List<string>();
        var desktopActivity = desktop.Poll();
        if (desktopActivity is not null) activities.Add(desktopActivity);
        if (!desktop.Healthy) { Healthy = false; lines.Add("キーボード / マウス: 取得失敗"); }
        else lines.Add($"キーボード / マウス: {(desktopActivity is null ? "入力なし" : desktopActivity + "の入力を検出")}");

        for (uint slot = 0; slot < 4; slot++)
        {
            uint result = XInputGetState(slot, out var state);
            bool connected = result == 0;
            if (connected != xConnected[slot]) activity = "XInput 接続変更";
            xConnected[slot] = connected;
            if (!connected)
            {
                xWasActive[slot] = false;
                if (result != 1167) { Healthy = false; lines.Add($"XInput {slot + 1}: エラー {result}"); }
                continue;
            }
            bool active = state.Buttons != 0 || state.LeftTrigger > 30 || state.RightTrigger > 30 ||
                Math.Abs((int)state.LX) > StickDeadzone || Math.Abs((int)state.LY) > StickDeadzone ||
                Math.Abs((int)state.RX) > StickDeadzone || Math.Abs((int)state.RY) > StickDeadzone;
            if (active || xWasActive[slot]) activity = $"XInput {slot + 1}";
            xWasActive[slot] = active;
            if (active) activities.Add($"XInput {slot + 1}");
            lines.Add($"XInput {slot + 1}: {(active ? "操作中" : "監視中")}");
        }

        if (now >= nextScan)
        {
            nextScan = now + 3000;
            try
            {
                var devices = directInput.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly);
                var present = devices.Select(d => d.InstanceGuid).ToHashSet();
                foreach (var id in pads.Keys.Where(id => !present.Contains(id)).ToArray())
                { pads[id].Dispose(); pads.Remove(id); activity = "DirectInput 切断"; }
                scanHealthy = true;
                foreach (var device in devices)
                {
                    if (pads.ContainsKey(device.InstanceGuid)) continue;
                    IDirectInputDevice8? native = null;
                    try
                    {
                        native = directInput.CreateDevice(device.InstanceGuid);
                        native.SetCooperativeLevel(window, CooperativeLevel.Background | CooperativeLevel.NonExclusive).CheckError();
                        native.SetDataFormat<RawJoystickState>().CheckError();
                        native.Properties.Range = new InputRange(0, 65535);
                        pads.Add(device.InstanceGuid, new Pad(native, device.InstanceName, AxisThreshold));
                        native = null; activity = "DirectInput 接続";
                    }
                    catch (Exception ex) { scanHealthy = false; scanError = $"{device.InstanceName}: {ex.Message}"; }
                    finally { native?.Dispose(); }
                }
            }
            catch (Exception ex) { scanHealthy = false; scanError = ex.Message; }
        }
        if (!scanHealthy) { Healthy = false; lines.Add($"DirectInput 列挙/初期化失敗: {scanError}"); }
        foreach (var pad in pads.Values)
        {
            try
            {
                if (pad.Read()) activities.Add($"DirectInput: {pad.Name}");
                lines.Add($"DirectInput: {pad.Name} — {pad.Status}");
            }
            catch (Exception ex)
            {
                Healthy = false;
                lines.Add($"DirectInput: {pad.Name} — 取得不可（減光を保留）{ex.Message}");
            }
        }
        if (!xConnected.Any(c => c) && pads.Count == 0 && scanHealthy) lines.Add("ゲームパッド未接続（3秒ごとに再検索）");
        Status = string.Join(Environment.NewLine, lines);
        if (activity is not null) activities.Add(activity);
        return activities.Count == 0 ? null : string.Join(" / ", activities.Distinct());
    }
    private string scanError = "";
    public void Calibrate()
    {
        foreach (var pad in pads.Values) pad.Calibrate(AxisThreshold);
    }
    public void Dispose() { foreach (var pad in pads.Values) pad.Dispose(); directInput.Dispose(); desktop.Dispose(); }

    private sealed class Pad(IDirectInputDevice8 device, string name, int threshold) : IDisposable
    {
        public string Name => name;
        private SettlingAxisActivity axes = new(threshold);
        private int currentThreshold = threshold;
        public string Status { get; private set; } = "中立位置の安定待ち";
        public bool IsCalibrating => axes.IsCalibrating;
        private bool wasActive;
        public void Calibrate(int threshold) { currentThreshold = threshold; axes = new(threshold); wasActive = false; }
        public bool Read()
        {
            // Vortice's GetDeviceState wrapper throws on failed HRESULTs.
            device.Poll();
            JoystickState raw;
            try { raw = device.GetCurrentJoystickState(); }
            catch (SharpGen.Runtime.SharpGenException)
            {
                device.Acquire().CheckError();
                Calibrate(currentThreshold);
                device.Poll();
                raw = device.GetCurrentJoystickState();
            }
            int[] values = [raw.X, raw.Y, raw.Z, raw.RotationX, raw.RotationY, raw.RotationZ, raw.Sliders[0], raw.Sliders[1]];
            bool active = axes.Sample(values, Environment.TickCount64);
            string[] names = ["X", "Y", "Z", "Rx", "Ry", "Rz", "Slider1", "Slider2"];
            var reasons = axes.ActiveAxes.Select(i => $"{names[i]}={values[i]}").ToList();
            for (int i = 0; i < 128; i++) if (raw.Buttons[i]) { active = true; reasons.Add($"ボタン{i + 1}"); }
            for (int i = 0; i < 4; i++) if ((raw.PointOfViewControllers[i] & 0xffff) != 0xffff) { active = true; reasons.Add($"POV{i + 1}={raw.PointOfViewControllers[i]}"); }
            Status = active ? "操作中: " + string.Join(", ", reasons) : axes.IsCalibrating ? "中立位置の安定待ち（手を離してください）" : "監視中（入力なし）";
            // Treat calibration as activity so a short idle timeout never dims while learning.
            active |= axes.IsCalibrating;
            bool report = active || wasActive;
            wasActive = active;
            return report;
        }
        public void Dispose() { device.Unacquire(); device.Dispose(); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct XState
    {
        public uint Packet; public ushort Buttons; public byte LeftTrigger, RightTrigger;
        public short LX, LY, RX, RY;
    }
    [DllImport("xinput1_4.dll")] private static extern uint XInputGetState(uint index, out XState state);
}
