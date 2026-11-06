using Quiver.App.Helpers;
using static Quiver.App.Helpers.CliArgs;

namespace Quiver.SelectorSmoke;

internal static class CliArgsChecks
{
    public static void Run()
    {
        string exe = Environment.ProcessPath!;
        (string CommandLine, CliArgs Expected)[] cases =
        [
            ("", new Empty()),
            ("--settings", new OpenSettings(SettingsTab.Browsers)),
            ("--settings --page rulesets", new OpenSettings(SettingsTab.Rulesets)),
            ("--page about", new OpenSettings(SettingsTab.About)),
            ("--page QuickView", new OpenSettings(SettingsTab.QuickView)),
            ("--page SETTINGS", new OpenSettings(SettingsTab.Settings)),
            ("--page unknown", new OpenSettings(SettingsTab.Browsers)),
            ("--page 2", new OpenSettings(SettingsTab.Browsers)),
            ("--page \" \"", new OpenSettings(SettingsTab.Browsers)),
            ("--settings --minimized https://a.example", new OpenSettings(SettingsTab.Browsers)),
            ("--page quickview --uri https://a.example", new OpenSettings(SettingsTab.QuickView)),
            ("--minimized", new Minimized()),
            ("--minimized --uri https://a.example", new Minimized()),
            ("https://a.example --minimized", new Minimized()),
            ("https://a.example https://b.example", new OpenUrl("https://a.example")),
            ("--uri https://a.example https://b.example", new OpenUrl("https://a.example")),
            ("--uri \" \" https://b.example", new OpenUrl("https://b.example")),
            ("QUIVER://https://a.example/x", new OpenUrl("https://a.example/x")),
            ("--uri quiver://https://a.example", new OpenUrl("https://a.example")),
            ("\"C:\\My Files\\a.html\"", new OpenUrl("\"C:\\My Files\\a.html\"")),
            ($"\"{exe.ToUpperInvariant()}\" https://a.example", new OpenUrl("https://a.example")),
            ($"\"{exe}\"", new Empty()),
            ("--unknown https://a.example", new OpenUrl("--unknown")),
            ("--uri", new Empty()),
            ("--page", new Empty()),
            ("--minimized --page", new Minimized()),
            ("--settings --page", new OpenSettings(SettingsTab.Browsers)),
            ("quiver://", new Empty()),
            ("--uri quiver://", new Empty()),
            ("\"\"", new Empty()),
            ("\"QUIVER:// \"", new Empty()),
        ];

        foreach (var (commandLine, expected) in cases)
        {
            CliArgs actual = Parse(commandLine);
            if (actual != expected)
            {
                throw new Exception($"CliArgs check failed: [{commandLine}] parsed as {actual}, expected {expected}");
            }
        }
    }
}
