using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Quiver.App.Helpers;
using Quiver.App.Windows;
using Quiver.Library.Models;

namespace Quiver.SelectorSmoke;

internal static class SelectorAddressChecks
{
    private static readonly (string Url, string Host, string Detail, bool Secure)[] Cases =
        [
            ("http://localhost:3000/login", "localhost:3000", "/login", false),
            ("http://localhost:5000/login", "localhost:5000", "/login", false),
            ("http://www.example.com:80/login", "example.com", "/login", false),
            ("https://www.example.com:8443/login?a=1#section", "example.com:8443", "/login?a=1#section", true),
            ("https://www.example.com:443/login", "example.com", "/login", true),
            ("http://[::1]:3000/login", "[::1]:3000", "/login", false),
            ("https://[2001:db8::1]:8443/?q=1#part", "[2001:db8::1]:8443", "/?q=1#part", true),
            ("https://[::1]:443/", "[::1]", "/", true),
            ("http://[::1]:80/", "[::1]", "/", false),
            ("ftp://www.example.com:2121/file", "example.com", "ftp://www.example.com:2121/file", false),
            ("file:///C:/folder/report.pdf", "report.pdf", "file:///C:/folder/report.pdf", false),
            ("mailto:someone@example.com", "example.com", "mailto:someone@example.com", false),
            ("not a URL", "not a URL", "not a URL", false),
            ("", "", "", false),
        ];

    public static void Run()
    {
        foreach (var (url, host, detail, secure) in Cases)
        {
            var address = SelectorAddress.For(url, true);
            Check(address == new SelectorAddress(host, detail, secure), $"List caption for {url}");
            var radial = SelectorAddress.For(url, false);
            Check(radial.Detail == url && radial.IsSecure == secure, $"Radial keeps the complete address for {url}");
        }
        Check(SelectorAddress.For(null, true) == new SelectorAddress("", "", false), "empty destination");
        Check(SelectorAddress.For("http://localhost:3000/login", false).Title == "localhost",
            "Radial retains its existing host caption with a complete URL below");
        Console.WriteLine("PASS: shortened List addresses retain non-default HTTP(S) ports, bracket IPv6, and preserve other destinations.");
    }

    public static async Task RunAsync(SelectorWindow window, TestSettings settings)
    {
        var preference = settings.LoadSettings().AppSettings;
        string originalUrl = window.ViewModel.Url;
        var root = (FrameworkElement)window.Content;
        var title = (TextBlock)root.FindName("PillTitle");
        var detail = (TextBlock)root.FindName("PillHost");
        var padlock = (FrameworkElement)root.FindName("PillLock");
        var card = (Button)root.FindName("NamePill");
        try
        {
            window.MinimizeWindow();
            settings.UpdateAppSettings(new AppSettings { SelectorLayout = SelectorLayout.List });
            window.ShowWindow(CursorPosition.Capture());
            foreach (var (url, host, path, secure) in Cases)
            {
                window.ViewModel.Url = url;
                await SmokeApplication.WaitForAsync(() => title.Text == (host.Length == 0 ? "Choose a browser" : host)
                    && detail.Text == path && (padlock.Visibility == Visibility.Visible) == secure
                    && AutomationProperties.GetHelpText(card) == url && (ToolTipService.GetToolTip(card) as string) == url,
                    $"native List caption, complete tooltip and help text match {url}");
            }
            Console.WriteLine("PASS: native List address captions retain ports and complete tooltip/help text without launching destinations.");
        }
        finally
        {
            window.MinimizeWindow();
            window.ViewModel.Url = originalUrl;
            settings.UpdateAppSettings(preference);
            await Task.Delay(50);
        }
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception($"Selector address check failed: {name}");
    }
}
