using System.ComponentModel;
using System.Management;
using System.Runtime.InteropServices;

namespace PadDim;

internal static class BrightnessTargets
{
    public static List<IBrightnessTarget> Discover(List<string> diagnostics)
    {
        var result = new List<IBrightnessTarget>();
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorBrightness WHERE Active = TRUE");
            searcher.Options.Timeout = TimeSpan.FromSeconds(5);
            using var collection = searcher.Get();
            foreach (ManagementObject item in collection)
            {
                using (item)
                    result.Add(new WmiTarget((string)item["InstanceName"], Convert.ToUInt32(item["CurrentBrightness"])));
            }
            diagnostics.Add($"WMI方式: {result.Count} 件対応");
        }
        catch (Exception ex) { diagnostics.Add($"WMI方式: 利用不可 ({ex.Message})"); }
        int ddcCount = 0;
        MonitorEnum callback = (nint monitor, nint dc, ref Rect bounds, nint data) =>
        {
            // Never let exceptions unwind across the native callback boundary.
            try
            {
                if (!GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out uint count)) return true;
                if (count == 0) return true;
                var physical = new PhysicalMonitor[count];
                if (!GetPhysicalMonitorsFromHMONITOR(monitor, count, physical)) return true;
                foreach (var entry in physical)
                {
                    var target = new DdcTarget(entry.Handle, entry.Description);
                    try { target.Capture(); result.Add(target); ddcCount++; }
                    catch (Exception ex) { diagnostics.Add($"{entry.Description}: DDC/CI利用不可 ({ex.Message})"); target.Dispose(); }
                }
            }
            catch (Exception ex) { diagnostics.Add($"DDC/CI 列挙エラー: {ex.Message}"); }
            return true;
        };
        if (!EnumDisplayMonitors(0, 0, callback, 0)) diagnostics.Add("ディスプレイ列挙に失敗しました");
        diagnostics.Add($"DDC/CI方式: {ddcCount} 件対応（内蔵・外付けの区別ではありません）");
        return result;
    }

    private sealed class WmiTarget(string instance, uint original) : IBrightnessTarget
    {
        public string Name => $"WMI: {instance}";
        public uint Minimum => 0;
        public uint Maximum => 100;
        public uint Original => original;
        public void Set(uint value)
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorBrightnessMethods");
            searcher.Options.Timeout = TimeSpan.FromSeconds(5);
            using var items = searcher.Get();
            foreach (ManagementObject item in items)
            {
                using (item)
                {
                    if (!string.Equals((string)item["InstanceName"], instance, StringComparison.OrdinalIgnoreCase)) continue;
                    using var parameters = item.GetMethodParameters("WmiSetBrightness");
                    parameters["Timeout"] = 0u;
                    parameters["Brightness"] = (byte)value;
                    using var output = item.InvokeMethod("WmiSetBrightness", parameters, new InvokeMethodOptions { Timeout = TimeSpan.FromSeconds(5) });
                    // Some display providers complete successfully with an empty output object;
                    // others return a Boolean rather than the documented UInt32 status.
                    object? returned = output?.Properties.Cast<PropertyData>().FirstOrDefault(p => p.Name == "ReturnValue")?.Value;
                    bool failed = returned is bool success ? !success : returned is not null && Convert.ToUInt32(returned) != 0;
                    if (failed) throw new InvalidOperationException($"WMI 輝度設定に失敗しました ({returned})");
                    return;
                }
            }
            throw new InvalidOperationException("画面が見つかりません");
        }
        public void Dispose() { }
    }
    private sealed class DdcTarget(nint handle, string name) : IBrightnessTarget
    {
        private nint handle = handle;
        private bool vcp;
        public string Name => $"DDC/CI: {name}";
        public uint Minimum { get; private set; }
        public uint Maximum { get; private set; }
        public uint Original { get; private set; }
        public void Capture()
        {
            if (GetMonitorBrightness(handle, out uint minimum, out uint current, out uint maximum))
            { Minimum = minimum; Original = current; Maximum = maximum; }
            else if (GetVCPFeatureAndVCPFeatureReply(handle, 0x10, out _, out current, out maximum))
            { vcp = true; Minimum = 0; Original = current; Maximum = maximum; }
            else throw new Win32Exception(Marshal.GetLastWin32Error());
            if (Maximum <= Minimum || Original < Minimum || Original > Maximum) throw new InvalidOperationException("輝度範囲が不正です");
        }
        public void Set(uint value)
        {
            bool ok = vcp ? SetVCPFeature(handle, 0x10, value) : SetMonitorBrightness(handle, value);
            if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        public void Dispose() { if (handle != 0) { DestroyPhysicalMonitor(handle); handle = 0; } }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PhysicalMonitor { public nint Handle; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description; }
    private delegate bool MonitorEnum(nint monitor, nint dc, ref Rect bounds, nint data);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorEnum callback, nint data);
    [DllImport("dxva2.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(nint monitor, out uint count);
    [DllImport("dxva2.dll", SetLastError = true, CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetPhysicalMonitorsFromHMONITOR(nint monitor, uint count, [Out] PhysicalMonitor[] physical);
    [DllImport("dxva2.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorBrightness(nint monitor, out uint min, out uint current, out uint max);
    [DllImport("dxva2.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetMonitorBrightness(nint monitor, uint value);
    [DllImport("dxva2.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetVCPFeatureAndVCPFeatureReply(nint monitor, byte code, out uint type, out uint current, out uint max);
    [DllImport("dxva2.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetVCPFeature(nint monitor, byte code, uint value);
    [DllImport("dxva2.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyPhysicalMonitor(nint monitor);
}
