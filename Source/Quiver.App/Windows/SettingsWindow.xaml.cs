using CommunityToolkit.WinUI;
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
    private bool closeAfterFlush;
    private bool isFlushing;

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
            if (closeAfterFlush) return;
            args.Cancel = true;
            if (isFlushing) return;
            isFlushing = true;
            try
            {
                CommitPendingEdits();
                await App.Services!.GetRequiredService<ISettingsService>().FlushAsync();
                closeAfterFlush = true;
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
                isFlushing = false;
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
        if (item?.Tag is string page)
        {
            NavigateToPage(page);
        }
    }

    public void NavigateToPage(string page)
    {
        (Type pageType, int index) = page.ToLowerInvariant() switch
        {
            "about" => (typeof(Views.AboutPage), -1),
            "rulesets" => (typeof(Views.RulesetPage), 1),
            "quickview" => (typeof(Views.QuickViewPage), 2),
            "settings" => (typeof(Views.SettingsPage), 3),
            _ => (typeof(Views.BrowsersPage), 0)
        };

        if (NavigationFrame.CurrentSourcePageType != pageType)
        {
            NavigationFrame.Navigate(pageType);
        }
        NavMenuHeaderList.SelectedIndex = index;
        NavMenuFooterList.SelectedIndex = index == -1 ? 0 : -1;
    }
}
