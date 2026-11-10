# Performance and memory changes

Release-note context for the next release:

- Quick View browser arguments save when the field loses focus, the user leaves the page, or the settings
  window closes. Other options still save immediately. Settings updates identify the changed section so
  unrelated edits do not rebuild the selector or reload its icons.
- Selector and browser settings each run at most four concurrent icon requests. Replaced loads and
  unloaded pages cancel their work; unchanged selector items retain their icons.
- Custom icon inputs are limited to 8 MiB, with decoded thumbnails bounded to 256 pixels on the longest
  edge while preserving aspect ratio. The icon service
  reuses up to 64 decoded images and shares concurrent requests for the same source. Canceling one caller
  leaves other callers running; canceling the last caller stops the shared request.
- Background maintenance trims the disk icon cache to 64 MiB and removes entries older than 30 days.
  A burst of cache writes can temporarily exceed that budget.
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
- Selector hover and keyboard focus use separate handlers. Delayed focus notifications cannot move
  focus or replace a newer selection, preventing browser highlights from cycling without input.
- The selector backdrop layer keeps its compositor geometry, brushes, shadow, and animation definitions
  when the theme changes. It updates the existing tint and fallback brushes as well as the shadow.
  Same-size warm opens reuse the last successful native shape.
- The selector uses a continuous acrylic flower with 104 DIP petals and inset browser icon tiles.
  The plate, selection surface, icon tiles, keycaps, and their foregrounds follow the Windows light or
  dark theme through live theme resources. High contrast uses paired system colors, including a highlight
  background and highlight text for the hub's normal, hovered, and pressed states.
  Each petal shows its profile or browser name and a
  numbered keycap inside its icon tile. A single outlined selection joins the active petal to the transparent center
  button, using the same radial coordinates as the browser layout. Pointer and keyboard selection share
  this outline instead of a separate rectangular system focus visual. Static tangent bridges slightly widen
  the acrylic flower's two- and three-browser waists so the complete selection and its stroke stay on the
  plate. The same endpoints define the plate, its shadow mask, and the shadow-padded native window region.
  Hovering or changing selection does not reshape the plate. Petal, ring, hub, and window dimensions stay
  unchanged. Browser hit targets are circular.
  The bottom link card shows the destination host and full URL, with ellipsis for long addresses and
  the complete address in its tooltip. Browser selection does not replace the destination details.
  The silhouette casts a soft drop shadow that is lighter in the light Windows theme. The plate edge is
  antialiased instead of following the aliased window region. Clicks within the shadow band no longer
  reach the window behind the selector. With transparency effects off or acrylic unavailable, the plate
  falls back to solid gray in the matching theme.

## Additional changes

- Icon reads retain one encoded stream through decoding and atomic cache writing. Known lengths are
  preallocated within the 8 MiB limit, and read buffers are pooled. Disk maintenance coalesces writes over
  100 ms and runs independently of image completion; the 64 MiB budget is enforced after maintenance,
  rather than before each image displays. Atomic cache writes remain on the image completion path.
  Normal exit and restart stop new loads, cancel active requests, and drain their cache maintenance.
- Browser settings load icons only for realized rows, with at most four active loads. Recycled rows release
  their image references using the container's saved association, even after WinUI clears its content.
  Editing or leaving a browser preview cancels its obsolete request.
- Selector updates preserve collection identity and apply individual moves, replacements, additions, and
  removals. Opening and highlighting the selector reuse composition animations and easing functions.
  `BackdropShapeLayer` retains desktop-compositor path geometry, the acrylic effect factory and brush, the
  shared silhouette mask surface, easing, and animation definitions. `SetTheme` updates the tint,
  fallback color, and shadow without rebuilding geometry. Temporary Win2D union intermediates and owned resources are disposed. Teardown is
  idempotent.
- The selector caches the last successful native shape by radial metrics and pixel window dimensions.
  Same-size warm opens reuse that shape. Collection and native-size notifications coalesce into one
  queued UI-dispatcher update. Unchanged metrics skip resize work. Selected item labels still refresh in
  that update. Forced initial layout and two show-time `MoveAndResize` calls remain.
- Rule editing uses a bounded virtualized list. Draft values survive row recycling and remain available
  when the ruleset is saved.
