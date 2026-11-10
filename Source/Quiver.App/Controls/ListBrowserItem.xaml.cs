using Microsoft.UI.Xaml.Controls;

namespace Quiver.App.Controls;

public sealed partial class ListBrowserItem : BrowserItemControl
{
    public ListBrowserItem() => InitializeComponent();

    protected override Button LaunchTarget => LaunchButton;
    protected override void UpdateItemBindings() => Bindings.Update();
}
