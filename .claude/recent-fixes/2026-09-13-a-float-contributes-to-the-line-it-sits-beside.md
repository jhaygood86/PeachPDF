# A float contributes to the line it sits beside in the intrinsic-width walk

Issue #1033, and the gap file
`.claude/accepted-gaps/a-float-does-not-contribute-to-the-line-in-the-intrinsic-walk.md`, now deleted.

A float is taken out of the flow but is still placed *beside* the inline content of the block it is in
([CSS 2.1 §9.5](https://www.w3.org/TR/CSS21/visuren.html#floats)), so for max-content sizing its width
**adds** to that line. `CssBox.GetMinMaxSumWords` saw only that §9.7 blockifies it — which
`StartsNewLine` reads correctly — and had it open a *competing* line, measuring `max(text, float)`
where the answer is `text + float`.

Float widths at `font: 16px monospace` (one character advances 6.5977pt):

| markup inside `<div style="float:left">` | before | after |
|---|---|---|
| `XY <span style="float:left">ZZZZ</span>` | 26.3906 | **46.1836** |
| …the same under `white-space: nowrap` | 26.3906 | **46.1836** |
| `<span style="float:left">AAA</span><span style="float:left">BBB</span>` | 19.7930 | **39.5859** |
| `<p>AB CD</p><span style="float:left">EF</span>` | 32.9883 | 32.9883 (unchanged, and must be) |

46.1836 is `"XY" + space + "ZZZZ"`, the same seven characters as one plain run. The `nowrap` row was a
regression from issue #1017 — the old predicate excused any `nowrap` box from opening a line, so the
float was summed onto it for a reason that had nothing to do with floats.

## The load-bearing idea: the walk needed to know it was in an IFC, and the marker for that was missing

"A float adds to the line" is **not** unconditionally true — a float between two block-level siblings
is on neither one's line, which is Blink's own split between `ComputeInlinePreferredLogicalWidths`
(floats add) and `ComputeBlockPreferredLogicalWidths` (floats accumulate separately and only `max`).
The gap file said this needed "the walk to know more than it does today", and it did, but only one
fact: **which of a box's block-level children are §9.2.1.1 anonymous wrappers around its own inline
run.** `DomParser.CorrectInlineBoxesParent` generates exactly one of those around `XY ` here, because
the blockified float makes `ContainsVariantBoxes` true — and nothing on the box recorded that it was
one. `CssBox.IsInlineRunWrapper` now does, set at the single place that creates them, and two
predicates read it:

- `FloatsShareTheLine(box)` — every in-flow child is inline-level content (an inline box or one of
  those wrappers), so a float child is beside it. Vacuously true for a box whose children are all
  floats, which is right: they do sit side by side.
- `SharesItsLineWithAFloat(box)` — the same question from the wrapper's side, so `StartsNewLine`
  returns false for it and the line is *not* closed at the float. **This is what keeps the space**:
  without it the wrapper's epilogue hangs `XY `'s trailing space (css-text-3 §4.1.2) and the answer is
  39.5859, one space short, because the wrapper wrongly believes its line ended.

The float itself is then measured in isolation via its own top-level `GetMinMaxWidth` and added, which
is the mechanism `IsFlexRow` already uses per flex item — no new machinery, and the same
`trailingSpace = 0` that branch does (see
[.claude/invariants/intrinsic-a-trailing-space-may-only-hang-where-a-line-is-known-to-end.md](../invariants/intrinsic-a-trailing-space-may-only-hang-where-a-line-is-known-to-end.md)).
`min` takes `Math.Max` with the float's own min-content and ends the unbreakable run at it, because a
float is a wrap opportunity and an unbreakable unit, which is what Blink does too.

## What running it turned up

- **Measuring the box in isolation forces a decision about `paddingSum`, and every answer but one is
  a different bug.** `paddingSum` is a running total kept separate from `maxSum` and combined across
  boxes by `Math.Max` rather than addition, so the isolated measurement's decoration cannot simply
  ride onto the line: that ADDS what the recursive descent would have MAXed. `GetMinMaxWidth` gained a
  `decoration` out-parameter for exactly this, and the branch splits the decoration back out and folds
  it in the way the descent folded it — **reproducing the existing accounting exactly**, so the change
  is only about *where the float's content lands*. All 115 showcases are byte-identical after
  normalizing the four known randomness sources, which is the evidence that it really is unchanged.
- **The two alternatives were built and measured first, and both shipped a different defect.** Adding
  the float's full **outer** width to the line makes an absolute shrink-to-fit box one decoration too
  wide, because `GetBoxWidth`'s `position: absolute` branch reads the walk's result as a *content*
  width while the table engine and `GetBoxWidth`'s own float branch read it as outer: Acid2's
  `.smile div div` went to 108pt against its real 90pt and drew over the mouth beside it. Fixing that
  consumer *and* making `paddingSum` accumulate down a containment chain (both genuinely wrong today)
  then widened a third defect — the padding added is not the winning line's own — on two more
  showcases. All three are one tangle, now tracked as **#1040** and recorded in
  [.claude/accepted-gaps/intrinsic-padding-total-is-not-the-winning-lines-own-padding.md](../accepted-gaps/intrinsic-padding-total-is-not-the-winning-lines-own-padding.md)
  with the measurements. **The visible cost of leaving it** is that two floats that each declare
  padding lose one of the two paddings from the line they share; without padding the same markup is
  correct.
- **A float's declared `width` had to be handled in the branch.** Taking the isolated path leaves the
  child-explicit-width fold further down the loop, and `float: left; width: 200px` is the ordinary
  case. Unlike that fold's *floor*, this **replaces** the measured width — a non-auto width IS the
  used width (CSS 2.1 §10.3.5). That is asserted through an auto table column rather than a
  shrink-to-fit box's used width, because `CssLayoutEngine.GetLargestChildWidth` separately maxes in
  every descendant's own width and an overflowing run wins there regardless.

## Deliberately not done

**Placement.** The box now reserves the room; layout still draws a float that *follows* inline content
at the block's left edge, on top of that content. Tracked as #1038 and recorded in
[.claude/accepted-gaps/a-float-after-inline-content-is-placed-on-the-next-line.md](../accepted-gaps/a-float-after-inline-content-is-placed-on-the-next-line.md) —
it is a box-tree change (not generating the wrapper at all, which `DomUtils.ContainsInlinesOnly`,
`CssBox.LayoutContents`' inline-vs-block dispatch and `CssLayoutEngine.FlowBox` all have to absorb),
not a placement tweak. Two floats with nothing between them are already placed side by side, so the
measurement fix is fully visible there.

**The anonymous wrapper itself.** Browsers generate none — an out-of-flow box does not make a block
container's children non-inline — and removing it would make both this and #1038 fall out. Same
reason: it is a box-tree change, and this issue was about the measurement.

## Evidence

`IntrinsicWidthWalkTests`, 36 fixtures. Six are new: the float beside inline content (plain and
`nowrap`), the geometry consequence, consecutive floats, the block-level-sibling contrast case, the
float's declared width and margins (including its padding, and the declared width replacing rather
than flooring), and an out-of-flow/`display:none` sibling not disturbing the gate. Five fail against
the merge base; the sixth is the contrast case, which must keep passing and does.

Full suite green on net8.0 (11,077 passed / 0 failed / 9 skipped), solution rebuild with 0 warnings,
100% diff coverage on the changed production lines, all 115 showcases byte-identical.
