using Quiver.Library;
using Microsoft.Windows.AppLifecycle;
using System;
using System.CommandLine;
using System.IO;
using System.Threading.Tasks;
using Windows.ApplicationModel.Activation;

namespace Quiver.App.Helpers;

public abstract record CliArgs
{
    private CliArgs() { }

    public sealed record OpenSettings(SettingsTab Page) : CliArgs;
    public sealed record Minimized : CliArgs;
    public sealed record OpenUrl(string Url) : CliArgs;
    public sealed record Empty : CliArgs;

    // CLI Options and Arguments
    private static readonly Option<bool> MinimizedOption = new("--minimized");
    private static readonly Option<string?> UriOption = new("--uri");
    private static readonly Option<bool> SettingsOption = new("--settings");
    private static readonly Option<string?> PageOption = new("--page");
    private static readonly Argument<string[]> ValuesArgument = new("values")
    {
        Arity = ArgumentArity.ZeroOrMore
    };

    private static readonly RootCommand CliRoot = CreateRootCommand();

    private const string ProtocolPrefix = "quiver://";

    public static CliArgs Parse(string commandLine) => Parse(CliRoot.Parse(commandLine));

    private static CliArgs Parse(ParseResult parseResult)
    {
        bool minimized = parseResult.GetValue(MinimizedOption);
        string? page = ValueOrNull(parseResult, PageOption);
        if (parseResult.GetValue(SettingsOption) || page is not null)
        {
            return new OpenSettings(SettingsTabs.Parse(page ?? string.Empty));
        }

        string[] values = parseResult.GetValue(ValuesArgument) ?? [];
        string? uriValue = ValueOrNull(parseResult, UriOption);

        if (minimized)
        {
            return new Minimized();
        }

        if (!string.IsNullOrWhiteSpace(uriValue))
        {
            return ToOpenUrl(uriValue);
        }

        int first = values.Length > 0
            && string.Equals(values[0], Environment.ProcessPath ?? string.Empty, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        return values.Length > first ? ToOpenUrl(values[first]) : new Empty();
    }

    // An option given without its value (for example a bare "--uri") is a parse error, and GetValue throws on it.
    private static string? ValueOrNull(ParseResult parseResult, Option<string?> option)
    {
        try
        {
            return parseResult.GetValue(option);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static OpenUrl ToOpenUrl(string value)
    {
        if (value.StartsWith(ProtocolPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return new OpenUrl(value[ProtocolPrefix.Length..]);
        }

        return new OpenUrl(value.Contains(' ') ? $"\"{value}\"" : value);
    }

    public static CliArgs GatherInfo(AppActivationArguments activationArgs, bool isSecondInstance)
    {
        if (activationArgs.Kind == ExtendedActivationKind.Protocol
            && activationArgs.Data is IProtocolActivatedEventArgs protocolArgs)
        {
            return ToOpenUrl(protocolArgs.Uri.OriginalString);
        }

        if (activationArgs.Kind == ExtendedActivationKind.File
            && activationArgs.Data is IFileActivatedEventArgs fileArgs
            && fileArgs.Files.Count > 0)
        {
            return ToOpenUrl(fileArgs.Files[0].Path);
        }

        var rawCommandLine = GetActivationCommandLine(activationArgs);
        var parseResult = CliRoot.Parse(rawCommandLine);

#if DEBUG
        Task.Run(() =>
        {
            Directory.CreateDirectory(Constants.APP_SETTINGS_DIR);
            var argsStoreFile = Path.Combine(Constants.APP_SETTINGS_DIR, "args.txt");
            var activationKind = activationArgs.Kind.ToString();
            var rawArgs = parseResult.GetValue(ValuesArgument) ?? [];
            var strFormat = $"\n\n{isSecondInstance} --- {activationKind} --- {string.Join("__", rawArgs)}";
            File.AppendAllText(argsStoreFile, strFormat);
        });
#endif

        return Parse(parseResult);
    }

    private static RootCommand CreateRootCommand()
    {
        RootCommand root = new()
        {
            TreatUnmatchedTokensAsErrors = false
        };

        root.Add(MinimizedOption);
        root.Add(UriOption);
        root.Add(SettingsOption);
        root.Add(PageOption);
        root.Add(ValuesArgument);

        return root;
    }

    private static string GetActivationCommandLine(AppActivationArguments activationArgs)
    {
        return activationArgs.Kind switch
        {
            ExtendedActivationKind.Protocol
                when activationArgs.Data is IProtocolActivatedEventArgs protocolArgs
                    => protocolArgs.Uri.OriginalString,
            ExtendedActivationKind.CommandLineLaunch
                when activationArgs.Data is ICommandLineActivatedEventArgs commandLineArgs
                    => commandLineArgs.Operation.Arguments,
            ExtendedActivationKind.Launch
                when activationArgs.Data is ILaunchActivatedEventArgs launchArgs
                    => launchArgs.Arguments,
            _ => string.Empty
        };
    }
}
