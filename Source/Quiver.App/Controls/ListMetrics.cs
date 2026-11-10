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

    public static double WindowHeight(int count) => Math.Min(MaximumHeight,
        2 * ShadowMargin + 2 * Padding + Math.Max(1, count) * RowHeight + FooterGap + FooterHeight);
    public static Point Anchor => new(ShadowMargin - CursorGap, ShadowMargin + Padding);
    public static Rect Bounds(double windowWidth, double windowHeight) => new(ShadowMargin, ShadowMargin,
        Math.Max(0, windowWidth - 2 * ShadowMargin), Math.Max(0, windowHeight - 2 * ShadowMargin));
}
