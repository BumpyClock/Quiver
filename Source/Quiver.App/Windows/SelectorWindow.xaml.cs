using Quiver.App.Controls;
using Quiver.App.Helpers;
using Quiver.App.Services.Interfaces;
using Quiver.App.ViewModels;
using Quiver.Library;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Composition;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.System;
using Windows.UI.ViewManagement;
using WinRT;
using WinRT.Interop;
using WinUIEx;

namespace Quiver.App.Windows;

[GeneratedBindableCustomProperty]
public sealed partial class SelectorWindow : Window
{
    private static readonly TimeSpan OpenAnimationDuration = TimeSpan.FromMilliseconds(180);
    private static readonly UISettings AnimationSettings = new();

    public SelectorPageViewModel ViewModel { get; }
    private readonly IQuickViewService quickViewService;
    private readonly ISettingsService settingsService;
    private readonly IntPtr hwnd;

    private bool isHiddenToTray = true;
    private RadialBrowserItem? highlightedItem;
    private Vector3KeyFrameAnimation? openScaleAnimation;
    private ScalarKeyFrameAnimation? openOpacityAnimation;

    #region Window Lifecycle
    public SelectorWindow()
    {
        IServiceProvider services = App.Services ?? throw new InvalidOperationException("Application services are not configured.");
        ViewModel = services.GetRequiredService<SelectorPageViewModel>();
        quickViewService = services.GetRequiredService<IQuickViewService>();
        settingsService = services.GetRequiredService<ISettingsService>();
        ViewModel.BrowserLaunched += ViewModel_BrowserLaunched;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        hwnd = WindowNative.GetWindowHandle(this);

        // A borderless, always-on-top popup that stays out of the taskbar and Alt+Tab.
        var presenter = OverlappedPresenter.Create();
        presenter.IsAlwaysOnTop = true;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsResizable = false;
        presenter.SetBorderAndTitleBar(false, false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;

        Activated += Window_Activated;
        Closed += SelectorWindow_Closed;

        InitializeComponent();
        ViewModel.Browsers.CollectionChanged += ViewModel_Browsers_CollectionChanged;
        settingsService.SettingsChanged += SettingsChanged;
        UpdateHub();
    }

    public void Init(CliArgs args)
    {
        ViewModel.Url = args.Url;

        if (args.IsRunAsMin)
        {
            MinimizeWindow();
            return;
        }

        ShowWindow();
    }

    private void Window_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated || isHiddenToTray)
        {
            return;
        }

#if DEBUG
        // No minimize on debug when not in focus
#else
        if (ViewModel.AppSettings.MinimizeOnFocusLoss)
        {
            MinimizeWindow();
        }
#endif
    }

    private void SelectorWindow_Closed(object sender, WindowEventArgs args)
    {
        if (!App.IsExiting)
        {
            args.Handled = true;
            MinimizeWindow();
            return;
        }

        settingsService.SettingsChanged -= SettingsChanged;
        ViewModel.CancelIconLoading();
        ViewModel.BrowserLaunched -= ViewModel_BrowserLaunched;
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        ViewModel.Browsers.CollectionChanged -= ViewModel_Browsers_CollectionChanged;
        Activated -= Window_Activated;
    }
    #endregion

    #region Window Lifecycle Helper methods
    internal void MinimizeWindow()
    {
        isHiddenToTray = true;
        EditUrlFlyout.Hide();
        HubMenu.Hide();
        AppWindow.Hide();
    }

    public void ShowWindow()
    {
        isHiddenToTray = false;
        SetHighlightedItem(null, false);
        var bounds = CursorPosition.SquareCenteredOnCursor(RadialMetrics.For(ViewModel.Browsers.Count).WindowSize);
        AppWindow.MoveAndResize(bounds);
        AppWindow.Show();
        // Moving onto a monitor with another DPI can rescale the window, so place it again once it is shown there.
        AppWindow.MoveAndResize(bounds);
        ApplyCircularRegion();
        Activate();
        this.SetForegroundWindow();
        HubButton.Focus(FocusState.Programmatic);
        PlayOpenAnimation();
    }

    private void ResizeAroundCenter()
    {
        if (isHiddenToTray)
        {
            return;
        }

        var current = AppWindow.Position;
        var size = AppWindow.Size;
        double scale = DiscRoot.XamlRoot?.RasterizationScale ?? 1.0;
        int newSize = (int)Math.Round(RadialMetrics.For(ViewModel.Browsers.Count).WindowSize * scale);
        AppWindow.MoveAndResize(new RectInt32(
            current.X + (size.Width - newSize) / 2,
            current.Y + (size.Height - newSize) / 2,
            newSize,
            newSize));
        ApplyCircularRegion();
    }

    /// <summary>
    /// Clips the window to a circle. Clicks outside it reach the windows below, and the acrylic backdrop forms the disc.
    /// </summary>
    private void ApplyCircularRegion()
    {
        var size = AppWindow.Size;
        IntPtr region = CreateEllipticRgn(0, 0, size.Width + 1, size.Height + 1);
        if (region != IntPtr.Zero && SetWindowRgn(hwnd, region, true) == 0)
        {
            DeleteObject(region);
        }
    }

    private void PlayOpenAnimation()
    {
        if (!AnimationSettings.AnimationsEnabled)
        {
            return;
        }

        var ringVisual = ElementCompositionPreview.GetElementVisual(BrowserRing);
        EnsureOpenAnimations(ringVisual.Compositor);
        ringVisual.CenterPoint = new Vector3((float)BrowserRing.ActualSize.X / 2, (float)BrowserRing.ActualSize.Y / 2, 0);
        var scaleAnimation = openScaleAnimation!;
        var opacityAnimation = openOpacityAnimation!;
        ringVisual.StartAnimation("Scale", scaleAnimation);
        ringVisual.StartAnimation("Opacity", opacityAnimation);

        var hubVisual = ElementCompositionPreview.GetElementVisual(HubButton);
        hubVisual.CenterPoint = new Vector3((float)HubButton.ActualSize.X / 2, (float)HubButton.ActualSize.Y / 2, 0);
        hubVisual.StartAnimation("Scale", scaleAnimation);
        hubVisual.StartAnimation("Opacity", opacityAnimation);
    }

    private void EnsureOpenAnimations(Compositor compositor)
    {
        if (openScaleAnimation is not null && openOpacityAnimation is not null)
        {
            return;
        }

        var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));
        openScaleAnimation = compositor.CreateVector3KeyFrameAnimation();
        openScaleAnimation.InsertKeyFrame(0f, new Vector3(0.82f, 0.82f, 1));
        openScaleAnimation.InsertKeyFrame(1f, Vector3.One, easing);
        openScaleAnimation.Duration = OpenAnimationDuration;

        openOpacityAnimation = compositor.CreateScalarKeyFrameAnimation();
        openOpacityAnimation.InsertKeyFrame(0f, 0f);
        openOpacityAnimation.InsertKeyFrame(1f, 1f, easing);
        openOpacityAnimation.Duration = OpenAnimationDuration;
    }
    #endregion

    #region Hub
    private void UpdateHub()
    {
        string host = GetDisplayHost(ViewModel.Url);
        if (highlightedItem?.BrowserItem is { } browser)
        {
            HubTitle.Text = browser.Name;
            HubSubtitle.Text = host;
        }
        else
        {
            HubTitle.Text = string.IsNullOrEmpty(host) ? Constants.NAME : host;
            HubSubtitle.Text = "Pick a browser";
        }
    }

    private static string GetDisplayHost(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
        {
            return uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        }

        return url;
    }

    private void SetHighlightedItem(RadialBrowserItem? item, bool highlighted)
    {
        if (highlighted)
        {
            highlightedItem = item;
        }
        else if (item is null || ReferenceEquals(highlightedItem, item))
        {
            highlightedItem = null;
        }

        UpdateHub();
    }

    private void HubMenu_Opening(object sender, object e)
    {
        QuickViewMenuItem.IsEnabled = quickViewService.IsQuickViewEnabled;
    }
    #endregion

    #region Selector UI Event Handlers
    private void BrowserRing_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is not RadialBrowserItem item)
        {
            return;
        }

        item.ShortcutNumber = args.Index + 1;
        item.LaunchRequested -= BrowserItem_LaunchRequested;
        item.LaunchRequested += BrowserItem_LaunchRequested;
        item.AlternateLaunchRequested -= BrowserItem_AlternateLaunchRequested;
        item.AlternateLaunchRequested += BrowserItem_AlternateLaunchRequested;
        item.HighlightChanged -= BrowserItem_HighlightChanged;
        item.HighlightChanged += BrowserItem_HighlightChanged;
    }

    private void BrowserRing_ElementIndexChanged(ItemsRepeater sender, ItemsRepeaterElementIndexChangedEventArgs args)
    {
        if (args.Element is RadialBrowserItem item)
        {
            item.ShortcutNumber = args.NewIndex + 1;
        }
    }

    private void BrowserItem_LaunchRequested(object? sender, BrowserItemViewModel e) => ViewModel.LaunchBrowserCommand.Execute(e);

    private void BrowserItem_HighlightChanged(object? sender, bool highlighted) => SetHighlightedItem(sender as RadialBrowserItem, highlighted);

    private void BrowserItem_AlternateLaunchRequested(object? sender, AlternateLaunchRequestedEventArgs e)
    {
        try
        {
            UriLauncher.Alternative(ViewModel.Url, e.Browser, e.AlternateLaunch);
            MinimizeWindow();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    private void ViewModel_BrowserLaunched(object? sender, EventArgs e) => MinimizeWindow();

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectorPageViewModel.Url))
        {
            UpdateHub();
        }
    }

    private void ViewModel_Browsers_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        highlightedItem = null;
        UpdateHub();
        ResizeAroundCenter();
    }

    private void SettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.Section.HasFlag(SettingsSection.AppSettings))
        {
            ViewModel.RefreshAppSettings();
        }

        if (e.Section.HasFlag(SettingsSection.Browsers))
        {
            ViewModel.RefreshBrowsers();
        }
    }

    private void LinkCopyBtnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            CopyCurrentUrlToClipboard();
        }
        catch (Exception err)
        {
            Debug.WriteLine(err);
        }
    }

    private void EditUrlBtnClick(object sender, RoutedEventArgs e) => ShowEditUrlFlyout();

    private void SettingsBtnClick(object sender, RoutedEventArgs e) => App.ShowSettings("settings");

    private void RulesBtnClick(object sender, RoutedEventArgs e) => App.ShowSettings("rulesets");

    private void QuickViewBtnClick(object sender, RoutedEventArgs e)
    {
        if (quickViewService.TryOpen(ViewModel.Url))
        {
            MinimizeWindow();
        }
    }

    private void UrlTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            EditUrlFlyout.Hide();
            e.Handled = true;
        }
    }

    private void EditUrlFlyout_Closed(object sender, object e)
    {
        if (!isHiddenToTray)
        {
            HubButton.Focus(FocusState.Programmatic);
        }
    }

    private void ShowEditUrlFlyout()
    {
        FlyoutBase.ShowAttachedFlyout(DiscRoot);
        UrlTextBox.Focus(FocusState.Keyboard);
        UrlTextBox.SelectAll();
    }
    #endregion

    #region Keyboard
    private void DiscRoot_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (IsTextBoxKeyAccelerator() || IsModifierDown())
        {
            return;
        }

        int count = ViewModel.Browsers.Count;
        int? number = e.Key switch
        {
            >= VirtualKey.Number1 and <= VirtualKey.Number9 => e.Key - VirtualKey.Number1,
            >= VirtualKey.NumberPad1 and <= VirtualKey.NumberPad9 => e.Key - VirtualKey.NumberPad1,
            _ => null
        };
        if (number is int index)
        {
            if (index < count)
            {
                ViewModel.LaunchBrowserCommand.Execute(ViewModel.Browsers[index]);
            }

            e.Handled = true;
            return;
        }

        int step = e.Key switch
        {
            VirtualKey.Right or VirtualKey.Down => 1,
            VirtualKey.Left or VirtualKey.Up => -1,
            _ => 0
        };
        if (step == 0 || count == 0)
        {
            return;
        }

        int focused = GetFocusedItemIndex();
        int next = focused < 0
            ? GetItemClosestToDirection(e.Key, count)
            : ((focused + step) % count + count) % count;
        (BrowserRing.TryGetElement(next) as RadialBrowserItem)?.FocusItem();
        e.Handled = true;
    }

    private int GetFocusedItemIndex()
    {
        var focused = DiscRoot.XamlRoot is null ? null : FocusManager.GetFocusedElement(DiscRoot.XamlRoot) as DependencyObject;
        while (focused is not null and not RadialBrowserItem)
        {
            focused = VisualTreeHelper.GetParent(focused);
        }

        return focused is RadialBrowserItem item ? BrowserRing.GetElementIndex(item) : -1;
    }

    private static int GetItemClosestToDirection(VirtualKey key, int count)
    {
        var target = key switch
        {
            VirtualKey.Up => new global::Windows.Foundation.Point(0, -1),
            VirtualKey.Down => new global::Windows.Foundation.Point(0, 1),
            VirtualKey.Left => new global::Windows.Foundation.Point(-1, 0),
            _ => new global::Windows.Foundation.Point(1, 0)
        };
        var metrics = RadialMetrics.For(count);
        int best = 0;
        double bestDot = double.MinValue;
        for (int i = 0; i < count; i++)
        {
            var center = metrics.ItemCenter(i, default);
            double dot = (center.X * target.X + center.Y * target.Y) / metrics.RingRadius;
            if (dot > bestDot)
            {
                bestDot = dot;
                best = i;
            }
        }

        return best;
    }

    private void EscapeAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        MinimizeWindow();
        args.Handled = true;
    }

    private void CopyAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsTextBoxKeyAccelerator())
        {
            return;
        }

        CopyCurrentUrlToClipboard();
        args.Handled = true;
    }

    private void EditUrlAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsTextBoxKeyAccelerator())
        {
            return;
        }

        ShowEditUrlFlyout();
        args.Handled = true;
    }

    private void RulesAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsTextBoxKeyAccelerator())
        {
            return;
        }

        App.ShowSettings("rulesets");
        args.Handled = true;
    }

    private bool IsTextBoxKeyAccelerator()
    {
        var xamlRoot = DiscRoot.XamlRoot;
        return xamlRoot is not null && FocusManager.GetFocusedElement(xamlRoot) is TextBox;
    }

    private static bool IsModifierDown() => KeyboardState.IsCtrlKeyDown() || KeyboardState.IsAltKeyDown();
    #endregion

    #region Helper methods
    private void CopyCurrentUrlToClipboard()
    {
        DataPackage package = new();
        package.SetText(ViewModel.Url ?? string.Empty);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateEllipticRgn(int left, int top, int right, int bottom);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr hObject);

    [LibraryImport("user32.dll")]
    private static partial int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, [MarshalAs(UnmanagedType.Bool)] bool bRedraw);
    #endregion
}
