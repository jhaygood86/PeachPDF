# CSS Grid item stretch now honors `box-sizing: border-box`

**Landed:** 2026-08-24 — CSS Grid `gap` renders far wider than the specified value (#811)
**Doc section:** docs/html-css-support.md § [Grid](../../docs/html-css-support.md#grid)

A stretched grid item (the default `align-items`/`justify-items: stretch`, or an explicit one) with
`box-sizing: border-box` and non-zero padding/border previously rendered exactly that padding+border
narrower/shorter than its actual track — `CssLayoutEngineGrid.cs`'s item-placement and auto-row-height
measurement code, and the shared `ItemContentCommit.CommitLayout` final layout pass it and
`CssLayoutEngineFlex` both go through, all assigned a *content-space* size to the item's `Width`/`Height`
regardless of its box-sizing, which this engine's own box-sizing contract only treats as correct for
`content-box`.

The shrunk item left the grid container's own background visible around it — for a document using the
`gap` + background-color divider technique with `box-sizing: border-box` (as most authors set globally, via
`* { box-sizing: border-box }`) and padded cells, this looked exactly like an oversized `gap`, even though
`gap`'s own value was rendered correctly throughout. A document relying on this combination will now see
each stretched item fill its track/row exactly (as an unstretched, explicitly-sized border-box item already
did), so any divider or background peeking through a gap shrinks down to the actual specified `gap` size.

`CssLayoutEngineFlex`'s own placement-phase stretch/shrink-to-fit sizing (`ResizeItem`,
`ShrinkColumnItemToContentWidth`) has the same content-space-only assumption and is not fixed by this
change — a `display:flex` container with `box-sizing:border-box` padded items can still size items smaller
than intended. Tracked separately: see
[flex-stretch-shrink-to-fit-sizing-ignores-box-sizing-border-box.md](../accepted-gaps/flex-stretch-shrink-to-fit-sizing-ignores-box-sizing-border-box.md)
and [#815](https://github.com/jhaygood86/PeachPDF/issues/815).
