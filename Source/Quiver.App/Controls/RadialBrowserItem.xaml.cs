using Quiver.App.ViewModels;
using Quiver.Library.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Linq;

namespace Quiver.App.Controls;

public sealed partial class RadialBrowserItem : UserControl
{
    public RadialBrowserItem()
    {
        InitializeComponent();
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

    public Visibility ProfileBadgeVisibility => BrowserItem?.Profile is null ? Visibility.Collapsed : Visibility.Visible;

    public string? ToolTipText => BrowserItem?.AlternateLaunches is { Count: > 0 } launches
        ? "Right-click for " + string.Join(", ", launches.Select(launch => launch.ItemName))
        : null;

    public event EventHandler<BrowserItemViewModel>? LaunchRequested;

    /// <summary>Raised when the pointer enters the item or it receives focus.</summary>
    public event EventHandler? SelectRequested;

    public event EventHandler<AlternateLaunchRequestedEventArgs>? AlternateLaunchRequested;

    public void FocusItem(FocusState focusState) => LaunchButton.Focus(focusState);
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

    private void LaunchButton_PointerEntered(object sender, PointerRoutedEventArgs e) => SelectRequested?.Invoke(this, EventArgs.Empty);

    private void LaunchButton_GotFocus(object sender, RoutedEventArgs e) => SelectRequested?.Invoke(this, EventArgs.Empty);
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
}

public sealed class AlternateLaunchRequestedEventArgs(Browser browser, AlternateLaunch alternateLaunch) : EventArgs
{
    public Browser Browser { get; } = browser;

    public AlternateLaunch AlternateLaunch { get; } = alternateLaunch;
}
