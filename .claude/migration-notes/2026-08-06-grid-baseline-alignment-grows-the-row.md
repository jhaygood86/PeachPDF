# CSS Grid baseline-aligned rows now grow to fit the baseline group's full extent

**Landed:** 2026-08-06 — Fix open alignment issues (#280)
**Doc section:** docs/html-css-support.md § [Grid limitations](../../docs/html-css-support.md#grid)

`CssLayoutEngineGrid.AlignRowBaselines` shifts every baseline-aligned item in a row down so they share a
common first baseline, but previously did so within the row's already-finalized natural-content height.
An item whose descent below its own baseline was disproportionate to its natural height (relative to the
item that set the row's max-ascent) could have its bottom edge shifted past the row — and so past the grid
container's own bottom — overlapping whatever followed.

The row is now grown, during track sizing (before positions are finalized), to at least
`max-ascent + max-descent` across the row's baseline-participating items (CSS Box Alignment 3 §9.3). A
document relying on `align-items: baseline`/`align-self: baseline` in Grid with items of mismatched
ascent/descent proportions (e.g. differing `line-height`, or padding on one side of the baseline) will now
see the row/container grow enough to contain every item fully, rather than a possible visual overlap into
the following content. Ordinary text with a normal `line-height` was already unaffected (the common case
already fit).
