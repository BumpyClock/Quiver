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
            ("--settings", new OpenSettings("browsers")),
            ("--settings --page rulesets", new OpenSettings("rulesets")),
            ("--page about", new OpenSettings("about")),
            ("--page \" \"", new OpenSettings("browsers")),
            ("--settings --minimized https://a.example", new OpenSettings("browsers")),
            ("--page quickview --uri https://a.example", new OpenSettings("quickview")),
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
