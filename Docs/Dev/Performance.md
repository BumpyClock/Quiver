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
- The radial plate, focus ink, browser content, and shadow now share the WinUI XAML rendering tree.
  The plate uses a WinUI XAML acrylic brush and an antialiased geometry group. Native root renders do
  not establish desktop-host blur. Its live alpha mask casts
  the shadow. This removes the independently rasterized desktop-compositor mask and the edge mismatch
  between that mask and the XAML selection. Same-size warm opens reuse the last successful native region.
- The selector uses a continuous acrylic flower with 104 DIP petals and bare browser artwork, without
  icon container fills or borders. The plate, selection surface, keycaps, and their foregrounds follow the Windows light or
  dark theme through live theme resources. High contrast uses paired system colors, including a highlight
  background and highlight text for the hub's normal, hovered, and pressed states.
  An 80 by 52 DIP unpainted icon/badge layout slot and upward-inset content leave at least six DIPs between each label's layout bounds
  and the circular petal edge at normal text scale. Google Chrome remains fully visible. Longer names
  use measured middle ellipsis, preferring complete distinguishing suffix words and word-boundary prefixes.
  Unbroken names retain a grapheme-safe middle-cut fallback. Truncation removes whitespace next to the ellipsis. Full browser/profile
  identity remains in the tooltip and accessible name. Bare browser artwork is 40 DIP. Profile avatars
  overlay its lower-left edge, including profiles without a number shortcut. Numbered keycaps sit beside
  the artwork on each petal's center-facing horizontal side, right on left petals and left on right petals.
  They leave the center button clear. Semibold 12 DIP digits and stronger light-theme keycap tones keep
  shortcuts distinct from the petal. The existing 1-9 shortcut contract is unchanged. Bitmap
  artwork snaps to device pixels after ancestor layout, while vector plate and focus coordinates stay
  unrounded. A single outlined selection joins the active petal to the transparent center button, using
  the same radial coordinates as the browser layout. A separate two-DIP system-accent underline below
  the label marks actual keyboard focus without boxing the icon. Pointer mode hides that underline. Its focus-state
  callback detaches on unload. Moving focus to link options keeps browser selection fill but removes
  the browser contour, leaving one active focus owner. Returning to a browser restores its contour.
  The selected outer stroke has a concentric
  two-DIP inset from the plate edge, avoiding a double-weight rim. Static tangent bridges slightly widen
  the acrylic flower's two- and three-browser waists so the complete selection and its stroke stay on the
  plate. The same endpoints define the plate, its shadow mask, and the shadow-padded native window region.
  Hovering or changing selection does not reshape the plate. Petal and hub sizes stay unchanged.
  The ring expands with browser count to keep selection ink at least four DIPs away from neighboring
  labels, artwork and visible badges. Three-browser radius is 83 DIP to clear the enlarged inward-facing
  keycaps. Two-browser placement stays compact. Browser hit targets are circular.
  Odd-count flowers use their actual lowest petal bounds, keeping the same 14 DIP geometric link-card
  gap as even counts instead of leaving an empty part of the ring viewport. The hub stays on the radial
  center rather than the shortened viewport's midpoint. The light selection outline is softer but retains
  at least 3:1 contrast against its selection surface.
  The bottom link card shows the destination host and full URL, with ellipsis for long addresses and
  the complete address in its tooltip. Browser selection does not replace the destination details.
  The silhouette casts a soft drop shadow that is lighter in the light Windows theme. The plate edge is
  antialiased instead of following the aliased window region. Clicks within the shadow band no longer
  reach the window behind the selector. The brush defines a solid fallback palette in the matching
  theme. Live desktop blur and transparency-off behavior remain unverified.

- List rows keep a 52 DIP minimum at normal text size and separate browser identity from the profile
  label. One measured List metric owns row, recovery-body and footer heights, viewport allocation and
  keyboard reveal. Scalable text can grow without clipping the row or overlapping the footer. When a
  short work area cannot fit the pinned footer and one row or recovery body, the complete List frame
  scrolls instead of hiding all browser choices. Keyboard focus reveals browser rows, recovery and link
  options within that frame. Normal-height List keeps its pinned footer. The shared
  measured middle-ellipsis formatter preserves profile suffixes without splitting Unicode text elements.
  Inline profile avatars no longer cover browser artwork. Rows without number shortcuts reclaim that
  column's text width. Selection uses a stable fill; keyboard focus uses system-accent native focus ink.
  A reserved scrollbar gutter and an integral-row viewport remove clipped row slivers while preserving
  full reveal on keyboard wrap. Row allocation uses rounded native window dimensions, so custom DPI
  rounding does not discard a whole row. List content and its footer use device-pixel layout rounding;
  the radial vector root stays unrounded. Profile text retains a native ellipsis fallback.
  List destination captions show path/query/fragment without repeating the host. The host caption
  retains the complete HTTP(S) hostname, non-default ports and IPv6 brackets; `www` and apex hosts remain
  distinct. Tooltips and UI Automation retain the complete address. The shared destination card has a
  subtle persistent plate and an offset system-accent focus outline. Empty List state includes Manage
  browsers with matching accent ink, offset focus treatment and extra caption clearance.

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
  removals. Both picker layouts bind and arrange before presentation and open fully opaque at full size.
  The radial zero-opacity, 82%-scale, 180 ms entrance has been removed. `BackdropShapeLayer` retains only
  the XAML plate's alpha mask and composition shadow; theme updates change shadow colors without rebuilding
  geometry. Selected item resources reapply after load and queued theme propagation, so selected labels
  and keycaps do not retain the previous theme. Native tests cycle light/dark themes twelve times. Temporary Win2D union intermediates and owned resources are disposed.
  Teardown is idempotent. Selector subscriptions attach only after XAML and window setup complete,
  so failed initialization cannot leave callbacks that dereference an uninitialized visual tree.
