# Changelog

## Unreleased

- Redesign the selector as an overlapping acrylic flower with a selection outline that reaches from the pointer to the chosen petal, a centered number badge on each icon, and a pill below that names the selection and shows the link's site. Clicking the pill opens the link actions, and the first petal is preselected so **Enter** opens it.
- Show a petal per Microsoft Edge profile, with the profile picture, when Edge has more than one profile. Browsers with configured alternate launches are unchanged.
- Run prepared rule matching in the background and preserve activation routing when settings restart a pending check.
- Reduce encoded icon allocations, coalesce cache maintenance, and drain pending icon work on normal exit or restart.
- Load browser-settings icons for realized rows and release them reliably when containers recycle.
- Preserve selector rows and shortcuts through browser updates; reuse animation resources.
- Virtualize editable rule drafts and cancel obsolete browser-icon previews.
- Queue settings writes and flush them before normal settings close, exit, or restart; retain failed saves for retry.

See [performance notes](Docs/Dev/Performance.md) for measurements and verification limits.
