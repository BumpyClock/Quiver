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

## Additional changes

- Icon reads retain one encoded stream through decoding and atomic cache writing. Known lengths are
  preallocated within the 8 MiB limit, and read buffers are pooled. Disk maintenance coalesces writes over
  100 ms and runs independently of image completion; the 64 MiB budget is enforced after maintenance,
  rather than before each image displays. Atomic cache writes remain on the image completion path.
- Browser settings load icons only for realized rows, with at most four active loads. Recycled rows release
  their image references. Editing or leaving a browser preview cancels its obsolete request.
- Selector updates preserve collection identity and apply individual moves, replacements, additions, and
  removals. Opening and highlighting the selector reuse composition animations and easing functions.
- Rule editing uses a bounded virtualized list. Draft values survive row recycling and remain available
  when the ruleset is saved.
- Activation and rule tests match immutable prepared rules on workers. Prepared regexes live with the
  current rules snapshot, avoiding the 256-entry cache used by the older standalone matching APIs.
  Ruleset edits replace that snapshot. A newer activation cancels an unfinished older check; cancellation
  is checked between matches, while each regex retains its 50 ms timeout.
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
& "$env:USERPROFILE/.cargo/bin/cargo.exe" test --workspace
dotnet build Source/Quiver.App/Quiver.App.csproj -c Release -r win-x64 -p:Platform=x64 -p:GenerateAppxPackageOnBuild=false
dotnet publish Source/Quiver.App/Quiver.App.csproj -c Release -r win-x64 -p:Platform=x64 -p:GenerateAppxPackageOnBuild=false
```

The rule checks cover exact matching, domain boundaries, invalid expressions, regex syntax compatibility,
timeouts, first-match ordering, edited content, cache capacity, immutable prepared snapshots, and cancellation.
Settings checks cover section events, argument persistence, no-op commits, preservation of other sections,
queued-save flushing, newest snapshot persistence, rules snapshot invalidation, and retry after storage failure.
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

`Tests/RuleMatchSmoke` accepts `--measure` for a local repeated-matching timing and allocation probe. This
probe measures the matching API, not whole-application latency or memory use.

Before landing, record UI checks for list scrolling and drag/reorder, saving arguments while closing the
settings window, icon selection during loading, and closing Quick View during cold initialization. Measure
application and WebView2 child-process memory across repeated open/close cycles before claiming a memory
reduction or a fixed renderer leak.
