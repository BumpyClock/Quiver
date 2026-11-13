using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using Quiver.App.Helpers;
using Windows.Foundation;

namespace Quiver.App.Controls;

public sealed partial class RadialBrowserItem : BrowserItemControl
{
    private readonly TextBlock nameMeasure = new();
    private readonly TranslateTransform artworkOffset = new();
    private long focusStateToken;

    public RadialBrowserItem()
    {
        InitializeComponent();
        BrowserIcon.RenderTransform = artworkOffset;
        Loaded += (_, _) =>
        {
            BrowserIcon.LayoutUpdated += BrowserIcon_LayoutUpdated;
            focusStateToken = LaunchButton.RegisterPropertyChangedCallback(Control.FocusStateProperty,
                (_, _) => UpdateKeyboardFocus());
            UpdateKeyboardFocus();
            UpdateLabel();
        };
        Unloaded += (_, _) =>
        {
            BrowserIcon.LayoutUpdated -= BrowserIcon_LayoutUpdated;
            LaunchButton.UnregisterPropertyChangedCallback(Control.FocusStateProperty, focusStateToken);
            KeyboardFocusIndicator.Opacity = 0;
        };
        PetalName.SizeChanged += (_, _) => UpdateLabel();
    }

    protected override Button LaunchTarget => LaunchButton;

    protected override void UpdateItemBindings()
    {
        Bindings.Update();
        UpdateLabel();
    }

    internal void SetInwardSide(bool inwardRight)
    {
        BrowserIcon.Margin = new Thickness(inwardRight ? 14 : 26, 1, 0, 0);
        ProfileBadge.Margin = new Thickness(inwardRight ? 12 : 24, 28, 0, 0);
        ShortcutBadge.HorizontalAlignment = inwardRight ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        ShortcutBadge.Margin = new Thickness(inwardRight ? 0 : 4, 10, inwardRight ? 4 : 0, 0);
    }

    private void UpdateKeyboardFocus()
        => KeyboardFocusIndicator.Opacity = LaunchButton.FocusState == FocusState.Keyboard ? 1 : 0;

    private void BrowserIcon_LayoutUpdated(object? sender, object args)
    {
        if (BrowserIcon.XamlRoot is not { } root) return;
        var position = BrowserIcon.TransformToVisual(root.Content).TransformPoint(default);
        double scale = root.RasterizationScale;
        double x = position.X - artworkOffset.X;
        double y = position.Y - artworkOffset.Y;
        double dx = Math.Round(x * scale) / scale - x;
        double dy = Math.Round(y * scale) / scale - y;
        if (Math.Abs((dx - artworkOffset.X) * scale) > 0.001) artworkOffset.X = dx;
        if (Math.Abs((dy - artworkOffset.Y) * scale) > 0.001) artworkOffset.Y = dy;
    }

    private void UpdateLabel()
    {
        string text = BrowserItem?.ShortName ?? string.Empty;
        nameMeasure.FontFamily = PetalName.FontFamily;
        nameMeasure.FontSize = PetalName.FontSize;
        nameMeasure.FontWeight = PetalName.FontWeight;
        nameMeasure.IsTextScaleFactorEnabled = PetalName.IsTextScaleFactorEnabled;
        PetalName.Text = MiddleEllipsis.Fit(text, PetalName.MaxWidth, Measure);
    }

    private double Measure(string text)
    {
        nameMeasure.Text = text;
        nameMeasure.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return nameMeasure.DesiredSize.Width;
    }
}
