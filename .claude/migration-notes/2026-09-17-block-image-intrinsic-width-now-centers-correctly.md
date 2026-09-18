# `display: block` images/SVGs with no declared width now center correctly with `margin: auto`

Previously, a `display: block` `<img>` or inline `<svg>` with `margin-left: auto; margin-right: auto`
but **no** declared `width` — relying on its intrinsic pixel size instead — did not center: the
computed offset used a stale/zero width instead of the element's real size, so the element rendered
flush to its containing block's start edge (or at some other incorrect position) instead of centered.

An element with an explicit declared `width` was unaffected by this — that case was already fixed (see
the migration note for `text-align` no longer centering a `display: block` replaced element). This
closes the remaining, intrinsic-size half of the same auto-margin centering mechanism (CSS 2.1
§10.3.3, via §10.3.4 for a replaced element): `margin-left: auto; margin-right: auto` now centers a
`display: block` `<img>`/`<svg>` correctly whether or not it has a declared width.