- Unpackaged NativeAOT publishing includes the application PRI and compiled XAML as explicit publish
  inputs. MSIX publishing keeps its existing resource path. The publication smoke script verifies
  declared resources in a fresh output directory and can observe a separately launched selector.
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
.\Tests\PublishSmoke\Test-UnpackagedPublish.ps1 -PublishDirectory _Publish/picker-aot-check
```

The publication check requires a fresh output directory, then verifies the application PRI and every
compiled application XAML file. Use `-VerifyOnly` to inspect an existing publication. Optional
`-ObserveProcessId <pid>` verifies a separately started published selector; the script never starts or
stops the app. A completed build alone does not prove that the published app launches.

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
UiSmoke also covers repeated XAML plate geometry and shadow theme/visibility updates, and double disposal.

`Tests/SelectorSmoke` runs the production selector with in-memory settings. Initial radial focus checks
use an inactive, offscreen window. Radial polish and List lifecycle checks activate the native picker,
then move it offscreen when not capturing. The twelve-opening radial timing loop runs only in explicit
capture mode. It checks rapid keyboard and programmatic focus changes, stable browser labels, and delayed
browser focus notifications after focus has moved to link options. It also checks compact two-browser
placement, radial spacing, the center disc, and the link options width. Selection checks cover
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
that the destination row is fully visible, not merely focused with a nonzero scroll offset. Repeated
wrap cycles also require actual focus on the expected row. Navigation reveals the measured uniform-row offset
and completes layout before retrieving its focus target, so recycling cannot reassign a held control
between lookup and focus. List reveal
uses the measured row metric and the scroll viewer's viewport; it does not rely on the layout rectangle of
a newly realized item. Implicit focus scrolling is disabled for the list host so a parked, force-realized
row cannot override that reveal with a stale negative layout position. Explicit navigation and actual
current-focus notifications share the measured-row reveal, including focus entered through other APIs.
Platform-generated pointer-entry events are ignored so layout and scrolling do
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
and reuses that snapshot during browser refreshes. If the complete radial bounds cannot fit that monitor,
the effective layout falls back to List without saving a different preference. A later opening that fits
regains Radial. Layout transitions and same-Radial growth share the opening work-area clamp while
preserving a fitting radial anchor. Fit checks cover full flower/footer/shadow bounds, corner refreshes,
negative origins and multiple scales. List dimensions have one writer based on actual window bounds,
applied before the native-shape cache check. A measured List frame owns body and footer allocation.
The one-column uniform virtualizing layout and its explicit total height use that same row metric,
preventing stale estimated scroll extents after live text-size changes. Allocation keeps double precision
through the 101-DPI boundary; graphics conversion happens afterward. A second frame scroller activates
only when the pinned footer plus one whole row or recovery cannot fit. In that mode the existing footer
moves into the frame; its geometry owns the extent and focus-reveal offsets. Live layout changes and
focus events share one post-layout actual-focus reveal. Recovery reveal uses the link's own bounds,
not the possibly oversized empty body. Restoring space or switching to Radial returns the footer to
its original root slot and preserves its focus kind.
Both layouts use XAML acrylic and shadows in the same visual tree as their controls. Neither replays
an entrance animation or shows a separately committed native plate. Cold-opening checks
verify bound, arranged rows and full-opacity, unscaled content without an active entrance animation.
Set `QUIVER_SELECTOR_CAPTURE_DIRECTORY` to an output directory for light/dark screenshots and sampled
cold/warm opening sequences with timestamps and window bounds. Opening sequences show the picker beside
the current cursor; theme captures briefly move it to screen position (100, 100). Capture work adds overhead,
so these sequences are visual diagnostics, not opening-latency benchmarks. Fixtures use fake browsers
without icons and do not read or change real user settings.

Set `QUIVER_SELECTOR_POLISH_ONLY=1` for the focused radial fixture. It always asserts fully arranged,
opaque, unscaled presentation immediately after `ShowWindow` returns, before compositor waits, the
first rendering event or image encoding. A deferred-arrangement mutation must be rejected. Radial
rendering fixtures pass an explicit unconstrained work-area snapshot at the actual native scale so
all 2-10 browser cases remain Radial; separate fit fixtures retain real and constrained work areas.
With `QUIVER_SELECTOR_CAPTURE_DIRECTORY` set, it saves actual WinUI `RenderTargetBitmap` captures of
2 through 10 choices in light/dark themes, including every selection for 2, 3, and 6 choices and
long profile names. Native containment checks exercise all 108 selections/theme combinations across
those nine browser counts. They require four DIPs between the full selected surface/stroke and every
neighboring label/artwork rectangle or visible badge shape, using actual arranged XAML coordinates. Edge and Chrome must be installed for these icon-bearing capture fixtures. These
are native XAML renders, not desktop screenshots or mockups. Desktop backdrop sampling and screen
presentation remain separate checks. The fixture also checks partially covered boundary pixels to
reject an aliased binary plate mask, full-name tooltips, and six-DIP circular label clearance.
They also verify native UI Automation button names, suffix-preserving Unicode truncation, consistent
ellipsis spacing, actual device-pixel bitmap origins, fixed card-to-petal gaps, hub placement, and badges inside
the unpainted layout slot, lower-left avatar overlays, inward-facing non-overlapping keycaps, hub clearance,
4.5:1 keycap-text contrast, and 3:1 disclosure-chevron contrast
against the native fallback palette. These palette checks do not qualify arbitrary desktop backgrounds.
Icon-bearing captures also include seven-frame native focus sequences in both themes with `interaction-light-trace.json` and
`interaction-dark-trace.json`. They record actual focus, selection, browser stroke and keyboard-indicator
opacity while production Down navigation advances and wraps, focus moves to link options and returns,
and the same selected browser switches to pointer-mode focus. These are API-driven interactions, not
physical keystrokes or a screen-reader session.
Set `QUIVER_SELECTOR_SOLVE_SPACING=1` together with `QUIVER_SELECTOR_POLISH_ONLY=1` to rerun the bounded
geometry search against measured icon-slot/label dimensions. It writes `radial-spacing-solution.txt` beside
the smoke executable, including the minimum radius at every browser count. This keeps spacing changes
reproducible instead of guessing larger radii after each overlap.
`radial-opening-timing.csv` records `ShowWindow` return, `DwmFlush`, and the first WinUI rendering event
for one first and eleven warm presentations with preloaded icons, before screenshot encoding.
Icon preparation is outside this timing interval and is explicitly not a cold-icon-loading measurement.
These are in-process fixture timings,
not OS link-click, process activation, rule-matching, icon-loading, or physical display latency.

The ordinary SelectorSmoke suite also runs icon-independent List polish assertions without a capture
directory or installed browsers. Set `QUIVER_SELECTOR_LIST_NAVIGATION_ONLY=1` to run the focused List
navigation and repeated-wrap fixture. `QUIVER_SELECTOR_TEXT_SCALE_ONLY=1` runs strict controlled-font
checks from 1x through 2.25x, including live increases/decreases, exact scroll extents, row text, keycaps,
recovery/footer separation and full wrap reveal. Profile fixtures are restored and required after every
reopen and visible constrained refresh. Constrained checks also exercise recovery/footer focus, Tab,
frame scrolling, focused last-row retention through text growth/shrink and viewport shrink, recovery
links in oversized bodies, return to normal size and List/Radial switching in simulated 200 and 226-DIP work areas
at the actual native raster scale. Compiled geometry covers the corresponding 600 and 680-pixel work
areas at 300%. Native checks do not change Windows settings or qualify real 300% desktop appearance,
the actual global accessibility-setting event path, or mouse-wheel chaining between the two scrollers.
A target taller than the padded frame viewport cannot be shown whole at once.
A separate child process with missing SelectorWindow XAML checks
that failed construction shuts down without a second exception.

Set `QUIVER_SELECTOR_LIST_POLISH_ONLY=1` with `QUIVER_SELECTOR_CAPTURE_DIRECTORY` for native icon-bearing
List captures at counts 0, 1, 2, 6 and 30 in both themes. `list-interaction-trace.json` records actual
keyboard/pointer-mode/card/recovery focus and production wrapping. Checks cover full UI Automation
identity, visible profile suffixes, artwork/avatar separation, scrollbar clearance, integral-row
viewports, complete last-row reveal and full destination tooltip/help text. The in-process first-render
fixture still uses two unprofiled cached-icon rows, not cold profile discovery. `capture-bounds.csv`
records raw bitmap and actual viewport sizes; empty WinUI focus-canvas overhang is removed without
rescaling native pixels. An attempted OS cursor-injection hover capture did not produce a qualified
pointer-entry event; actual hover and hardware input remain unverified.

`Tests/RuleMatchSmoke` accepts `--measure` for a local repeated-matching timing and allocation probe. This
probe measures the matching API, not whole-application latency or memory use.

Before landing, record UI checks for list scrolling and drag/reorder, saving arguments while closing the
settings window, icon selection during loading, and closing Quick View during cold initialization. Measure
application and WebView2 child-process memory across repeated open/close cycles before claiming a memory
reduction or a fixed renderer leak.
