# `backdrop-filter` now renders (it used to be ignored)

**Before:** `backdrop-filter` (and `-webkit-backdrop-filter`) was accepted and dropped: a frosted-glass card painted as its plain
translucent background, with the unblurred page showing straight through it. It also did not create a stacking context.

**Now:** the filter list is applied to whatever was painted behind the element, inside its border box, and the result is drawn under
the element's own background, clipped to its `border-radius`. See [Rasterized effects](../../docs/html-css-support.md#rasterized-effects)
and the `backdrop-filter` row for what counts as the backdrop (the nearest backdrop root, else the page over white paper) and where it is
not applied (a transformed element, or one under a transformed ancestor inside its backdrop root).

What a document author can notice: a page that already set `backdrop-filter` for other browsers now shows the effect, as a bitmap at the
raster resolution; the element is now a stacking context (a positioned or `z-index` child can no longer paint outside it, as with
`filter`); and a document targeting PDF/A-1 or PDF/X-1a/X-3 that uses it is rejected instead of silently rendering without it.

Verified at the previous release tag (v0.9.19): `docs/html-css-support.md` listed `backdrop-filter` under unsupported CSS features.
