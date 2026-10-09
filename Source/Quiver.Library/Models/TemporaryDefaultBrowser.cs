namespace Quiver.Library.Models;

public class TemporaryDefaultBrowser
{
    public required Browser TargetBrowser { get; set; }

    public DateTime SelectedAt { get; set; }

    public DateTime ValidTill { get; set; }
}
