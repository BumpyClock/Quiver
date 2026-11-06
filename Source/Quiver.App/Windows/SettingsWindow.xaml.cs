using CommunityToolkit.WinUI;
using Quiver.App.Helpers;
using Quiver.App.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.IO;
using Windows.Graphics;

namespace Quiver.App.Windows;

public sealed partial class SettingsWindow : Window
{
    private enum CloseState { Open, Saving, ReadyToClose }

    private CloseState closeState;

    public SettingsWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        Title = "Quiver Settings";
        AppWindow.ResizeClient(new SizeInt32(1320, 900));
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "internet.ico"));
        SystemBackdrop = new MicaBackdrop();
        AppWindow.Closing += async (_, args) =>
        {
            if (closeState == CloseState.ReadyToClose) return;
            args.Cancel = true;
            if (closeState == CloseState.Saving) return;
            closeState = CloseState.Saving;
            try
            {
                CommitPendingEdits();
                await App.Services!.GetRequiredService<ISettingsService>().FlushAsync();
                closeState = CloseState.ReadyToClose;
                Close();
            }
            catch (Exception ex)
            {
                await new ContentDialog
                {
                    XamlRoot = NavigationFrame.XamlRoot,
                    Title = "Could not save settings",
                    Content = ex.Message,
                    CloseButtonText = "Keep settings open"
                }.ShowAsync();
            }
            finally
            {
                if (closeState == CloseState.Saving)
                {
                    closeState = CloseState.Open;
                }
            }
        };
    }

    public void CommitPendingEdits()
    {
        if (NavigationFrame.Content is Views.QuickViewPage quickViewPage)
        {
            quickViewPage.CommitPendingArguments();
        }
    }

    private void OnNavItemClicked(object sender, ItemClickEventArgs e)
    {
        var item = e.ClickedItem as ListViewItem
            ?? (e.ClickedItem as FrameworkElement)?.FindParent<ListViewItem>();
        if (item?.Tag is string tag)
        {
            NavigateToPage(SettingsTabs.Parse(tag));
        }
    }

    public void NavigateToPage(SettingsTab page)
    {
        (Type pageType, int index) = page switch
        {
            SettingsTab.Browsers => (typeof(Views.BrowsersPage), 0),
            SettingsTab.Rulesets => (typeof(Views.RulesetPage), 1),
            SettingsTab.QuickView => (typeof(Views.QuickViewPage), 2),
            SettingsTab.Settings => (typeof(Views.SettingsPage), 3),
            SettingsTab.About => (typeof(Views.AboutPage), -1),
            _ => throw new ArgumentOutOfRangeException(nameof(page), page, null)
        };

        if (NavigationFrame.CurrentSourcePageType != pageType)
        {
            NavigationFrame.Navigate(pageType);
        }
        NavMenuHeaderList.SelectedIndex = index;
        NavMenuFooterList.SelectedIndex = index == -1 ? 0 : -1;
    }
}
