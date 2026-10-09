using Quiver.Library;
using Quiver.Library.Models;
using Quiver.App.ViewModels;
using Quiver.App.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Quiver.App.Views.Dialogs;

public sealed partial class TestRules : Page
{
    public RulesetPageViewModel ViewModel { get; }
    private CancellationTokenSource? testCancellation;

    public TestRules()
    {
        this.InitializeComponent();
        ViewModel = App.Services!.GetRequiredService<RulesetPageViewModel>();
        Unloaded += (_, _) => testCancellation?.Cancel();
    }

    private void TestRuleButton_Click(object sender, RoutedEventArgs e)
    {
        testCancellation?.Cancel();
        var uri = _UriInput.Text;
        var ruleMode = _RuleTypeInput.SelectedValue;
        var ruleContent = _RuleInput.Text;

        if (string.IsNullOrWhiteSpace(uri)
            || string.IsNullOrWhiteSpace(ruleContent)
            || ruleMode is null)
        {
            PresentOutput("Please fill in all fields - URI, Rule Type, Rule", InfoBarSeverity.Error);
            return;
        }

        var rule = new Rule(ruleContent, ruleMode.ToString());

        _ = RunTestAsync(_ => RuleMatch.CheckRule(uri, rule), matches =>
            PresentOutput(matches ? "Rule matches" : "Rule does not match",
                matches ? InfoBarSeverity.Success : InfoBarSeverity.Informational));
    }

    private void TestExistingButton_Click(object sender, RoutedEventArgs e)
    {
        testCancellation?.Cancel();
        var uri = _UriInput.Text;

        if (string.IsNullOrWhiteSpace(uri))
        {
            PresentOutput("Please fill in the URI field", InfoBarSeverity.Error);
            return;
        }

        var snapshot = App.Services!.GetRequiredService<ISettingsService>().PreparedRulesets;
        _ = RunTestAsync(token => snapshot.Check(uri, token), matchingRuleset =>
        {
            if (matchingRuleset is not null)
            {
                PresentOutput($"Ruleset match: {ViewModel.GetBrowserDisplayName(matchingRuleset.BrowserId)}\nRuleset name: {matchingRuleset.RulesetName}", InfoBarSeverity.Success);
            }
            else
            {
                PresentOutput("No Ruleset match", InfoBarSeverity.Informational);
            }
        });
    }

    private async Task RunTestAsync<T>(Func<CancellationToken, T> check, Action<T> present)
    {
        using var cancellation = new CancellationTokenSource();
        testCancellation = cancellation;
        PresentOutput("Testing rules...", InfoBarSeverity.Informational);
        try
        {
            var result = await Task.Run(() => check(cancellation.Token), cancellation.Token);
            if (!cancellation.IsCancellationRequested)
            {
                present(result);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!cancellation.IsCancellationRequested)
            {
                PresentOutput($"Rule test failed: {ex.Message}", InfoBarSeverity.Error);
            }
        }
        finally
        {
            if (ReferenceEquals(testCancellation, cancellation))
            {
                testCancellation = null;
            }
        }
    }

    private void PresentOutput(string text, InfoBarSeverity severity)
    {
        var textBlock = new InfoBar
        {
            Message = text,
            IsOpen = true,
            Severity = severity,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _outputCard.Visibility = Visibility.Visible;
        _outputCard.Children.Clear();
        _outputCard.Children.Add(textBlock);
    }

    private void CopyRuleButton_Click(object sender, RoutedEventArgs e)
    {
        //System.Windows.Clipboard.SetText(_RuleInput.Text);

        var dataPackage = new global::Windows.ApplicationModel.DataTransfer.DataPackage();
        dataPackage.SetText(_RuleInput.Text);
        dataPackage.RequestedOperation = global::Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        global::Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);

    }
}
