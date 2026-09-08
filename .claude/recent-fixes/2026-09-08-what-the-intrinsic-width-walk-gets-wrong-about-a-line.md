# Four things the intrinsic-width walk got wrong about what a line is

`CssBox.GetMinMaxWidth`/`GetMinMaxSumWords` is a flat walk carrying ONE running line total across a
whole subtree. Four separate defects all trace to that shape, and all four show up as a
shrink-to-fit box or an auto table column sized for something other than what it draws.

- **A `<br>` ends a line, so max-content is the widest line, not the sum.** The walk had no notion
  of a line ending mid-subtree, so a cell holding `H-095<br>H-095-A1 HOSE` measured as the whole run
  and took width from its neighbour. `widestLine` now carries the widest line CLOSED by a forced
  break anywhere in the subtree, and `maxWidth` is the max of that and the still-open total.
- **A flex ROW's items sit side by side.** One running total cannot express that: a `<br>` inside
  one item terminated the total the previous item had contributed to, so a row measured as
  "first item + the second item's first line". A row is now measured as the sum of its items, each
  measured in isolation, with the gaps counted (css-align-3 §8).
- **Hanging white space was counted at both ends of a line.** css-text-3 §4.1.2 removes a
  collapsible space at the start of a line and at the end. Counted, measurement claimed 2.6pt per
  line more than the box draws.
- **An inline child's horizontal margins sit ON the line.** Uncounted, a shrink-to-fit box measured
  80pt narrower than the run it had to hold and wrapped text that fits.

Plus one that cannot wrap: a `white-space: nowrap`/`pre` box has no smaller size to offer, so its
min-content IS its max-content (CSS 2.1 §17.5.2). Measured as its longest word it is far smaller,
and an auto table column sized from that stays narrower than the run it holds.

## What running it turned up

- **`trailingSpace` was not reset alongside the rest of the per-line state.** `StartsNewLine` resets
  `maxSum`, `paddingSum` and `atLineStart`; the hanging space of the previous line survived. It is
  now reset with them. Worth being precise: I could not build a fixture where it changes the result,
  and the reason is structural — the stale value only ever reaches
  `widestLine = Math.Max(widestLine, maxSum - trailingSpace)` against a running maximum that already
  dominates the freshly-reset `maxSum`. Fixed for consistency, not because a document was wrong.
- **The css-text-3 citations were off by a subsection.** The comments said §4.1.1 and §4.1.3;
  both rules (leading and trailing collapsible spaces on a line) are §4.1.2, Phase II: Trimming and
  Positioning. Checked against the spec text rather than from memory.
- **A `float` in a full-width container is not shrink-to-fit** and says nothing about this walk — it
  stretches. The first version of the inline-margin fixture used one and passed against the merge
  base. `position: absolute` (CSS 2.1 §10.3.7) and an auto table cell are the shapes that actually
  exercise it.

## Evidence

`IntrinsicWidthWalkTests`, five fixtures asserting laid-out geometry: three fail against the merge
base (both forced-break cases and the inline-margin one), one is the widest-vs-first contrast case,
one guards the forced-break-first shape. Full suite green on net8.0, 0 build warnings.
