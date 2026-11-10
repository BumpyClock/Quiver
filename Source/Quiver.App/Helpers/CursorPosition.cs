using System;
using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Graphics;

namespace Quiver.App.Helpers;

internal enum SelectorPlacement { Centered, BesideCursor }

internal readonly record struct SelectorGeometry(double Width, double Height, Point Anchor, SelectorPlacement Placement);

internal readonly record struct CursorSnapshot(PointInt32 Position, RectInt32? WorkArea, double Scale)
{
    public RectInt32 Place(SelectorGeometry geometry) => CursorPosition.CalculateBounds(
        geometry.Width, geometry.Height, geometry.Anchor, Position, WorkArea, Scale, geometry.Placement);
}

internal static partial class CursorPosition
{
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const int MdtEffectiveDpi = 0;
    private const double DefaultDpi = 96;

    public static CursorSnapshot Capture()
    {
        if (!GetCursorPos(out var cursor)) cursor = new ScreenPoint(0, 0);

        double scale = 1.0;
        RectInt32? workArea = null;
        var monitor = MonitorFromPoint(cursor, MonitorDefaultToNearest);
        if (monitor != IntPtr.Zero)
        {
            if (GetDpiForMonitor(monitor, MdtEffectiveDpi, out uint dpiX, out _) == 0 && dpiX > 0)
                scale = dpiX / DefaultDpi;

            MonitorInfo monitorInfo = new() { CbSize = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref monitorInfo))
            {
                var area = monitorInfo.RcWork;
                workArea = new RectInt32(area.Left, area.Top, area.Right - area.Left, area.Bottom - area.Top);
            }
        }
        return new CursorSnapshot(new PointInt32(cursor.X, cursor.Y), workArea, scale);
    }

    internal static RectInt32 CalculateBounds(double widthDips, double heightDips, Point anchorDips,
        PointInt32 cursor, RectInt32? workArea, double scale, SelectorPlacement placement)
    {
        int width = (int)Math.Round(widthDips * scale);
        int height = (int)Math.Round(heightDips * scale);
        int x = cursor.X - (int)Math.Round(anchorDips.X * scale);
        int y = cursor.Y - (int)Math.Round(anchorDips.Y * scale);
        if (workArea is { } area)
        {
            if (placement == SelectorPlacement.BesideCursor)
            {
                width = Math.Min(width, area.Width);
                height = Math.Min(height, area.Height);
                if (x + width > area.X + area.Width)
                    x = cursor.X - width + (int)Math.Round(anchorDips.X * scale);
            }
            x = Math.Clamp(x, area.X, Math.Max(area.X, area.X + area.Width - width));
            y = Math.Clamp(y, area.Y, Math.Max(area.Y, area.Y + area.Height - height));
        }
        return new RectInt32(x, y, width, height);
    }

    internal static double WindowScale(IntPtr hwnd) => GetDpiForWindow(hwnd) / DefaultDpi;

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(IntPtr hwnd);

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
