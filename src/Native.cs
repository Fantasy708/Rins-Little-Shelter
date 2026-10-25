using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
namespace LittleShelter;

internal static class Native
{
    [DllImport("kernel32.dll")] private static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);
    [DllImport("wtsapi32.dll", EntryPoint="WTSQuerySessionInformationW", SetLastError=true)]
    private static extern bool QuerySessionInformation(nint server, uint sessionId, int infoClass, out nint buffer, out int bytes);
    [DllImport("wtsapi32.dll")] private static extern void WTSFreeMemory(nint buffer);
    // WTSINFOEX's union is aligned to eight bytes on the supported x64 runtime.
    // Read only its fixed prefix, never the user/domain name fields.
    internal static bool? DecodeSessionLockState(nint buffer, int bytes, uint sessionId)
    {
        if (buffer == 0 || bytes < 20 || Marshal.ReadInt32(buffer) != 1
            || unchecked((uint)Marshal.ReadInt32(buffer, 8)) != sessionId) return null;
        return Marshal.ReadInt32(buffer, 16) switch { 0 => true, 1 => false, _ => null };
    }
    internal static bool? QuerySessionLocked()
    {
        if (!ProcessIdToSessionId((uint)Environment.ProcessId, out uint sessionId)) return null;
        nint buffer = 0;
        try
        {
            if (!QuerySessionInformation(0, sessionId, 25, out buffer, out int bytes)) return null;
            return DecodeSessionLockState(buffer, bytes, sessionId);
        }
        finally { if (buffer != 0) WTSFreeMemory(buffer); }
    }
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(nint h, out Rect r);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point p);
    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] private static extern nint GetStyle(nint h, int n);
    [DllImport("user32.dll", EntryPoint="SetWindowLongPtrW")] private static extern nint SetStyle(nint h, int n, nint style);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(nint h, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(nint h, int id);
    [DllImport("user32.dll")] public static extern nint MonitorFromWindow(nint h, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(nint h, ref MonitorInfo info);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetClassName(nint h, System.Text.StringBuilder value, int length);
    [DllImport("kernel32.dll")] public static extern bool AttachConsole(uint process);
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    public static void Configure(Window window, bool through)
    {
        var h = new WindowInteropHelper(window).Handle;
        var style = GetStyle(h, -20).ToInt64();
        style |= 0x08000000L; // WS_EX_NOACTIVATE
        style = window.Tag as string == "qa" ? style & ~0x80L : style | 0x80L;
        style = through ? style | 0x20L : style & ~0x20L;
        SetStyle(h, -20, (nint)style);
    }
    public static bool Fullscreen(Window window)
    {
        var h = GetForegroundWindow();
        if (h == 0 || h == new WindowInteropHelper(window).Handle) return false;
        var name = new System.Text.StringBuilder(128); GetClassName(h, name, 128);
        if (name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd") return false;
        if (MonitorFromWindow(h, 2) != MonitorFromWindow(new WindowInteropHelper(window).Handle, 2)) return false;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(h, 2), ref info) || !GetWindowRect(h, out var rect)) return false;
        return WindowPolicy.IsFullscreen(GetStyle(h, -16).ToInt64(), (rect.Left,rect.Top,rect.Right,rect.Bottom), (info.Monitor.Left,info.Monitor.Top,info.Monitor.Right,info.Monitor.Bottom));
    }
    public static void KeepOnScreen(Window window, bool initial = false)
    {
        var h = new WindowInteropHelper(window).Handle;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(h, 2), ref info)) return;
        var dpi = VisualTreeHelper.GetDpi(window);
        double l = info.Work.Left / dpi.DpiScaleX, t = info.Work.Top / dpi.DpiScaleY;
        double r = info.Work.Right / dpi.DpiScaleX, b = info.Work.Bottom / dpi.DpiScaleY;
        if (initial) { window.Left = r - window.Width - 16; window.Top = b - window.Height - 12; }
        else { window.Left = Math.Clamp(window.Left, l, Math.Max(l, r - window.Width)); window.Top = Math.Clamp(window.Top, t, Math.Max(t, b - window.Height)); }
    }
}
