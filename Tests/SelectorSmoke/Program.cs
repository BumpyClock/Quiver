using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Quiver.App.Controls;
using Quiver.App.Services.Interfaces;
using Quiver.App.ViewModels;
using Quiver.App.Windows;
using Quiver.Library;
using Quiver.Library.Models;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace Quiver.SelectorSmoke;

internal static partial class Program
{
    [LibraryImport("Microsoft.ui.xaml.dll")]
    private static partial void XamlCheckProcessRequirements();

    [STAThread]
    private static void Main()
    {
        XamlCheckProcessRequirements();
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            new SmokeApplication();
        });
    }
}

public sealed partial class SmokeApplication : Application
{
    public SmokeApplication() => InitializeComponent();

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        SelectorWindow? window = null;
        SelectorWindow? coldListWindow = null;
        ServiceProvider? services = null;
        string resultPath = Path.Combine(AppContext.BaseDirectory, "selector-smoke-result.txt");
        try
        {
            CliArgsChecks.Run();
            CheckMetrics();
            SelectorGeometryChecks.Run();
            services = new ServiceCollection()
                .AddSingleton<ISettingsService, TestSettings>()
                .AddSingleton<IQuickViewService, TestQuickView>()
                .AddSingleton<IIconLoader, TestIcons>()
                .AddTransient<SelectorPageViewModel>()
                .BuildServiceProvider();
            typeof(Quiver.App.App).GetProperty(nameof(Quiver.App.App.Services))!.SetValue(null, services);
            window = new SelectorWindow();
            window.ViewModel.Url = "https://a-long-sharepoint-hostname-for-the-selector.example.com/document";
            var root = (FrameworkElement)window.Content;
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            root.Loaded += (_, _) => loaded.TrySetResult();
            window.AppWindow.IsShownInSwitchers = false;
            var metrics = RadialMetrics.For(window.ViewModel.Browsers.Count);
            window.AppWindow.MoveAndResize(new RectInt32(-32000, -32000,
                (int)metrics.WindowWidth, (int)metrics.WindowHeight));
            window.AppWindow.Show(activateWindow: false);
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var ring = (ItemsRepeater)root.FindName("BrowserRing");
            // Window x:Bind initializes on activation; keep this test offscreen and inactive.
            ring.ItemsSource = window.ViewModel.Browsers;
            var first = (RadialBrowserItem)ring.GetOrCreateElement(0);
            var second = (RadialBrowserItem)ring.GetOrCreateElement(1);
            root.UpdateLayout();
            CheckSelectionAlignment();
            foreach (var item in new[] { first, second })
            {
                var tile = (FrameworkElement)item.FindName("IconTile");
                var badge = (Border)item.FindName("ShortcutBadge");
                var offset = badge.TransformToVisual(tile).TransformPoint(default);
                Check(Math.Abs(offset.X + badge.ActualWidth / 2 - tile.ActualWidth / 2) <= 1 / root.XamlRoot.RasterizationScale
                    && offset.Y >= 0 && offset.Y + badge.ActualHeight <= tile.ActualHeight,
                    $"shortcut keycaps are centered inside the icon tiles (offset {offset.X},{offset.Y}; "
                    + $"badge {badge.ActualWidth}x{badge.ActualHeight}; tile {tile.ActualWidth}x{tile.ActualHeight})");
                var shortcut = (TextBlock)item.FindName("ShortcutText");
                Check(shortcut.Text == (ring.GetElementIndex(item) + 1).ToString(),
                    "integrated hints keep the browser's assigned number shortcut");
            }
            first.ShortcutNumber = 10;
            Check(((Border)first.FindName("ShortcutBadge")).Visibility == Visibility.Collapsed,
                "browsers without a number-key shortcut do not show a keycap");
            first.ShortcutNumber = 1;
            foreach (var theme in new[] { ElementTheme.Dark, ElementTheme.Light, ElementTheme.Dark })
            {
                root.RequestedTheme = theme;
                root.UpdateLayout();
                await Task.Delay(50);
                var outline = (Microsoft.UI.Xaml.Shapes.Path)root.FindName("SelectionOutline");
                var hub = (FontIcon)((Button)root.FindName("HubButton")).Content;
                bool dark = theme == ElementTheme.Dark;
                Check(IsDark(outline.Fill) == dark && IsDark(outline.Stroke) != dark
                    && IsDark(hub.Foreground) != dark,
                    "selection surface, outline, and hub follow live theme changes");
                foreach (var item in new[] { first, second })
                {
                    Check(IsDark(((Border)item.FindName("IconSurface")).Background) == dark
                        && IsDark(((Border)item.FindName("ShortcutBadge")).Background) == dark
                        && IsDark(((TextBlock)item.FindName("ShortcutText")).Foreground) != dark
                        && IsDark(((TextBlock)item.FindName("PetalName")).Foreground) != dark,
                        "selected and unselected icon tiles, hints, and labels follow live theme changes");
                }
            }
            var highContrast = (ResourceDictionary)((FrameworkElement)root.FindName("FlowerHost"))
                .Resources.ThemeDictionaries["HighContrast"];
            var highlightColor = ((SolidColorBrush)highContrast["SelectionFill"]).Color;
            foreach (string key in new[] { "HubBackground", "HubBackgroundPointerOver", "HubBackgroundPressed" })
            {
                Check(((SolidColorBrush)highContrast[key]).Color == highlightColor,
                    "all high-contrast hub states pair highlight backgrounds with highlight text");
            }
            root.RequestedTheme = ElementTheme.Default;
            var pill = (Button)root.FindName("NamePill");
            var title = (TextBlock)root.FindName("PillTitle");
            var host = (TextBlock)root.FindName("PillHost");
            const string linkTitle = "a-long-sharepoint-hostname-for-the-selector.example.com";
            Check(title.Text == linkTitle && host.Text == window.ViewModel.Url,
                "link card shows the destination host and complete URL instead of the browser name");
            window.ViewModel.Url = "https://www.example.com/path?q=1#section";
            Check(title.Text == "example.com" && host.Text == window.ViewModel.Url,
                "edited links update their host, path, query, and fragment");
            window.ViewModel.Url = string.Empty;
            Check(title.Text == "Choose a browser"
                && ((FrameworkElement)root.FindName("PillLink")).Visibility == Visibility.Collapsed,
                "opening without a URL shows a useful prompt instead of stale link details");
            window.ViewModel.Url = "https://" + linkTitle + "/document";
            int focusChanges = 0;
            root.GettingFocus += (_, _) => focusChanges++;
            first.FocusItem(FocusState.Keyboard);
            second.FocusItem(FocusState.Keyboard);
            await CheckSettledAsync(second);
            Check(host.ActualWidth > 0 && host.ActualWidth < pill.ActualWidth - 40,
                "long link hosts stay within the card and leave room for link options");

            second.FocusItem(FocusState.Programmatic);
            first.FocusItem(FocusState.Programmatic);
            await CheckSettledAsync(first);

            first.FocusItem(FocusState.Keyboard);
            second.FocusItem(FocusState.Keyboard);
            pill.Focus(FocusState.Programmatic);
            await Task.Delay(100);
            Check(ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), pill),
                "stale browser focus notifications do not steal focus from link options");
            Check(first.IsSelected && !second.IsSelected, "stale focus does not replace the selected browser");
            int before = focusChanges;
            await Task.Delay(100);
            Check(focusChanges == before, "focus stays on link options without new input");

            second.FocusItem(FocusState.Keyboard);
            await CheckSettledAsync(second);
            var testSettings = (TestSettings)services.GetRequiredService<ISettingsService>();
            await ListSelectorChecks.RunAsync(window, testSettings);
            testSettings.UpdateAppSettings(new AppSettings { SelectorLayout = SelectorLayout.List });
            coldListWindow = new SelectorWindow();
            await ListSelectorChecks.CheckColdOpeningAsync(coldListWindow);
            File.WriteAllText(resultPath, "PASS: radial focus and list selector checks.");
            Console.WriteLine(File.ReadAllText(resultPath));

            static bool IsDark(Brush brush)
            {
                var color = ((SolidColorBrush)brush).Color;
                return Math.Max(color.R, Math.Max(color.G, color.B)) < 128;
            }

            async Task CheckSettledAsync(RadialBrowserItem item)
            {
                await WaitForAsync(() => item.IsSelected && ReferenceEquals(
                    FocusManager.GetFocusedElement(root.XamlRoot), item.FindName("LaunchButton")),
                    "browser focus and selection converge");
                var focused = FocusManager.GetFocusedElement(root.XamlRoot) as DependencyObject;
                while (focused is not null && !ReferenceEquals(focused, item))
                {
                    focused = VisualTreeHelper.GetParent(focused);
                }
                Check(ReferenceEquals(focused, item), "latest browser focus wins");
                Check(item.IsSelected, "selected browser follows the latest focus");
                Check(title.Text == linkTitle && host.Text == window.ViewModel.Url,
                    "browser selection leaves destination details unchanged");
                CheckSelectionAlignment();
                int before = focusChanges;
                await Task.Delay(100);
                Check(focusChanges == before, "browser focus stays stable without new input");
                Check(item.IsSelected, "selected browser stays stable without new input");
            }

            void CheckSelectionAlignment()
            {
                var outline = (Microsoft.UI.Xaml.Shapes.Path)root.FindName("SelectionOutline");
                int selected = first.IsSelected ? 0 : 1;
                var item = selected == 0 ? first : second;
                var flower = (FrameworkElement)root.FindName("FlowerHost");
                var position = item.TransformToVisual(flower).TransformPoint(default);
                var center = new Windows.Foundation.Point(position.X + item.ActualWidth / 2,
                    position.Y + item.ActualHeight / 2);
                var geometry = (PathGeometry)outline.Data;
                var arc = (ArcSegment)geometry.Figures[0].Segments[0];
                Check(Math.Abs(arc.Size.Width + outline.StrokeThickness / 2 - item.ActualWidth / 2) < 0.01,
                    "selection stroke fits exactly within the browser circle");
                var start = geometry.Figures[0].StartPoint;
                var end = arc.Point;
                Check(Math.Abs(Math.Sqrt(Math.Pow(start.X - center.X, 2) + Math.Pow(start.Y - center.Y, 2))
                    - arc.Size.Width) < 0.01
                    && Math.Abs(Math.Sqrt(Math.Pow(end.X - center.X, 2) + Math.Pow(end.Y - center.Y, 2))
                    - arc.Size.Width) < 0.01,
                    "selection arc is centered on the arranged browser, not a separate focus rectangle");
                var button = (Button)item.FindName("LaunchButton");
                Check(!button.UseSystemFocusVisuals, "browser selection owns its aligned keyboard outline");
            }
        }
        catch (Exception error)
        {
            Environment.ExitCode = 1;
            File.WriteAllText(resultPath, error.ToString());
            Console.Error.WriteLine(error);
        }
        finally
        {
            var app = typeof(Quiver.App.App);
            var shuttingDown = app.GetNestedType("AppState", BindingFlags.NonPublic)!.GetNestedType("ShuttingDown")!;
            app.GetField("state", BindingFlags.Static | BindingFlags.NonPublic)!
                .SetValue(null, Activator.CreateInstance(shuttingDown));
            if (coldListWindow is not null)
            {
                coldListWindow.Content = null;
                coldListWindow.Close();
            }
            if (window is not null)
            {
                window.Content = null;
                window.Close();
            }
            services?.Dispose();
            Exit();
        }
    }

    internal static async Task WaitForAsync(Func<bool> condition, string name)
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && timeout.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(20);
        }
        Check(condition(), name);
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception($"Selector check failed: {name}");
    }

    private static void CheckMetrics()
    {
        var pair = RadialMetrics.For(2);
        Check(pair.FlowerHeight == RadialMetrics.ItemSize && pair.FlowerOrigin.Y == RadialMetrics.PetalRadius,
            "two browsers occupy one row without an empty square viewport");
        Check(pair.PillTop - pair.FlowerTop - pair.FlowerHeight == RadialMetrics.PillGap,
            "link options sit directly below the visible browser petals");
        Check(pair.RingRadius == 58 && RadialMetrics.For(4).RingRadius == 64
            && Math.Abs(RadialMetrics.For(6).RingRadius - 80) < 0.0001,
            "ink stamp ring radii match the 2, 4, and 6 browser concept");
        foreach (int count in new[] { 2, 3, 4, 6, 10 })
        {
            var metrics = RadialMetrics.For(count);
            Check(metrics.CenterFillRadius > 0 && metrics.CenterFillRadius < metrics.RingRadius,
                "the ink center disc joins the petals without covering them");
            Check(metrics.PillWidth >= 264, "link options keep the concept caption width");
        }
        foreach (int count in Enumerable.Range(0, 11))
        {
            var metrics = RadialMetrics.For(count);
            for (int index = 0; index < count; index++)
            {
                var center = metrics.ItemCenter(index, metrics.FlowerOrigin);
                Check(center.X - RadialMetrics.PetalRadius >= -0.0001
                    && center.Y - RadialMetrics.PetalRadius >= -0.0001
                    && center.X + RadialMetrics.PetalRadius <= metrics.FlowerSize + 0.0001
                    && center.Y + RadialMetrics.PetalRadius <= metrics.FlowerHeight + 0.0001,
                    "every browser petal fits inside its visible host");
                var figure = metrics.SelectionGeometry(index).Figures[0];
                var arc = (ArcSegment)figure.Segments[0];
                Check(Math.Abs(arc.Size.Width - 51) < 0.0001,
                    "selected petal includes the stroke inside its 52 DIP radius");
                if (count > 1)
                {
                    var hubArc = (ArcSegment)figure.Segments[2];
                    var hubStart = ((LineSegment)figure.Segments[1]).Point;
                    var direction = new Windows.Foundation.Point(
                        (center.X - metrics.FlowerOrigin.X) / metrics.RingRadius,
                        (center.Y - metrics.FlowerOrigin.Y) / metrics.RingRadius);
                    Check(arc.IsLargeArc && !hubArc.IsLargeArc,
                        "selection follows the outer browser arc and the inner hub arc");
                    Check((figure.StartPoint.X - center.X) * direction.X
                        + (figure.StartPoint.Y - center.Y) * direction.Y < 0,
                        "tangents join the inward side of each browser, leaving its outer edge circular");
                    Check(Math.Abs(Math.Sqrt(Math.Pow(hubStart.X - metrics.FlowerOrigin.X, 2)
                        + Math.Pow(hubStart.Y - metrics.FlowerOrigin.Y, 2)) - RadialMetrics.HubRadius) < 0.0001,
                        "all selections join the same central circle");
                    foreach (var (petalPoint, hubPoint) in new[]
                    {
                        (arc.Point, hubStart),
                        (figure.StartPoint, hubArc.Point)
                    })
                    {
                        double dx = hubPoint.X - petalPoint.X;
                        double dy = hubPoint.Y - petalPoint.Y;
                        double length = Math.Sqrt(dx * dx + dy * dy);
                        Check(length > 0, "selection connectors have nonzero length");
                        Check(Math.Abs(dx * (petalPoint.X - center.X)
                            + dy * (petalPoint.Y - center.Y)) / (length * arc.Size.Width) < 0.0001
                            && Math.Abs(dx * (hubPoint.X - metrics.FlowerOrigin.X)
                            + dy * (hubPoint.Y - metrics.FlowerOrigin.Y)) / (length * hubArc.Size.Width) < 0.0001,
                            "both selection connectors are tangent to the petal and hub circles");
                    }
                }
            }
        }
    }
}

