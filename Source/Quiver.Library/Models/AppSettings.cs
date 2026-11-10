using CommunityToolkit.Mvvm.ComponentModel;

namespace Quiver.Library.Models;

public partial class AppSettings : ObservableObject
{
    [ObservableProperty]
    public partial bool MinimizeOnFocusLoss { get; set; } = true;

    [ObservableProperty]
    public partial bool RuleMatching { get; set; } = false;

    [ObservableProperty]
    public partial SelectorLayout SelectorLayout { get; set; } = SelectorLayout.Radial;
}

public enum SelectorLayout
{
    Radial = 0,
    List = 1
}
