using Microsoft.UI.Xaml.Controls;
using Quiver.App.Helpers;
using System;
using Windows.Foundation;

namespace Quiver.App.Controls;

public sealed partial class ListBrowserItem : BrowserItemControl
{
    private readonly TextBlock profileMeasure = new();

    public ListBrowserItem()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateProfileLabel();
        IdentityText.SizeChanged += (_, _) => UpdateProfileLabel();
    }

    protected override Button LaunchTarget => LaunchButton;

    protected override void UpdateItemBindings()
    {
        Bindings.Update();
        UpdateProfileLabel();
    }

    private void UpdateProfileLabel()
    {
        profileMeasure.FontFamily = ProfileName.FontFamily;
        profileMeasure.FontSize = ProfileName.FontSize;
        profileMeasure.FontWeight = ProfileName.FontWeight;
        profileMeasure.IsTextScaleFactorEnabled = ProfileName.IsTextScaleFactorEnabled;
        double available = Math.Max(0, IdentityText.ActualWidth - ProfileBadge.Width - ProfileLine.ColumnSpacing);
        ProfileName.Text = MiddleEllipsis.Fit(BrowserItem?.Profile?.Name ?? string.Empty, available, Measure);
    }

    private double Measure(string text)
    {
        profileMeasure.Text = text;
        profileMeasure.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return profileMeasure.DesiredSize.Width;
    }
}
