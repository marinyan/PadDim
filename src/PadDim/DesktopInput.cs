using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PadDim;

// Observe keyboard/mouse reports directly; the system-wide idle tick can also
// advance for controller reports and must not be labelled keyboard/mouse input.
public sealed class DesktopInput : NativeWindow, IDisposable
{
    private bool keyboard, mouse;
    private readonly Dictionary<nint, Point> absolute = [];
    private readonly PointerInput pointer;
    private readonly SystemInputActivity systemActivity = new();
    public bool UseSystemInputTime { get; set; }
    private bool systemHealthy = true;
    private bool rawHealthy = true;
    public bool Healthy => rawHealthy && pointer.Healthy && (!UseSystemInputTime || systemHealthy);
    public DesktopInput()
    {
        CreateHandle(new CreateParams { Caption = "PadDim input", Parent = -3 });
        if (!Register(0x100, Handle)) { DestroyHandle(); throw new Win32Exception(Marshal.GetLastWin32Error()); }
        try { pointer = new(); }
        catch { Register(1, 0); DestroyHandle(); throw; }
    }
    private static bool Register(uint flags, nint target) => RegisterRawInputDevices(
        [new() { Page = 1, Usage = 2, Flags = flags, Target = target }, new() { Page = 1, Usage = 6, Flags = flags, Target = target }],
        2, (uint)Marshal.SizeOf<Device>());
    public string? Poll()
    {
        var activity = pointer.Poll();
        if (keyboard) activity |= DesktopActivity.Keyboard;
        if (mouse) activity |= DesktopActivity.Mouse;
        var labels = new List<string>();
        if (activity.HasFlag(DesktopActivity.Keyboard)) labels.Add("キーボード");
        if (activity.HasFlag(DesktopActivity.Mouse)) labels.Add("マウス／リモート入力");
        if (activity.HasFlag(DesktopActivity.Touch)) labels.Add("タッチ");
        if (activity.HasFlag(DesktopActivity.Pen)) labels.Add("ペン");
        if (UseSystemInputTime)
        {
            var last = new LastInput { Size = (uint)Marshal.SizeOf<LastInput>() };
            systemHealthy = GetLastInputInfo(ref last);
            if (systemHealthy)
            {
                if (systemActivity.Sample(last.Tick, true) && labels.Count == 0)
                    labels.Add("タッチ／リモート等（入力時刻）");
            }
            else systemActivity.Reset();
        }
        else { systemActivity.Reset(); systemHealthy = true; }
        string? result = labels.Count == 0 ? null : string.Join(" + ", labels);
        keyboard = mouse = false;
        return result;
    }
    public static bool HasRelativeMouseActivity(ushort buttons, int x, int y) => buttons != 0 || x != 0 || y != 0;
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0xff)
        {
            nint buffer = 0;
            try
            {
                uint size = 0, header = (uint)Marshal.SizeOf<Header>();
                if (GetRawInputData(m.LParam, 0x10000003, 0, ref size, header) == uint.MaxValue || size < header || size > 65536)
                    throw new InvalidDataException();
                buffer = Marshal.AllocHGlobal((int)size);
                if (GetRawInputData(m.LParam, 0x10000003, buffer, ref size, header) == uint.MaxValue) throw new InvalidDataException();
                var info = Marshal.PtrToStructure<Header>(buffer);
                if (info.Type == 1 && size >= header + 16) keyboard = true;
                else if (info.Type == 0 && size >= header + 24)
                {
                    var data = buffer + (int)header;
                    ushort flags = (ushort)Marshal.ReadInt16(data);
                    ushort buttons = (ushort)Marshal.ReadInt16(data, 4);
                    int x = Marshal.ReadInt32(data, 12), y = Marshal.ReadInt32(data, 16);
                    if ((flags & 1) == 0) mouse |= HasRelativeMouseActivity(buttons, x, y);
                    else
                    {
                        var position = new Point(x, y);
                        mouse |= buttons != 0 || (absolute.TryGetValue(info.Device, out var old) && old != position);
                        absolute[info.Device] = position;
                    }
                }
                rawHealthy = true;
            }
            catch { rawHealthy = false; }
            finally { if (buffer != 0) Marshal.FreeHGlobal(buffer); }
        }
        base.WndProc(ref m);
    }
    public void Dispose() { pointer.Dispose(); Register(1, 0); DestroyHandle(); }
    [StructLayout(LayoutKind.Sequential)] private struct LastInput { public uint Size, Tick; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetLastInputInfo(ref LastInput input);
    [StructLayout(LayoutKind.Sequential)] private struct Device { public ushort Page, Usage; public uint Flags; public nint Target; }
    [StructLayout(LayoutKind.Sequential)] private struct Header { public uint Type, Size; public nint Device, WParam; }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RegisterRawInputDevices(Device[] devices, uint count, uint size);
    [DllImport("user32.dll")] private static extern uint GetRawInputData(nint input, uint command, nint data, ref uint size, uint headerSize);
}
