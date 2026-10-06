# SVG `<foreignObject>` now renders (inline SVG)

Before: `<foreignObject>` and its HTML were dropped, so nothing painted (and a `<switch>` skipped it). Now, in an inline `<svg>` in an HTML document, the HTML inside is laid out as a block `width` wide and painted at (`x`, `y`), clipped to its `width`×`height`. A `foreignObject` with a missing/zero size still paints nothing. Standalone SVG is unchanged.
