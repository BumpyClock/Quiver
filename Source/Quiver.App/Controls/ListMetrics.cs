using Quiver.App.Helpers;
using System;
using Windows.Foundation;

namespace Quiver.App.Controls;

internal static class ListMetrics
{
    public const double ShadowMargin = 20;
    public const double ContentWidth = 296;
    public const double RowHeight = 52;
    public const double Padding = 8;
    public const double FooterHeight = 56;
    public const double FooterGap = 8;
    public const double CornerRadius = 12;
    public const double MaximumHeight = 600;
    public const double CursorGap = 12;
    public const double WindowWidth = ContentWidth + 2 * ShadowMargin;

    private const double NonRowHeight = 2 * ShadowMargin + 2 * Padding + FooterGap + FooterHeight;
    private const int MaximumVisibleRows = (int)((MaximumHeight - NonRowHeight) / RowHeight);

    public static double WindowHeight(int count) => NonRowHeight + Math.Clamp(count, 1, MaximumVisibleRows) * RowHeight;
    public static double ViewportHeight(int windowPixels, double scale)
    {
        int rows = Math.Max(0, (int)Math.Floor(((windowPixels + 0.5) / scale - NonRowHeight) / RowHeight));
        while (rows > 0 && CursorPosition.NativeLength(NonRowHeight + rows * RowHeight, scale) > windowPixels) rows--;
        return rows * RowHeight;
    }
    public static Point Anchor => new(ShadowMargin - CursorGap, ShadowMargin + Padding);
    public static Rect Bounds(double windowWidth, double windowHeight) => new(ShadowMargin, ShadowMargin,
        Math.Max(0, windowWidth - 2 * ShadowMargin), Math.Max(0, windowHeight - 2 * ShadowMargin));
}
