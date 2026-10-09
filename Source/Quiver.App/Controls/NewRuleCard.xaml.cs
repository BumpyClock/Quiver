using Quiver.Library.Models;
using Microsoft.UI.Xaml.Controls;
using System;

namespace Quiver.App.Controls;

public sealed partial class NewRuleCard : UserControl
{
    private static readonly RuleMode[] RuleModeValues = Enum.GetValues<RuleMode>();

    public RuleMode[] RuleModes => RuleModeValues;

    public event EventHandler? DeleteRequested;

    public NewRuleCard()
    {
        InitializeComponent();
    }

    private void DeleteButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        DeleteRequested?.Invoke(this, EventArgs.Empty);
    }
}
