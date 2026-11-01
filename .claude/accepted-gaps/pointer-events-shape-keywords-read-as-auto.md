# SVG `pointer-events` shape keywords are read as `auto`

`pointer-events` is parsed with its full SVG keyword set (`visiblePainted`, `visibleFill`, `visibleStroke`,
`visible`, `painted`, `fill`, `stroke`, `all`), but the hit test works on HTML box fragments. An inline
`<svg>` is one box, so a shape inside it is not a separate hit target and every one of these keywords
behaves as `auto`; only `none` and `auto` change anything. Hit-testing a point against a shape's fill and
stroke geometry (and `visibility`/paint state) would need the SVG renderer to expose per-shape geometry
to the hit test, which it does not today.

Only `none` can be authored to remove a whole `<svg>` from hit testing; a link inside it is still written
(the SVG `<a>` link path does not consult `pointer-events` either).

Not tracked as a separate issue yet beyond #706, which this change partly closes.
