# Flex cross-axis auto margins (css-flexbox-1 §8.1, §9.4 step 11)

`CssLayoutEngineFlex` distributed **main-axis** auto margins (justify-content's "Phase 7") but not **cross-axis**
ones, so `margin-left: auto` on a `flex-direction: column` item - or `margin-top: auto` on a `row` item - left the
item at the start edge. Publicly that was `column.Item().Width(100).AlignRight()` doing nothing while the same call
on a `Row` item worked (found while fixing
[declarative table-cell alignment](2026-09-21-declarative-text-alignment-and-table-cells.md); the accepted-gap note
it produced is deleted with this change).

## The load-bearing ideas

**An auto margin is not the number `ActualMarginLeft` returns for it.** The flex engine reads margins through
`CssBox.ActualMargin*`, and for a horizontal auto margin that is whatever *block layout* resolved
(`CssLayoutEngine.ResolveAutoHorizontalMargin`, centring against the containing block) - but only when **both**
sides are auto; `GetActualMarginLeft` returns 0 as soon as the other margin is a value. So a one-sided
`margin-left:auto` happened to read 0 and looked fine, while `margin: 0 auto` read as "130pt on each side of a 40pt
item" and would have made its line the container's whole width. Everything the flex algorithm sees now goes through
`CrossMarginBefore/After`, which return 0 for an auto margin (§9.4: "auto margins are treated as zero" while sizing).
Three call sites had this: line cross size (`ComputeLineCrossSize`), Phase 4b's available width, and
`ComputeCrossOffsets`. The test that tells the two readings apart is a **multi-line column with `margin: 0 auto`**
(`Column_MultiLine_BothAutoMargins_...`); every single-line fixture passes either way, because a single line is as
wide as the container and the resolved margin happens to equal the free space.

**The offset arm bypasses `align-self` and the stretch branch entirely.** §9.4 step 11: positive free cross space is
shared equally among the item's auto cross margins; an item that overflows its line ignores them and stays at the
block-start edge. Implemented as one arm at the top of `ComputeCrossOffsets`' per-item loop that `continue`s, so the
existing `flex-start`/`center`/`stretch`/baseline arms - and the border-box stretch arithmetic and the "re-read the
size after the stretch branch" invariant that live in them - are not touched. An item with an auto cross margin is
never stretched, which falls out of skipping that arm.

**It is deliberately independent of `wrap-reverse`.** An auto margin names a *physical* side
(`CrossBefore`/`CrossAfter`, from writing-mode only), so `margin-bottom:auto` holds a row item at the top of its line
whichever way the lines are stacked - where `align-self: flex-start` would, under `wrap-reverse`, send it to the
bottom. The [wrap-reverse invariant](../invariants/flex-wrap-reverse-reverses-two-things-and-they-compose.md) (the
swap lives in the flush arms and in the line stack, and applying it twice cancels it) is respected by not applying it
at all: `Row_WrapReverse_AutoMargin_NamesAPhysicalSide_NotFlexStart` pins both directions.

**RTL needed nothing.** The engine ignores `direction` entirely (`ComputeAxisMapping` passes `DirectionMode.Ltr`),
and an auto margin is physical anyway - `margin-left:auto` pushes right in either direction - so there is no
direction-dependent case to get wrong; tests pin that an `rtl` container does not mirror it. (The retired gap note
said RTL "has its own invariants"; there is no flex/RTL invariant file.)

**An auto-margined column item must shrink to fit-content.** Phase 4b (`ShrinkColumnItemToContentWidth`) only shrank
non-stretch alignments, because `align-items: normal` (the default) is stretch and stays full-width. An auto cross
margin means "not stretched" whatever the alignment says, so the shrink now also runs for it - otherwise
`column.Item().AlignRight().Text("x")` (no `Width`) is column-wide with nothing for the margin to absorb.

**Auto-margined items leave the baseline group.** They are placed by their margins, so they no longer feed
`maxBaseline`; a much taller auto-margined item would otherwise push the rest of the group down to meet its baseline.

## What running it (not just reading it) found

Each part was neutralized in turn to prove the tests are load-bearing: the offset arm -> **25** tests fail; Phase 4b's
condition -> 1; the auto-margin-reads-as-zero helpers -> **0** on the first fixture set (the multi-line `margin: 0 auto`
test above was added because of it, then confirmed to fail); the baseline exclusion -> 0 with empty boxes, then 1 once
the fixture used real text with a much larger font. Two of those four "passed" the first time - the fixtures
were too easy, not the code right.

## Not changed

Grid, and the intrinsic-size (`min-content`/`max-content`) walk of a flex container, are untouched. Negative free
space follows the spec's overflow rule (start edge) rather than centring an overflowing item.

## Evidence

- `FlexboxIntegrationTests` +36 cases counting theory rows (row, column, multi-line row and column, `wrap-reverse` both margins, RTL row and
  column, both-sides-auto centring, per-item resolution, every `align-self` overridden, filling-line and overflow,
  baseline group) including the requested stretch regression guards (row and column: an item without an auto margin
  still stretches, the same item with one does not). `DeclarativeApiIntegrationTests` +5 (`AlignLeft/Center/Right` on a
  column item, auto-width column item, and the report's column-item-in-a-table-cell variant).
- New showcase section `14b — Auto margins (cross axis)` in the `flexbox` showcase, rasterized through **both PDFium
  and MuPDF**: identical, and every row as expected (bottom / centred / top-under-wrap-reverse / right / centred /
  `align-self: stretch` overridden with the sibling still stretched).
- Whole showcase set compared before and after: **145 of 150 identical**; the five that changed are the two invoice
  showcases and two declarative ones whose *footer* now honors `Alignment(Center)` (both from the alignment fix, not
  this one - no showcase calls `AlignLeft/Center/Right`), plus `flexbox` gaining the new section.
