using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Quiver.App.Helpers;
using System;
using Windows.Foundation;
using Windows.Graphics;

namespace Quiver.App.Controls;

internal readonly record struct ListFrame(double Width, double Height, double Body, double FooterTop);

internal readonly record struct ListMetrics(double Row, double Empty, double Footer)
{
    public const double ShadowMargin = 20;
    public const double ContentWidth = 296;
    public const double Padding = 8;
    public const double FooterGap = 8;
    public const double CornerRadius = 12;
    public const double MaximumHeight = 600;
    public const double CursorGap = 12;
    public const double WindowWidth = ContentWidth + 2 * ShadowMargin;
    private const double HeightStep = 4;

    public static ListMetrics Compact { get; } = new(52, 52, 56);
    public static double RowHeight => Compact.Row;
    public static double WindowHeight(int count) => Compact.Height(count);
    public static double ViewportHeight(int windowPixels, double scale) => Compact.Viewport(windowPixels, scale);
    public static Point Anchor => new(ShadowMargin - CursorGap, ShadowMargin + Padding);

    public static ListMetrics Measure(FrameworkElement row, FrameworkElement empty, ContentControl footer) => new(
        Fit(row, Compact.Row), Fit(empty, Compact.Empty), Fit((FrameworkElement)footer.Content, Compact.Footer,
            footer.Padding.Top + footer.Padding.Bottom + footer.BorderThickness.Top + footer.BorderThickness.Bottom));

    private double NonBodyHeight => 2 * ShadowMargin + 2 * Padding + FooterGap + Footer;
    private int MaximumVisibleRows => Math.Max(1, (int)((MaximumHeight - NonBodyHeight) / Row));

    public double Height(int count) => NonBodyHeight + (count == 0 ? Empty : Math.Clamp(count, 1, MaximumVisibleRows) * Row);
    public SelectorGeometry Geometry(int count) => new(WindowWidth, Height(count), Anchor, SelectorPlacement.BesideCursor);

    public double Viewport(int windowPixels, double scale)
    {
        int rows = Math.Max(0, (int)Math.Floor(((windowPixels + 0.5) / scale - NonBodyHeight) / Row));
        while (rows > 0 && CursorPosition.NativeLength(NonBodyHeight + rows * Row, scale) > windowPixels) rows--;
        return rows * Row;
    }

    public ListFrame Frame(SizeInt32 window, double scale, int count)
    {
        double height = window.Height / scale;
        return new(Math.Max(0, window.Width / scale - 2 * ShadowMargin), Math.Max(0, height - 2 * ShadowMargin),
            count == 0 ? Empty : Viewport(window.Height, scale), height - ShadowMargin - Padding - Footer);
    }

    public static Rect Bounds(double windowWidth, double windowHeight) => new(ShadowMargin, ShadowMargin,
        Math.Max(0, windowWidth - 2 * ShadowMargin), Math.Max(0, windowHeight - 2 * ShadowMargin));

    private static double Fit(FrameworkElement element, double minimum, double chrome = 0)
    {
        element.Measure(new Size(ContentWidth - 2 * Padding, double.PositiveInfinity));
        double height = element.DesiredSize.Height - element.Margin.Top - element.Margin.Bottom + chrome;
        return Math.Max(minimum, Math.Ceiling(height / HeightStep) * HeightStep);
    }
}
