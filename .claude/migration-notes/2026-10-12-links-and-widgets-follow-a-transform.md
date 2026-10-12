# Links and form widgets over a transformed element follow it

**Before:** `<div style="transform: rotate(30deg)"><a href="...">link</a></div>` painted the link rotated but wrote its
link annotation (and an `<input>`'s AcroForm widget) at the rectangle layout gave it, so the clickable area and the visible
element no longer coincided. The same held under `scale()`, `translate()`, `perspective` and `transform-style:
preserve-3d`, and for links in an inline SVG or a running element.

**Now:** the annotation's `/Rect` is the bounding box of what was drawn, so it moves, scales and turns with the element
(css-transforms-1 "Transform Rendering"). A link whose drawn shape is not an axis-aligned rectangle (a rotation or skew, a
perspective trapezoid) also carries that shape as `/QuadPoints` when the file declares PDF 1.6 or later (the default PDF 2.0
does; the historical 1.4 header does not). A widget's appearance is rotated and scaled with its control for an affine
transform; under `perspective` it is the upright bounding box. A link whose box is wholly off the page is no longer
written. A document that moved an element with `transform` and relied on its link or field staying at the layout
position needs that position back (use `position`/`margin` to move it instead).
