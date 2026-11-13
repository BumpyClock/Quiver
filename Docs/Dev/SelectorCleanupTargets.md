# Selector cleanup targets

The comment-only cleanup leaves two refactor targets. Neither changes behavior in this PR.

- **MUST KILL `SelectorWindow.ListHost`.** Its blank footer row reserves space for the sibling `NamePill`, whose position is calculated separately. Extract a List frame that owns body and footer allocation together instead of requiring a comment to explain the empty row.
- **MUST KILL `SmokeApplication.OnLaunched`.** The inactive-window phase manually assigns `BrowserRing.ItemsSource` even though production XAML owns that binding. Extract a named inactive-window fixture that owns this setup and makes the test boundary explicit.

Remaining code comments define public geometry contracts or explain WinUI-generated pointer entry, Win2D curve-flattening tolerance and GDI integer-region boundaries. The cleanup changes no application or test logic.
