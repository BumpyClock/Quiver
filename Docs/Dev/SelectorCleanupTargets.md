# Selector cleanup targets

The comment-only cleanup left the following refactor target. The ListHost blank-footer reservation was
subsequently removed; one measured List frame now owns body and footer allocation. The footer joins
the frame while constrained-height scrolling is needed and returns to its root slot otherwise.

- **MUST KILL `SmokeApplication.OnLaunched`.** The inactive-window phase manually assigns `BrowserRing.ItemsSource` even though production XAML owns that binding. Extract a named inactive-window fixture that owns this setup and makes the test boundary explicit.

Remaining code comments define public geometry contracts or explain WinUI-generated pointer entry, Win2D curve-flattening tolerance and GDI integer-region boundaries. The cleanup changes no application or test logic.
