# List selector polish judging contract

This is the separately requested List polish pass after the five-round radial pass. Preserve compact vertical browser picking, cursor-relative placement, destination card, number shortcuts 1-9, profile avatars, alternate launches, scrolling and keyboard navigation. Improve native readability, spacing, state clarity and immediate first presentation. No unrelated redesign.

Use these unchanged categories and scoring scale for every List round and every judge. They retain the radial contract's five equally weighted dimensions; containment refers to rows rather than petals. Reviews are read-only. Judges use UX critique, visual craft, typography, and accessibility guidance. This portable record omits private machine-specific skill paths without changing the scoring categories or scale. Scores describe inspected evidence, not assumed behavior, and must not be inflated to reach the target.

## Fixed scoring

Score each category from 0 to 10. Overall is the arithmetic mean of the five scores. A 9 means polished, no material defect in inspected evidence, only small optional refinements. A 10 is exceptional within inspected scope. A 7 or 8 means good but concrete visible or verified interaction defects remain.

1. **Containment and spacing.** Browser/profile text, icons, avatars, shortcuts, focus ink and destination details have deliberate breathing room. Long labels fit without collisions. The bounded list scrolls and fully reveals its focused rows.
2. **Hierarchy and legibility.** Browser and profile identity remain distinguishable at native scale, including shared-prefix names. Shortcuts remain readable without covering artwork. Host and address have clear hierarchy. Selection is clear without overwhelming labels.
3. **Visual craft and coherence.** Alignment, row rhythm, radii, acrylic, shadow, themes and focus treatment feel intentional and consistent with Quiver. Judge actual native output, not a hypothetical source-only appearance.
4. **Interaction and accessibility.** Selection and actual focus remain coherent and distinct. Keyboard navigation, wrapping, scroll reveal, launch targets, menus, escaping, high contrast, reduced motion and full accessible names retain usable behavior. Static images cannot prove interaction. Name unverified checks.
5. **Immediacy and continuity.** List is readable and actionable on first presentation, without a blank plate, entrance delay, visible relayout or optional synchronous work blocking it. Use opening captures and timing evidence when available. Do not treat capture encoding as app latency or in-process cached-icon measurements as OS click-to-visible latency.

## Response and stopping rule

- State round and exact evidence inspected.
- Return five scores and mean to two decimals.
- Identify material defects with affected elements and native evidence, ordered by user impact.
- Give the smallest corrections that would materially improve the result.
- State verification limits and explicitly answer whether this judge reaches 9.00.

Target is every enabled independent judge at least 9.00 on the same frozen List artifact, without averaging judges together. Run at most five List rounds including this pass's initial baseline. Stop earlier if all judges pass. Judges remain GPT 6.1 Sol with medium reasoning, Claude Opus 5.5, and Muse Wonder Full 1.0 via its available CLI provider. Report actual final scores even if the cap is reached below target. Native functional and latency qualification remain separate from aesthetic scores.
