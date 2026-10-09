using Quiver.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;

namespace Quiver.App.Views;

public sealed partial class BrowsersPage : Page
{
    internal BrowsersPageViewModel ViewModel { get; }
    public ObservableCollection<string> BreadcrumbItems { get; } = ["Browsers"];

    public BrowsersPage()
    {
        ViewModel = App.Services!.GetRequiredService<BrowsersPageViewModel>();
        InitializeComponent();
        Unloaded += (_, _) => ViewModel.CancelIconLoading();
    }

    private void BrowserList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        BrowserItemViewModel? item = args.InRecycleQueue
            ? args.ItemContainer.Content as BrowserItemViewModel ?? args.Item as BrowserItemViewModel
            : args.Item as BrowserItemViewModel ?? args.ItemContainer.Content as BrowserItemViewModel;

        if (item is not null)
        {
            ViewModel.SetBrowserRealized(item, !args.InRecycleQueue);
        }
    }

    private async void RefreshButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ContentDialog dialog = new()
        {
            XamlRoot = XamlRoot,
            Title = "Refresh browsers",
            Content = "Preserve existing entries keeps current browser IDs by matching detected browsers with existing entries by executable path.\n\nAdd all detected entries imports every detected browser as a new entry with a new ID.",
            PrimaryButtonText = "Preserve existing",
            SecondaryButtonText = "Add all detected",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        ContentDialogResult result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            ViewModel.RefreshBrowserList(BrowserRefreshMode.PreserveExistingByExePath);
        }
        else if (result == ContentDialogResult.Secondary)
        {
            ViewModel.RefreshBrowserList(BrowserRefreshMode.AddAllDetectedAsNew);
        }
    }

    private void CreateBrowser_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        Frame?.Navigate(typeof(Quiver.App.Views.EditBrowserPage));
    }

    private void EditBrowser_Click(SplitButton sender, SplitButtonClickEventArgs e)
    {
        if (sender is SplitButton btn && btn.DataContext is BrowserItemViewModel browser)
        {
            Frame?.Navigate(typeof(Quiver.App.Views.EditBrowserPage), browser.Model);
        }
    }

    private void DeleteBrowser_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: Guid browserId })
        {
            ViewModel.DeleteBrowser(browserId);
        }
    }

    private void ListView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        ViewModel.UpdateBrowserOrder();
    }

    private void BreadcrumbBar_ItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        if (args.Index == 0 && Frame?.Content is not BrowsersPage)
        {
            Frame?.Navigate(typeof(BrowsersPage));
        }
    }
}