internal sealed class TestSettings : ISettingsService
{
    private readonly Settings settings = new()
    {
        Browsers = new([new Browser("Browser 1", "selector-test-1.exe"),
            new Browser("Browser 2", "selector-test-2.exe")]),
    };
    public Settings LoadSettings() => settings;
    public PreparedRulesets PreparedRulesets => throw new NotSupportedException();
    public Task FlushAsync() => Task.CompletedTask;
    public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;
    public void UpdateAppSettings(AppSettings value)
    {
        settings.AppSettings = value;
        SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(SettingsSection.AppSettings));
    }
    public void UpdateQuickView(QuickViewSettings value) => throw new NotSupportedException();
    public void UpdateBrowsers(ObservableCollection<Browser> value) => throw new NotSupportedException();
    public void UpdateRulesets(ObservableCollection<Ruleset> value) => throw new NotSupportedException();
}

internal sealed class TestQuickView : IQuickViewService
{
    public bool IsQuickViewEnabled => false;
    public bool TryOpen(string? url) => false;
    public bool TryOpenIfModifierKeyActivated(string? url) => false;
}

internal sealed class TestIcons : IIconLoader
{
    public Task StopAsync() => Task.CompletedTask;
    public Task FlushCacheMaintenanceAsync() => Task.CompletedTask;
    public Task<BitmapImage?> LoadIconAsync(Browser browser, CancellationToken cancellationToken = default) => Task.FromResult<BitmapImage?>(null);
    public Task<BitmapImage?> LoadIconFromExe(string path, CancellationToken cancellationToken = default) => Task.FromResult<BitmapImage?>(null);
    public Task<BitmapImage?> LoadIconFromExe(string path, int index, CancellationToken cancellationToken = default) => Task.FromResult<BitmapImage?>(null);
    public Task<int> GetExeIconCountAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(0);
    public Task<BitmapImage?> LoadIconFromIco(string path, CancellationToken cancellationToken = default) => Task.FromResult<BitmapImage?>(null);
    public Task<BitmapImage?> LoadIconFromImage(string path, CancellationToken cancellationToken = default) => Task.FromResult<BitmapImage?>(null);
    public Task<BitmapImage?> LoadIconFromURL(string url, CancellationToken cancellationToken = default) => Task.FromResult<BitmapImage?>(null);
}