- Activation and rule tests match immutable prepared rules on workers. Prepared regexes live with the
  current rules snapshot, avoiding the 256-entry cache used by the older standalone matching APIs.
  Ruleset edits replace that snapshot. A newer activation cancels an unfinished older check; cancellation
  is checked between matches, while each regex retains its 50 ms timeout.
  Settings-triggered retries restart only matching, preserving the original activation's routing decision.
- Settings updates capture UTF-8 snapshots on the calling thread and queue them to one background writer. Pending snapshots
  coalesce to the newest complete document. Change events describe the in-memory update; `FlushAsync`
  waits for persistence. Closing settings, exiting, and restarting commit pending arguments and flush
  writes. Failed writes retain the newest pending snapshot for retry. Forced process termination can
  interrupt a queued save.
- Quick View browser mode searches the existing browser collection directly; WebView mode materializes
  the visible browser list once.

## Local measurements

On Windows with .NET SDK 10.0.401 and Release builds, the matching probe compared 1,000 warm scans of
300 regex rules that match only the final rule. Two runs recorded 835–873 ms and 819,271,464–819,341,496
allocated bytes for the standalone cached API, versus 20–24 ms and 56,000 bytes for the prepared snapshot.
Each run measured both paths after warmup. These are operation measurements, not application latency measurements.

An isolated probe of the baseline and changed bounded-read methods used an 8 MiB seekable payload,
five warmup pairs, and 20 samples per implementation. Allocation per read was approximately
25,166,702 bytes before and 8,388,872 bytes after (66.7% lower). Timing was noisy and does not establish
an end-to-end image-loading speedup. The probe excluded input creation and native decoding.

## Verification

Run from the repository root:

```powershell
dotnet run --project Tests/RuleMatchSmoke/RuleMatchSmoke.csproj
dotnet run --project Tests/SettingsSmoke/SettingsSmoke.csproj
dotnet run --project Tests/UiSmoke/UiSmoke.csproj -p:Platform=x64 -p:PublishAot=false
dotnet run --project Tests/SelectorSmoke/SelectorSmoke.csproj -p:Platform=x64 -p:PublishAot=false
& "$env:USERPROFILE/.cargo/bin/cargo.exe" test --workspace
dotnet build Source/Quiver.App/Quiver.App.csproj -c Release -r win-x64 -p:Platform=x64 -p:GenerateAppxPackageOnBuild=false
dotnet publish Source/Quiver.App/Quiver.App.csproj -c Release -r win-x64 -p:Platform=x64 -p:GenerateAppxPackageOnBuild=false
```

For NativeAOT verification, use x64 Visual Studio Developer PowerShell and include the Visual Studio
Installer directory containing `vswhere.exe` on `PATH`. The picker control inheritance can be checked
without packaging, signing, deploying, or reading user settings:

```powershell
dotnet publish Source/Quiver.App/Quiver.App.csproj -c Release -r win-x64 -p:Platform=x64 -p:SelfContained=true -p:PublishAot=true -p:WindowsPackageType=None -p:EnableMsixTooling=false -p:GenerateAppxPackageOnBuild=false -o Tests/SelectorSmoke/bin/aot-picker
```

The rule checks cover exact matching, domain boundaries, invalid expressions, regex syntax compatibility,
timeouts, first-match ordering, edited content, cache capacity, immutable prepared snapshots, and cancellation.
Settings checks cover section events, argument persistence, no-op commits, preservation of other sections,
queued-save flushing, newest snapshot persistence, rules snapshot invalidation, and retry after storage failure.
Layout settings checks also verify that transient/undefined ComboBox indices do not change or save the mode.
Rust tests cover valid, oversized,
malformed, and truncated frames, including the maximum accepted size.

