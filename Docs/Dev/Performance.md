# Performance and memory changes

Release-note context for the next release:

- Quick View browser arguments save when the field loses focus, the user leaves the page, or the settings
  window closes. Other options still save immediately. Settings updates identify the changed section so
  unrelated edits do not rebuild the selector or reload its icons.
- Selector and browser settings icon loads run with at most four concurrent requests. Replaced loads and
  unloaded pages cancel their work; unchanged selector items retain their icons.
- Custom icon inputs are limited to 8 MiB, with decoded thumbnails bounded to 256 pixels on the longest
  edge while preserving aspect ratio. The icon service
  reuses up to 64 decoded images and shares concurrent requests for the same source. Canceling one caller
  leaves other callers running; canceling the last caller stops the shared request.
- The disk icon cache is limited to 64 MiB, with entries older than 30 days removed during cache writes.
  Local file fingerprints invalidate changed sources. Executable icon selection displays 16 entries per
  page and preserves the selected icon when changing pages. Stale temporary cache files are also removed.
- Closing a Quick View window closes its WebView2 control and prevents pending initialization from
  navigating a closed window. The shared environment remains available to other windows.
- Browser settings use a bounded, scrolling list so offscreen row controls can be virtualized. Other
  settings pages retain their own scrolling. Ruleset edits and moves update the affected display rows.
- Regex rules use a 50 ms timeout. Invalid or timed-out expressions do not match; later rules remain
  eligible. Up to 256 parsed rules are cached with individual eviction, and URL parsing is shared across
  each ruleset check. Existing first-match ordering and supported regex syntax are preserved.
- Native messages larger than 1 MiB are rejected before allocating their body buffer.
- Browser launches dispose their process wrappers after starting the child. Tile animations reuse their
  composition objects and respect the Windows animation preference.

## Verification

Run from the repository root:

```powershell
dotnet run --project Tests/RuleMatchSmoke/RuleMatchSmoke.csproj
dotnet run --project Tests/SettingsSmoke/SettingsSmoke.csproj
dotnet run --project Tests/UiSmoke/UiSmoke.csproj -p:Platform=x64 -p:PublishAot=false
& "$env:USERPROFILE/.cargo/bin/cargo.exe" test --workspace
dotnet build Source/Quiver.App/Quiver.App.csproj -c Release -r win-x64 -p:Platform=x64 -p:GenerateAppxPackageOnBuild=false
```

The rule checks cover exact matching, domain boundaries, invalid expressions, regex syntax compatibility,
timeouts, first-match ordering, edited content, and cache capacity. Settings checks cover section events,
argument persistence, no-op commits, and preservation of other sections. Rust tests cover valid, oversized,
malformed, and truncated frames, including the maximum accepted size.

The headless Windows WinUI checks run production image-loading and view-model code on a real dispatcher
without opening the product UI or changing user settings. They cover local PNG/ICO decoding, proportional
decode sizes, encoded input limits, image reuse, shared-load cancellation, decoded and disk cache eviction,
stale generated cache cleanup, bounded icon paging with selection retention, selector concurrency and
cancellation, and ruleset row reuse.

`Tests/RuleMatchSmoke` accepts `--measure` for a local repeated-matching timing and allocation probe. This
probe measures the matching API, not whole-application latency or memory use.

Before landing, record UI checks for list scrolling and drag/reorder, saving arguments while closing the
settings window, icon selection during loading, and closing Quick View during cold initialization. Measure
application and WebView2 child-process memory across repeated open/close cycles before claiming a memory
reduction or a fixed renderer leak.
