using Microsoft.UI.Xaml.Controls;

namespace Quiver.App.Controls;

public sealed partial class RadialBrowserItem : BrowserItemControl
{
    public RadialBrowserItem() => InitializeComponent();

    protected override Button LaunchTarget => LaunchButton;
    protected override void UpdateItemBindings() => Bindings.Update();
}
