using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Quiver.App.ViewModels;
using Quiver.Library.Models;
using System;
using System.Linq;

namespace Quiver.App.Controls;

public abstract class BrowserItemControl : UserControl
{
    protected abstract Button LaunchTarget { get; }
    protected abstract void UpdateItemBindings();

    protected BrowserItemControl()
    {
        Loaded += (_, _) =>
        {
            RefreshSelectionState();
            DispatcherQueue.TryEnqueue(RefreshSelectionState);
        };
        ActualThemeChanged += (_, _) => DispatcherQueue.TryEnqueue(RefreshSelectionState);
    }

    private void RefreshSelectionState()
    {
        VisualStateManager.GoToState(this, "Unselected", false);
        if (IsSelected) VisualStateManager.GoToState(this, "Selected", false);
    }

    public BrowserItemViewModel? BrowserItem
    {
        get => (BrowserItemViewModel?)GetValue(BrowserItemProperty);
        set => SetValue(BrowserItemProperty, value);
    }

    public static readonly DependencyProperty BrowserItemProperty = DependencyProperty.Register(
        nameof(BrowserItem), typeof(BrowserItemViewModel), typeof(BrowserItemControl),
        new PropertyMetadata(null, OnBrowserItemChanged));

    public int ShortcutNumber
    {
        get => (int)GetValue(ShortcutNumberProperty);
        set => SetValue(ShortcutNumberProperty, value);
    }

    public static readonly DependencyProperty ShortcutNumberProperty = DependencyProperty.Register(
        nameof(ShortcutNumber), typeof(int), typeof(BrowserItemControl),
        new PropertyMetadata(0, OnBrowserItemChanged));

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.Register(
        nameof(IsSelected), typeof(bool), typeof(BrowserItemControl),
        new PropertyMetadata(false, OnIsSelectedChanged));

    public Visibility ShortcutVisibility => ShortcutNumber is >= 1 and <= 9 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ProfileBadgeVisibility => BrowserItem?.Profile is null ? Visibility.Collapsed : Visibility.Visible;
    public string? ToolTipText => BrowserItem?.AlternateLaunches is { Count: > 0 } launches
        ? BrowserItem.Name + "\nRight-click for " + string.Join(", ", launches.Select(launch => launch.ItemName))
        : BrowserItem?.Name;

    public event EventHandler<BrowserItemViewModel>? LaunchRequested;
    public event EventHandler<AlternateLaunchRequestedEventArgs>? AlternateLaunchRequested;

    public void FocusItem(FocusState focusState) => LaunchTarget.Focus(focusState);

    private static void OnBrowserItemChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var item = (BrowserItemControl)sender;
        item.UpdateItemBindings();
        item.LaunchTarget.ContextFlyout = item.CreateAlternateLaunchFlyout();
    }

    private static void OnIsSelectedChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var item = (BrowserItemControl)sender;
        VisualStateManager.GoToState(item, item.IsSelected ? "Selected" : "Unselected", false);
    }

    protected void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        if (BrowserItem is not null) LaunchRequested?.Invoke(this, BrowserItem);
    }

    private MenuFlyout? CreateAlternateLaunchFlyout()
    {
        if (BrowserItem?.AlternateLaunches is not { Count: > 0 } launches) return null;

        var browser = BrowserItem.Model;
        MenuFlyout flyout = new() { ShouldConstrainToRootBounds = false };
        foreach (var launch in launches)
        {
            MenuFlyoutItem item = new() { Text = launch.ItemName };
            item.Click += (_, _) => AlternateLaunchRequested?.Invoke(this,
                new AlternateLaunchRequestedEventArgs(browser, launch));
            flyout.Items.Add(item);
        }
        return flyout;
    }
}

public sealed class AlternateLaunchRequestedEventArgs(Browser browser, AlternateLaunch alternateLaunch) : EventArgs
{
    public Browser Browser { get; } = browser;
    public AlternateLaunch AlternateLaunch { get; } = alternateLaunch;
}
