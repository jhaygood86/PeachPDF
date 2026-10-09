# An inline-flowed box's whole border box is its line rectangle (transformed inline-blocks painted nowhere)

Closes #1670 and #1683.

## What was wrong

An `inline-block` that fits on its line, and an inline replaced element such as `<img>`, is flowed into the
line instead of being laid out as a box, so `CssBox.Location` is never assigned and reads `(0, 0)` (see
[the invariant](../invariants/paint-an-inline-flowed-box-has-no-location-or-size-only-per-line-rectangles.md)).
`FragmentEmitter.ExtentOf` built `BoxFragment.WholeBoxRect` from `BoundsOf`, so for such a box it was a
rectangle of the right size at the page origin. The `transform` pivot is `ActualTransformMatrix.RebaseOrigin(
WholeBoxRect.X, WholeBoxRect.Y)`, so the box was rotated/scaled around the page's top-left corner and drawn
off the page. Layout was right (the space is reserved in the line), nothing was painted where it belongs: a
rotated `inline-block` square was missing, and in a longer page it showed up displaced up and left.

## The fix

`FragmentEmitter.ExtentOf` starts from `ClipSourceBoundsOf` (the union of the box's line rectangles for an
inline-level box that has any, `Bounds` otherwise) instead of `BoundsOf`, and
`MarginBoxContentFragmentBuilder` (running elements; footnote bodies share it but do not paint such a box at all, [issue #1690](https://github.com/jhaygood86/PeachPDF/issues/1690)) builds `WholeBoxRect` from
`RenderUtils.ClipSourceBoundsOf` the same way. That is the stand-in the `overflow` clip already used for exactly
this reason. These are the two builders of a `BoxFragment`; a new one has to do the same, since nothing makes the
choice for it (a single "border box of any box" helper was considered and not done: the two builders read
different sources, a live box and a geometry snapshot).

## Traps

- **It is not only `transform`.** `WholeBoxRect` is also the `clip-path` reference box, the raster union for
  filters/opacity groups and the 3D/projective origin. Rendered before and after on an inline-block: `clip-path:
  circle()`/`inset()`/`polygon()` were clipped against the origin and drew almost nothing, `perspective()`
  warped around the origin, `drop-shadow()` was right only by luck. All of them now sit on the box.
- **A passing equality test hid it.** `FragmentEmitterTests.FragmentRects_EqualTheLegacyPaintTimeCoordinates`
  asserted `WholeBoxRect.Y == Box.Bounds.Y` for every fragment, which encoded the stale `Bounds` for an inline
  span. It now expects the line-rectangle top for an inline box. A block-level-vs-inline-block comparison at
  the page origin passes on the broken code (both pivot at `(0, 0)`); the tests put the box well away from it.
- **Every inline-level box with line rectangles changes, not only atomic ones.** A plain wrapped `<span>` now has a
  `WholeBoxRect` equal to the union of all its line rectangles (it used to be a small rectangle at the origin).
  Only `clip-path`, filter/opacity raster extents and 3D read it for such a box, and all were wrong before;
  `WrappedInlineSpan_WholeBoxRect_IsTheUnionOfItsLineRectangles` pins it. Not examined: a `clip-path` on an inline span
  that crosses a page break, where the union is built from every line, not only the ones on this page.
- **The pivot size had the same cause and is fixed with it.** `transform-origin` and the `translate*()` percentages
  resolved against `ActualWidth`/`ActualHeight`, which for an inline-flowed box are its padding and border only
  (0 wide for a default-`inline` `<img>`; a content-sized `inline-block` badge was pivoted about a point near its
  top-left corner). `CssValueParser.ReferenceWidth`/`ReferenceHeight` read the line rectangle for such a box. With
  only the `WholeBoxRect` fix a transformed `<img>` or `Label` badge was *placed* right but rotated about the wrong
  point, so both are in the test theory (the content-sized one and the default-inline `<img>` each fail without it).
  Measured against Chrome after the fix, in both axes: a 40pt `<img>` rotated 45 degrees has its box centre where
  Chrome has it.
- **A transform on a plain non-replaced inline box is now visible**, where Chrome ignores it (it is not a
  transformable element). It used to be drawn displaced, about the wrong point, which is no more right
  ([issue #1684](https://github.com/jhaygood86/PeachPDF/issues/1684)).
- An identity transform was never affected (no matrix to rebase), which is why `rotate(0deg) scale(1)` drew.

## Evidence

`TransformIntegrationTests.Paint_TransformedInlineBlock_PivotsAroundItsPositionInTheLine` (span, span followed
by text, inline-block `<img>`, `scale`), `ClipPathPaintIntegrationTests.Polygon_OnAnInlineBlock_IsResolvedAgainstItsRectangleInTheLine`
and `MarginBoxContentClipTests.FlowedInlineBlock_WholeBoxRect_IsItsLineRectangle_NotTheUnassignedOrigin` fail on the
merge base. `perspective()` and filter extents have no assertion of their own: they read the same
`WholeBoxRect`, which the tests above pin, and were checked by rasterizing. The new `inline_block_effects`
showcase rasterizes identically in PDFium and MuPDF, and every existing showcase regenerates with byte-identical
page content streams before and after. The defect is present at `v0.9.21` (same `ExtentOf` line).

## Not done

An empty `inline-block` with a declared height hangs below the baseline instead of resting its bottom edge on
it, so it overflows its row's background and an `<img>` in the same line sits higher (seen while building the
showcase, which uses `vertical-align: top` to avoid it). That is
[issue #1307](https://github.com/jhaygood86/PeachPDF/issues/1307), a layout matter unchanged by this fix.
