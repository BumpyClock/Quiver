using Quiver.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Quiver.App.Views;

public sealed partial class QuickViewPage : Page
{
    public QuickViewPage()
    {
        InitializeComponent();

        ViewModel = App.Services!.GetRequiredService<QuickViewPageViewModel>();
        Unloaded += (_, _) => CommitPendingArguments();
    }

    public QuickViewPageViewModel ViewModel { get; }

    private void AdditionalBrowserArguments_LostFocus(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => CommitPendingArguments();

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        CommitPendingArguments();
        base.OnNavigatedFrom(e);
    }

    public void CommitPendingArguments()
        => ViewModel.Option_AdditionalBrowserArguments = AdditionalBrowserArgumentsTextBox.Text;
}
