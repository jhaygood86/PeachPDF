# Flex item stretch/shrink-to-fit now honors `box-sizing: border-box`

**Landed:** 2026-08-25 — flex item stretch/shrink-to-fit sizing ignored `box-sizing: border-box` (#815)
**Doc section:** docs/html-css-support.md § [Flexbox](../../docs/html-css-support.md#flexbox)

A flex item resized by `CssLayoutEngineFlex.ResizeItem` (main-axis growth/shrink resolution, both
`flex-direction: row` and `column`) or `CssLayoutEngineFlex.ShrinkColumnItemToContentWidth` (cross-axis
shrink-to-fit under `flex-direction: column` with a non-`stretch` `align-items`/`align-self`), with
`box-sizing: border-box` and non-zero padding/border, previously rendered exactly that padding+border
narrower/shorter than its actual allotted size — both methods assigned a *content-space* size to the
item's `Width`/`Height` regardless of its box-sizing, which this engine's own box-sizing contract
(`CssBox.ActualBoxSizeIncludedWidth`/`ActualBoxSizeIncludedHeight`) only treats as correct for
`content-box`.

This is the flex analog of the identical CSS Grid bug fixed by #811 — see
[2026-08-24-grid-item-stretch-now-honors-box-sizing-border-box.md](2026-08-24-grid-item-stretch-now-honors-box-sizing-border-box.md).
A document relying on `flex-grow`/`flex-shrink` or column-direction shrink-to-fit sizing together with
`box-sizing: border-box` (as most authors set globally, via `* { box-sizing: border-box }`) and padded/
bordered flex items will now see each affected item fill its resolved main-axis size, or shrink-to-fit
its content, at its full outer footprint — matching how an explicitly-sized (non-grown/shrunk) border-box
item already rendered.