The headless Windows WinUI checks run production image-loading and view-model code on a real dispatcher
without opening the product UI or changing user settings. They cover local PNG/ICO decoding, proportional
decode sizes, encoded input limits, image reuse, shared-load cancellation, decoded and disk cache eviction,
stale generated cache cleanup, bounded icon paging with selection retention, selector concurrency and
cancellation, and ruleset row reuse.
They also cover selector collection identity and reorder/hide updates, realized-row icon concurrency,
recycled image reference release, and superseded editor preview cancellation. Cache-size assertions await
the explicit maintenance drain.
Container checks also cover cleared event data, reassignment, and scrolling an actual 1,000-row ListView
in an offscreen test window. Drain checks cover active loads and canceled sole waiters.
UiSmoke also covers repeated Windows.UI.Composition desktop acrylic shape, shadow theme, and animation
updates, and double disposal.

`Tests/SelectorSmoke` runs the production selector with in-memory settings. Radial focus checks use
an inactive, offscreen window. List lifecycle checks briefly activate the test picker, then move it offscreen. It checks rapid keyboard and programmatic focus changes, stable browser labels, and delayed
browser focus notifications after focus has moved to link options. It also checks the ink stamp ring radii
for two, four, and six browsers, the center disc, and the link options width. Selection checks cover
both connectors' tangency to the petal and hub, stroke bounds, and alignment with the arranged browser circles.
Full filled-selection-plus-stroke containment runs against the production Win2D backdrop builder for
0 through 10 browsers at 100%, 125%, 150%, 175%, 200%, and 300% DPI, covering 330 selections. It allows
0.05 DIP for curve flattening. Real GDI `PtInRegion` checks sample every half physical pixel along the
selection and its shadow-padded envelope. Selection ink has no raster tolerance; the outer shadow band
allows two physical pixels for integer ellipse/polygon scan conversion. Native window-edge clipping remains
in force. The high-contrast resource check covers all three hub states without changing the desktop theme.
List checks cover settings-driven layout switching, full-width rows, focus selection, light/dark labels,
alternate-launch menus, production arrow navigation wrapping to an offscreen browser, bounded scrolling
with 30 browsers, reopening at the first row, and the empty state. Wrapping in both directions checks
that the destination row is fully visible, not merely focused with a nonzero scroll offset. List reveal
uses the fixed row metric and the scroll viewer's viewport; it does not rely on the layout rectangle of
a newly realized item. Platform-generated pointer-entry events are ignored so layout and scrolling do
not replace keyboard selection; actual pointer and touch events keep their existing behavior.
A short-window fixture followed by
hidden browser hide/unhide checks that host sizing stays tied to actual bounds even when shape keys recur.
Reintroducing nominal sizing into layout selection makes that regression check fail.
Placement checks cover negative monitor origins, narrow work areas, horizontal flipping, and 100-300% DPI.
Focus checks wait for the actual focused button and selected row to converge, then count synchronous
`GettingFocus` events to detect unwanted focus movement. Asynchronous `GotFocus` delivery alone does not
mean focus moved.
Radial and List use separate XAML hosts and item controls. `BrowserItemControl` shares launch events,
alternate-launch menus, shortcuts, and selection properties without constructing both item visual trees.
The window retains commands, selection, and lifetime. It captures cursor/monitor placement once per open
and reuses that snapshot during browser refreshes. List dimensions have one writer based on actual window
bounds, applied before the native-shape cache check. Its internal grid owns row and footer allocation.
The list card uses a XAML acrylic surface and shadow in the same visual tree as its rows. It does not
replay the radial entrance animation or show a separately committed native plate. Cold-opening checks
verify bound, arranged rows and full-opacity, unscaled content without an active entrance animation.
Set `QUIVER_SELECTOR_CAPTURE_DIRECTORY` to an output directory for light/dark screenshots and sampled
cold/warm opening sequences with timestamps and window bounds. Opening sequences show the picker beside
the current cursor; theme captures briefly move it to screen position (100, 100). Capture work adds overhead,
so these sequences are visual diagnostics, not opening-latency benchmarks. Fixtures use fake browsers
without icons and do not read or change real user settings.

`Tests/RuleMatchSmoke` accepts `--measure` for a local repeated-matching timing and allocation probe. This
probe measures the matching API, not whole-application latency or memory use.

Before landing, record UI checks for list scrolling and drag/reorder, saving arguments while closing the
settings window, icon selection during loading, and closing Quick View during cold initialization. Measure
application and WebView2 child-process memory across repeated open/close cycles before claiming a memory
reduction or a fixed renderer leak.
