# A `transform` on a non-replaced inline box is applied

CSS Transforms 1 §2 limits `transform` to transformable elements: block-level and atomic inline-level boxes, and the
table-internal display types. A non-replaced `display: inline` box (a plain `<span>`, `<a>`, `<em>`) is not one,
so the property has no effect on it. PeachPDF applies it: `DerivedStyle.IsTransformed`/`HasTransform` read only
the computed `transform` value, and `FragmentPainter.PaintFragment`, `DomUtils.IsStackingContextBox` and
`FormsContainingBlockForFixed` all act on that.

Measured against headless Chrome (`<span style="transform:rotate(45deg)">inline span</span>`, 14pt text): Chrome's
bounding box is 66.9 x 15.8pt, unrotated; PeachPDF draws the text and background rotated.

It was hidden while the whole-box rectangle of an inline-flowed box was wrong (such a span was pivoted around the
page origin and so drawn off the page, which is also wrong): it became visible once that was fixed. The test has to
sit on the computed value, so paint, stacking context and containing block agree, not on the painter alone.

Filed as [issue #1684](https://github.com/jhaygood86/PeachPDF/issues/1684).
