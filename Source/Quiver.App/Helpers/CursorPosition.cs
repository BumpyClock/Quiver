using System;
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace Quiver.App.Helpers;

internal static partial class CursorPosition
{
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const int MdtEffectiveDpi = 0;
    private const double DefaultDpi = 96;

    /// <summary>
    /// Returns a square of <paramref name="sizeDips"/> centered on the cursor and kept inside the cursor
    /// monitor's work area, in physical pixels for that monitor's DPI.
    /// </summary>
    public static RectInt32 SquareCenteredOnCursor(double sizeDips)
    {
        if (!GetCursorPos(out var cursor))
        {
            cursor = new ScreenPoint(0, 0);
        }

        double scale = 1.0;
        Rect? workArea = null;
        var monitor = MonitorFromPoint(cursor, MonitorDefaultToNearest);
        if (monitor != IntPtr.Zero)
        {
            if (GetDpiForMonitor(monitor, MdtEffectiveDpi, out uint dpiX, out _) == 0 && dpiX > 0)
            {
                scale = dpiX / DefaultDpi;
            }

            MonitorInfo monitorInfo = new() { CbSize = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref monitorInfo))
            {
                workArea = monitorInfo.RcWork;
            }
        }

        int size = (int)Math.Round(sizeDips * scale);
        int x = cursor.X - size / 2;
        int y = cursor.Y - size / 2;
        if (workArea is { } area)
        {
            x = Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - size));
            y = Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - size));
        }

        return new RectInt32(x, y, size, size);
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out ScreenPoint lpPoint);

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromPoint(ScreenPoint pt, uint dwFlags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private struct ScreenPoint(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int CbSize;
        public Rect RcMonitor;
        public Rect RcWork;
        public uint DwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
