using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Markup;
using Quiver.App.Services.Interfaces;
using Quiver.App.ViewModels;
using Quiver.App.Windows;
using System.Diagnostics;

namespace Quiver.SelectorSmoke;

internal static class LifecycleChecks
{
    public const string MissingXamlVariable = "QUIVER_SELECTOR_MISSING_XAML_CHILD";
    private const string ChildResult = "missing-xaml-result.txt";

    public static void CheckFailedInitializationShutdown()
    {
        string source = AppContext.BaseDirectory;
        string copy = Path.Combine(Path.GetTempPath(), "quiver-selector-missing-xaml-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(source, file);
                if (relative.Equals(Path.Combine("Windows", "SelectorWindow.xbf"), StringComparison.OrdinalIgnoreCase)
                    || relative.StartsWith("list-polish-icon-cache", StringComparison.OrdinalIgnoreCase))
                    continue;
                string target = Path.Combine(copy, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }

            var start = new ProcessStartInfo(Path.Combine(copy, Path.GetFileName(Environment.ProcessPath!)))
            {
                UseShellExecute = false,
                WorkingDirectory = copy,
            };
            start.Environment[MissingXamlVariable] = "1";
            using var child = Process.Start(start) ?? throw new Exception("Missing-XAML child did not start.");
            if (!child.WaitForExit(TimeSpan.FromSeconds(60)))
            {
                child.Kill();
                throw new Exception("Selector lifecycle check failed: missing-XAML child did not exit within 60 seconds");
            }
            string resultPath = Path.Combine(copy, ChildResult);
            string result = File.Exists(resultPath) ? File.ReadAllText(resultPath) : "<no result>";
            Check(child.ExitCode == 0 && result.StartsWith("PASS", StringComparison.Ordinal),
                $"failed SelectorWindow initialization shuts down without a secondary Closed exception "
                + $"(exit 0x{child.ExitCode:X8}, result {result})");
        }
        finally
        {
            try { Directory.Delete(copy, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        Console.WriteLine("PASS: failed selector XAML initialization leaves no callbacks for shutdown.");
    }

    public static void RunMissingXamlChild(Application app)
    {
        string resultPath = Path.Combine(AppContext.BaseDirectory, ChildResult);
        app.UnhandledException += (_, e) =>
        {
            Environment.ExitCode = 2;
            File.WriteAllText(resultPath, "FAIL: shutdown raised " + e.Exception);
        };
        var services = new ServiceCollection()
            .AddSingleton<ISettingsService, TestSettings>()
            .AddSingleton<IQuickViewService, TestQuickView>()
            .AddSingleton<IIconLoader, TestIcons>()
            .AddTransient<SelectorPageViewModel>()
            .BuildServiceProvider();
        typeof(Quiver.App.App).GetProperty(nameof(Quiver.App.App.Services))!.SetValue(null, services);
        try
        {
            _ = new SelectorWindow();
            Environment.ExitCode = 1;
            File.WriteAllText(resultPath, "FAIL: SelectorWindow constructed without its XAML resource");
        }
        catch (XamlParseException)
        {
            File.WriteAllText(resultPath, "PASS: missing SelectorWindow XAML failed construction");
        }
        SmokeApplication.EnterShutdown();
        app.Exit();
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception($"Selector lifecycle check failed: {name}");
    }
}
