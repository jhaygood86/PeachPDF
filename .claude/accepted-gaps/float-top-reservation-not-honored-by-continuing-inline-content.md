# `float: top`'s reservation is not honored by inline content continuing from an earlier page

`float: top`'s band-start reservation (`HtmlContainerInt.TopFloatAreaHeightsBySlot`) is enforced by a
floor clamp in `CssBox.ResolveBlockChildOffset` — the block-level child-placement path: `top` is
never allowed to land inside the reserved strip at the head of a page that has one, whatever computed
it (ordinary block-flow arithmetic, a resumed pass, a forced or margin-truncated break all funnel
through the same return point). That correctly pushes a block-level box below the reserved strip
whenever it would otherwise be the first thing on that page.

It does not cover inline content. A paragraph that started on an earlier page and is still flowing
(`CssLayoutEngine.CreateLineBoxes`) when it crosses into a page that reserves top space is not
clamped — its lines continue at their ordinary, continuous document-Y position (pages tile with no
gap, so a line's Y is simply the previous line's bottom) and may render into the reserved strip
instead of below it.

In practice this only matters for a tall run of text that happens to cross a page boundary that also
carries a top float — the common case this feature targets (a figure or callout placed at a
block-level position between paragraphs, [docs/html-css-support.md](../../docs/html-css-support.md)'s
`float` row) already works correctly via the block-level clamp, since the float itself and its
siblings are ordinary block boxes.

`BlockConstraint.BandStartInset`/`RemainingBlockSize` carries the same narrower-than-ideal shape as
`BandEndInset` already does for `float: footnote` (see
[footnote-reservation-not-honored-by-every-4-3-mover.md](footnote-reservation-not-honored-by-every-4-3-mover.md)):
honored only for the live pass's own slot, not for a hypothetical slot's own §4.3 fit-check.

Fixing this needs the equivalent floor applied to a fresh line's starting Y inside
`CreateLineBoxes`, mirroring the block-level clamp.

Tracked as [#1273](https://github.com/jhaygood86/PeachPDF/issues/1273).
