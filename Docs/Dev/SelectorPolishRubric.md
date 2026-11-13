# Selector polish judging contract

Task scope is Quiver's existing radial browser picker. Preserve the flower, cursor-centered placement, link card, keyboard shortcuts, profile avatars, and alternate launches. Fix crowding at petal edges and make link activation feel immediate. No unrelated redesign.

Archived judging instructions used UX critique, visual craft, typography, and accessibility guidance. This portable record omits the review machine's private skill paths; the scoring categories and scale below are unchanged. Reviews are read-only. Never change product files or this rubric. Use this unchanged rubric every round. Scores describe inspected evidence, not assumed behavior. Do not award a target score merely because it was requested.

## Fixed scoring

Score each category from 0 to 10. Overall is the arithmetic mean of the five scores. A 9 means polished, no material defect in inspected evidence, only small optional refinements. A 10 means exceptional within the inspected scope. A 7 or 8 means good but concrete visible or verified interaction defects remain. Lower scores indicate greater task friction.

1. **Containment and spacing.** Browser names, icon tiles, badges, focus ink, and link details have deliberate breathing room. Text stays within the circular silhouette, including Google Chrome and long custom/profile names. Petals do not overlap unrelated content.
2. **Hierarchy and legibility.** Browser/profile identity is readable at native scale. Shortcuts remain readable without obscuring icons. Link host and address have clear hierarchy. Selection reads immediately without overpowering names.
3. **Visual craft and coherence.** Optical alignment, consistent radii, acrylic, shadow, theme colors, and focus treatment look intentional. Preserve the established flower identity. Inspect actual native renders, not hypothetical source-only appearance.
4. **Interaction and accessibility.** Pointer and keyboard selection agree. Focus, launch targets, menus, escaping, high contrast, reduced motion, and long labels retain usable behavior. Static images cannot prove interaction. Name unverified checks explicitly.
5. **Immediacy and continuity.** Picker is readable and actionable on first presentation, without a blank plate, delayed entrance, visible relayout, or synchronous optional work delaying it. Judge opening captures and actual timing evidence when available. Do not call screenshot capture overhead application latency.

## Required response each round

- Round and evidence inspected.
- Five scores and overall to two decimals. State partial evidence and unverified states.
- Concrete blockers with affected element and evidence, ordered by user impact.
- Smallest corrections that would materially improve the score.
- Explicit answer whether inspected design reaches 9/10. No averaging between judges to conceal one judge below 9.

Completion target is every enabled independent judge at least 9.00 overall on the same final artifact, with no unresolved material defect. Native smoke checks and latency evidence remain separate requirements; aesthetic scores do not prove functional correctness.

## Run policy

User capped critique at five rounds total, including the initial baseline round. Stop earlier when every judge reaches 9.00. Judges are GPT 6.1 Sol with medium reasoning, Claude Opus 5.5, and Muse Wonder Full 1.0 through the Muse CLI when available. Adding the third judge and the round cap does not change the fixed scoring categories or scale above. Report final actual scores even when the cap is reached below target.
