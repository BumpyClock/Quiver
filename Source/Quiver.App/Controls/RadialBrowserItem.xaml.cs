using Quiver.App.ViewModels;
using Quiver.Library.Models;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using System;
using System.Linq;
using System.Numerics;
using Windows.UI.ViewManagement;

namespace Quiver.App.Controls;

public sealed partial class RadialBrowserItem : UserControl
{
    private const float HighlightScale = 1.15f;
    private static readonly TimeSpan ScaleAnimationDuration = TimeSpan.FromMilliseconds(140);
    private static readonly UISettings AnimationSettings = new();

    private bool isPointerOver;
    private bool isFocused;
    private bool isHighlighted;
    private Vector3KeyFrameAnimation? scaleAnimation;

    public RadialBrowserItem()
    {
        InitializeComponent();
        BrowserIcon.SizeChanged += (_, _) => ElementCompositionPreview.GetElementVisual(BrowserIcon).CenterPoint =
            new Vector3((float)BrowserIcon.ActualWidth / 2, (float)BrowserIcon.ActualHeight / 2, 0);
    }

    #region Public API
    public BrowserItemViewModel? BrowserItem
    {
        get => (BrowserItemViewModel?)GetValue(BrowserItemProperty);
        set => SetValue(BrowserItemProperty, value);
    }

    public static readonly DependencyProperty BrowserItemProperty =
        DependencyProperty.Register(
            nameof(BrowserItem),
            typeof(BrowserItemViewModel),
            typeof(RadialBrowserItem),
            new PropertyMetadata(null, OnBrowserItemChanged));

    public int ShortcutNumber
    {
        get => (int)GetValue(ShortcutNumberProperty);
        set => SetValue(ShortcutNumberProperty, value);
    }

    public static readonly DependencyProperty ShortcutNumberProperty =
        DependencyProperty.Register(
            nameof(ShortcutNumber),
            typeof(int),
            typeof(RadialBrowserItem),
            new PropertyMetadata(0, OnBrowserItemChanged));

    public Visibility ShortcutVisibility => ShortcutNumber is >= 1 and <= 9 ? Visibility.Visible : Visibility.Collapsed;

    public string ToolTipText
    {
        get
        {
            string name = BrowserItem?.Name ?? string.Empty;
            string shortcut = ShortcutNumber is >= 1 and <= 9 ? $" ({ShortcutNumber})" : string.Empty;
            string more = BrowserItem?.AlternateLaunches is { Count: > 0 } launches
                ? "\nRight-click for " + string.Join(", ", launches.Select(launch => launch.ItemName))
                : string.Empty;
            return name + shortcut + more;
        }
    }

    public event EventHandler<BrowserItemViewModel>? LaunchRequested;

    public event EventHandler<AlternateLaunchRequestedEventArgs>? AlternateLaunchRequested;

    public void FocusItem() => LaunchButton.Focus(FocusState.Keyboard);
    #endregion

    #region Event handlers
    private static void OnBrowserItemChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is RadialBrowserItem item)
        {
            item.Bindings.Update();
            item.LaunchButton.ContextFlyout = item.BrowserItem is null ? null : item.CreateAlternateLaunchFlyout(item.BrowserItem.Model);
        }
    }

    private void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        if (BrowserItem is not null)
        {
            LaunchRequested?.Invoke(this, BrowserItem);
        }
    }

    private void LaunchButton_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        isPointerOver = true;
        UpdateHighlight();
    }

    private void LaunchButton_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        isPointerOver = false;
        UpdateHighlight();
    }

    private void LaunchButton_GotFocus(object sender, RoutedEventArgs e)
    {
        isFocused = LaunchButton.FocusState == FocusState.Keyboard;
        UpdateHighlight();
    }

    private void LaunchButton_LostFocus(object sender, RoutedEventArgs e)
    {
        isFocused = false;
        UpdateHighlight();
    }
    #endregion

    #region Alternate launches
    private MenuFlyout? CreateAlternateLaunchFlyout(Browser browser)
    {
        if (browser.AlternateLaunches is not { Count: > 0 } alternateLaunches)
        {
            return null;
        }

        MenuFlyout flyout = new() { ShouldConstrainToRootBounds = false };
        foreach (var alternateLaunch in alternateLaunches)
        {
            MenuFlyoutItem menuItem = new() { Text = alternateLaunch.ItemName };
            menuItem.Click += (_, _) => AlternateLaunchRequested?.Invoke(
                this,
                new AlternateLaunchRequestedEventArgs(browser, alternateLaunch));
            flyout.Items.Add(menuItem);
        }

        return flyout;
    }
    #endregion

    #region Highlight
    private void UpdateHighlight()
    {
        bool highlighted = isPointerOver || isFocused;
        if (highlighted == isHighlighted)
        {
            return;
        }

        isHighlighted = highlighted;
        AnimateScale(highlighted ? HighlightScale : 1f);
    }

    private void AnimateScale(float scale)
    {
        var visual = ElementCompositionPreview.GetElementVisual(BrowserIcon);
        var target = new Vector3(scale, scale, 1);
        if (!AnimationSettings.AnimationsEnabled)
        {
            visual.StopAnimation("Scale");
            visual.Scale = target;
            return;
        }

        scaleAnimation ??= visual.Compositor.CreateVector3KeyFrameAnimation();
        scaleAnimation.InsertKeyFrame(1f, target, visual.Compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.16f, 1f),
            new Vector2(0.3f, 1f)));
        scaleAnimation.Duration = ScaleAnimationDuration;
        visual.StartAnimation("Scale", scaleAnimation);
    }
    #endregion
}

public sealed class AlternateLaunchRequestedEventArgs(Browser browser, AlternateLaunch alternateLaunch) : EventArgs
{
    public Browser Browser { get; } = browser;

    public AlternateLaunch AlternateLaunch { get; } = alternateLaunch;
}
