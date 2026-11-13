# Radial selector polish review

The five-round cap was reached. The unanimous 9.00 target was **not met**. Final independent scores were Sol 9.10, Opus 9.02 and Muse 8.70. Scores are subjective design judgments under [the fixed rubric](SelectorPolishRubric.md), not performance measurements. Judges are never averaged together to conceal a failure.

| Round | Sol, medium | Opus 5.5 | Muse Wonder Full |
| --- | ---: | ---: | ---: |
| 1, baseline | 6.60 | 6.20 | 6.20 |
| 2 | 9.12 | 8.30 | 8.00 |
| 3 | 9.08 | 8.40 | 8.70 |
| 4 | 9.06 | 8.80 | 8.90 |
| 5, final | 9.10 | 9.02 | 8.70 |

## Frozen round-five implementation

This section describes the judged artifact, not the later bare-icon implementation below.

- Labels keep circular edge clearance. Google Chrome remains complete. Long profile names prefer complete distinguishing suffix words without splitting Unicode text elements.
- Artwork and badges no longer overlap. Bitmap origins align with device pixels while vector geometry stays unrounded.
- Acrylic plate, selection and shadow share the native XAML rendering tree. Selection leaves a concentric two-DIP plate rim.
- Count-aware spacing clears neighboring labels and tiles. Odd counts use actual petal bounds so the link card stays connected.
- Keyboard focus has distinct blue tile ink. Card focus retains browser selection fill without a second focus-like browser contour. Pointer-mode focus does not show keyboard ink.
- Both layouts bind and arrange before showing, without the former radial 180 ms fade/zoom. Native smoke also caught and fixed stale selected theme colors and a List keyboard-wrap scroll regression.

## Remaining finding at the cap

Muse considers light-theme card focus insufficiently distinct from its existing dark rim, and retained fill-only browser selection too faint when that card owns focus. Sol and Opus found no material defect in the same artifact. No sixth radial review was run. Optional observations include closely spaced tile and wedge outlines in keyboard mode and small dense-layout labels.

The shared card-focus finding is relevant to the separately requested List pass. It remains recorded here even if later shared-control work changes that appearance. Round-5 scores apply only to their frozen artifact.

## Later user-directed change

After the capped review, the user explicitly requested removal of radial icon containers. The visible
icon plate and border were removed. At that stage, logos sat directly on the petals, with number/profile
badges kept clear underneath. Keyboard focus uses an accent underline below the label rather than another
box around the logo. Native focused/full checks and a fresh NativeAOT publish passed. Local renders
are in `_Publish/selector-polish/no-icon-tiles/`.

A subsequent user request enlarged the bare logos to 40 DIP and placed profile avatars over their
lower-left edge. Number keycaps now sit beside the logo, on the right of left petals and the left of
right petals, facing the center without covering artwork or the hub. The native spacing solver required
an 83 DIP three-browser ring reservation; compact two-browser geometry remains unchanged. The focused
108-case native fixture and full SelectorSmoke passed. Renders from that adjustment are in
`_Publish/selector-polish/inward-overlay/`. At that stage, no new NativeAOT publication was run.
Neither user-directed change was a sixth judging round; scores above describe only the frozen round-5 artifact.

## Correctness review follow-up

Independent Opus and Astra code reviews led to parallel Opus/Sol fixes for unpackaged PRI/XBF publication,
failed-construction cleanup, monitor-fit fallback, List DPI allocation and rounding, profile trimming,
and default-suite coverage. Radial first-presentation checks now precede rendering waits. A native probe
also reproduced a recycled keyboard target; navigation now finishes reveal/layout before retrieving
the control to focus. Repeated wrap checks verify the actual focused row.

The integrated source passed three complete SelectorSmoke runs, focused radial/List capture fixtures,
UiSmoke, and the secret-free Boat hook checks. A fresh x64 NativeAOT publication contains the application
PRI and all 17 compiled XAML files, and its actual selector process passed startup observation.
No package installation, signing, protocol-registration update or deployment was performed.
The comment-only pass preserved non-comment code; [named cleanup targets](SelectorCleanupTargets.md)
remain follow-up refactors rather than hidden application changes.

## Evidence and limits

The `_Publish` paths in this record are ignored local diagnostics, not checked-in artifacts or portable verification links. Reproduce current checks with the commands in [Performance.md](Performance.md). Local archived artifacts are in `_Publish/selector-polish/round5-final/`. `evidence.md` describes actual renders, fourteen native interaction frames and their traces. `measurements.json` hashes UI/test sources, PNGs, traces and CSVs. `source-snapshot.zip` preserves those scoped sources against later List work.

Three complete native SelectorSmoke runs, the focused 108-case fixture, 330 geometry/native-region cases, UiSmoke and Release x64 NativeAOT publish passed. Earlier matching/settings checks and the secret-free 12-test Boat hook run also passed. During that capped review, no deployment, package signing or launch of the published app was performed.
A later user-requested launch of the bare-icon NativeAOT publish failed to locate
`ms-appx:///Windows/SelectorWindow.xaml`; a Debug app launched instead at that stage. Publishing successfully
does not establish that unpackaged output is launchable.

First-rendering observations used an already-created window and preloaded icons. They do not establish cold startup, OS link-click-to-visible latency or a cross-round speedup. Desktop/acrylic presentation, hardware input, screen-reader speech, enlarged text, whole-desktop high contrast and actual external browser launches remain unverified. Native raster captures cover 100% DPI; geometry checks cover 100-300%.
