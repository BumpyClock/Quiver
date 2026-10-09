# Changelog

## Unreleased

- Fix NativeAOT runtime selection for x64 and ARM64 builds, correct the .NET SDK baseline, and resolve build warnings in package dependencies, browser initialization, and the About page.
- Run prepared rule matching in the background and preserve activation routing when settings restart a pending check.
- Reduce encoded icon allocations, coalesce cache maintenance, and drain pending icon work on normal exit or restart.
- Load browser-settings icons for realized rows and release them reliably when containers recycle.
- Preserve selector rows and shortcuts through browser updates; reuse animation resources.
- Virtualize editable rule drafts and cancel obsolete browser-icon previews.
- Queue settings writes and flush them before normal settings close, exit, or restart; retain failed saves for retry.

See [performance notes](Docs/Dev/Performance.md) for measurements and verification limits.
