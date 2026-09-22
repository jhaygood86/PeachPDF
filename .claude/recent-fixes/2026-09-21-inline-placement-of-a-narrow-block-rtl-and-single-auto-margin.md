# Where a narrow in-flow block sits: rtl end-alignment and the single `auto` margin (#1245)

A block narrower than its container was always placed at `ContainingBlock.ClientLeft + ActualMarginLeft`, with
`margin-right` the implicit shock absorber. Correct for `ltr` only, and it also hid a second gap: when exactly
one margin is `auto` the engine resolved it to 0, so `margin-left: auto` never did anything.

## The load-bearing idea

Anchor an `rtl` block to the containing block's **right** edge instead of solving for a left edge:
`x = contentRight - marginRight - borderBoxWidth`. That one expression gives both of §10.3.3's rtl results - a
narrower box end-aligns, an over-constrained one ignores `margin-left` and overflows toward the start - and is
*algebraically identical* to `contentLeft + marginLeft` for a box that fills its container, so no full-width
rtl document moves. Nothing had to be branched on "narrower" or "over-constrained".

Both places that write a block's left edge go through `CssBox.ResolveBlockInlineStart`:
`CommitBlockChildOffset` and `ResumeInTheNextFragmentainer` (a multicol translation that re-derived the same
`ClientLeft + ActualMarginLeft` and would have snapped an rtl box back to the left on the next column).

It reads `CssLayoutEngine.ContentRightOf`, the same page-aware right edge `GetBoxWidth` measured against, rather
than `ClientRight`, so a per-page `@page` measure change cannot leave the width and the anchor disagreeing.

## Single `auto` margin

`FreeInlineSpace` is what both-`auto` already computed (containing content width less the used border box,
after the max/min clamps), now unclamped and shared. Both-`auto` is `max(0, free) / 2`; single-`auto` is
`max(0, free - otherMargin)`. The auto-width branch returns 0 slack unless a `max-width` narrows it, so a plain
`margin-left: auto` block still fills. Restricted to `IsInFlowBlockLevel` (`FillsContainingBlockWidth` plus
`display: block|list-item`): a float, a table, a flex item and an inline box keep 0, because each has margin
rules of its own and none was verified.

## Found by running it

- **The single-`auto` gap was real and unreported.** It came from the plan's own hypothesis: `<hr align=right>`
  has to map to `margin-left: auto; margin-right: 0`, and the first failing test showed that mapping would have
  been inert. `ltr` failed too, not just `rtl`.
- **The existing suite pinned neither behaviour.** 12,976 tests passed before and 12,993 after (the 17 new
  ones); no rtl fixture in the suite has a narrower-than-container block, which is exactly why the bug survived.
  Chrome 153 measured all 17 cases (`ltr`/`rtl`, single and both `auto`, over-constrained, padded container,
  relative offset) and agrees with every expectation to within its 1/64px rounding.
- The `<hr>` half of each pairing needed no code: `CssBoxHr` reaches `PlaceAsBlockChild`, hence the same commit.

## Not done, and why

- **Tables** (#1251, [accepted gap](../accepted-gaps/table-inline-placement-rtl-and-single-auto-margin.md)): an rtl
  table stays at the left and `margin-left: auto` does not right-align one. Chrome 153 measured; confirmed by a
  throwaway probe test that was deleted rather than committed.
- **Vertical writing modes**: the inline axis is the other one there, and `frame.BlockStartIsRight` already owns
  the block axis. Gated out by `WritingMode is HorizontalTb`.
- **Block-level replaced elements** (a `display: block` `<img>`): its synthetic wrapper is inline-level, so
  `IsInFlowBlockLevel` is false and it keeps the old placement. Not measured.

## Evidence

Full net8.0 suite green (12,993, +17), 0 warnings on a solution rebuild, 100% diff coverage on 24 lines. All 150
existing showcases rasterize pixel-identical to the PR 1 baseline (none contains a narrower-than-container block
in an rtl container, which is why the new `block_inline_placement` showcase exists); the new one agrees between
PDFium and MuPDF.
