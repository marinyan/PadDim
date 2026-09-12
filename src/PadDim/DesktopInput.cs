using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PadDim;

// Observe keyboard/mouse reports directly; the system-wide idle tick can also
// advance for controller reports and must not be labelled keyboard/mouse input.
public sealed class DesktopInput : NativeWindow, IDisposable
{
    private bool keyboard, mouse;
    private readonly Dictionary<nint, Point> absolute = [];
    public bool Healthy { get; private set; } = true;
    public DesktopInput()
    {
        CreateHandle(new CreateParams { Caption = "PadDim input", Parent = -3 });
        if (!Register(0x100, Handle)) { DestroyHandle(); throw new Win32Exception(Marshal.GetLastWin32Error()); }
    }
    private static bool Register(uint flags, nint target) => RegisterRawInputDevices(
        [new() { Page = 1, Usage = 2, Flags = flags, Target = target }, new() { Page = 1, Usage = 6, Flags = flags, Target = target }],
        2, (uint)Marshal.SizeOf<Device>());
    public string? Poll()
    {
        string? result = keyboard && mouse ? "キーボード + マウス" : keyboard ? "キーボード" : mouse ? "マウス" : null;
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
                Healthy = true;
            }
            catch { Healthy = false; }
            finally { if (buffer != 0) Marshal.FreeHGlobal(buffer); }
        }
        base.WndProc(ref m);
    }
    public void Dispose() { Register(1, 0); DestroyHandle(); }
    [StructLayout(LayoutKind.Sequential)] private struct Device { public ushort Page, Usage; public uint Flags; public nint Target; }
    [StructLayout(LayoutKind.Sequential)] private struct Header { public uint Type, Size; public nint Device, WParam; }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RegisterRawInputDevices(Device[] devices, uint count, uint size);
    [DllImport("user32.dll")] private static extern uint GetRawInputData(nint input, uint command, nint data, ref uint size, uint headerSize);
}
