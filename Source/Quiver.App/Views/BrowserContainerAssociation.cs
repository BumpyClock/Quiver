using Microsoft.UI.Xaml.Controls;
using Quiver.App.ViewModels;

namespace Quiver.App.Views;

internal static class BrowserContainerAssociation
{
    public static void Update(ListViewItem container, BrowserItemViewModel? item, BrowsersPageViewModel viewModel)
    {
        // WinUI can clear the event item and container content before recycling.
        if (container.Tag is BrowserItemViewModel previousItem && !ReferenceEquals(previousItem, item))
        {
            viewModel.SetBrowserRealized(previousItem, false);
        }

        container.Tag = item;
        if (item is not null)
        {
            viewModel.SetBrowserRealized(item, true);
        }
    }
}
