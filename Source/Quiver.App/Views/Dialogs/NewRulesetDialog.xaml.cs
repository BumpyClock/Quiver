using Quiver.Library.Models;
using Quiver.App.Controls;
using Quiver.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using System.Linq;

namespace Quiver.App.Views.Dialogs;

public sealed partial class NewRulesetDialog : Page
{
    public StoreRulesetViewModel viewModel { get; }
    public ObservableCollection<DraftRuleViewModel> DraftRules { get; } = [];

    public NewRulesetDialog()
    {
        InitializeComponent();
        viewModel = App.Services!.GetRequiredService<StoreRulesetViewModel>();
        LoadDraftRules();
    }

    public NewRulesetDialog(StoreRulesetViewModel vm)
    {
        InitializeComponent();
        viewModel = vm;
        LoadDraftRules();
    }

    private void LoadDraftRules()
    {
        foreach (Rule rule in viewModel.Rules)
        {
            DraftRules.Add(new DraftRuleViewModel(rule));
        }
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        DraftRules.Add(new DraftRuleViewModel());
    }

    private void RuleCard_DeleteRequested(object? sender, System.EventArgs e)
    {
        if (sender is NewRuleCard { DataContext: DraftRuleViewModel rule })
        {
            DraftRules.Remove(rule);
        }
    }

    public Ruleset Generate()
    {
        viewModel.Rules = DraftRules
            .Where(rule => !string.IsNullOrWhiteSpace(rule.RuleContent))
            .Select(rule => new Rule(rule.RuleContent, rule.Mode.ToString()))
            .ToList();
        return viewModel.ToRuleSet();
    }
}
