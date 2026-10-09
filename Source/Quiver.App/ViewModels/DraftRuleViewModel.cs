using CommunityToolkit.Mvvm.ComponentModel;
using Quiver.Library.Models;
using WinRT;

namespace Quiver.App.ViewModels;

[GeneratedBindableCustomProperty]
public sealed partial class DraftRuleViewModel : ObservableObject
{
    private RuleMode mode = RuleMode.Domain;
    private string ruleContent = string.Empty;

    public DraftRuleViewModel() { }

    public DraftRuleViewModel(Rule rule)
    {
        mode = rule.Mode;
        ruleContent = rule.RuleContent;
    }

    public RuleMode Mode
    {
        get => mode;
        set => SetProperty(ref mode, value);
    }

    public string RuleContent
    {
        get => ruleContent;
        set => SetProperty(ref ruleContent, value);
    }
}
