# An inline-flowed box has no `Location`/`Size` — its geometry is its per-line rectangles

`CssBox.Location` and `CssBox.Size` are written by the block layout path. A box whose content this
engine flows into the surrounding inline formatting context — a non-replaced `inline` box, and an
`inline-block` whose content fits on one line (see `CssLayoutEngine.LaysOutAsAnAtomicBox` for the
shapes that instead get a real box) — never goes through that path, so **both stay at whatever they
were initialized to**. Its real geometry is `CssBox.Rectangles`: one border-box rectangle per line
box it appears on, which is what the painter already draws its background, border and decorations
from.

**So anything computing geometry for such a box must read its rectangles, never `CssBox.Bounds`.**

## The measured symptom

`CssBox.Bounds` for one of these is not empty, which is what makes it dangerous — an emptiness check
does not catch it. For `<span style="display:inline-block;overflow:hidden;padding:2pt 4pt;border:1pt
solid">inside</span>` it reads `(0, 0, 10, 6)`: the box's own padding and border, at the page origin.

`FragmentEmitter.OverflowClipOf` built the `overflow: hidden` clip from exactly that, so the clip came
out as a perfectly well-formed 8×4pt rectangle in the page's top-left corner, and every word inside
the box fell outside it. The box painted its border around **nothing at all**, in both PDFium and
MuPDF. `ClipSourceBoundsOf` is the fix: fall back to the union of `RectanglesOf(box)` for a box that
`CssBox.IsInline`.

## Why the test suite will not catch the next one

The box's *layout* geometry was correct throughout — every word inside the box's own rectangle, to
three decimal places, asserted by a test that passed before and after. Nothing about `CssBox`/
`CssRect` positions is wrong in this failure; only what paint was handed. A test for anything in this
area has to reach the content stream (or a rasterization) and check the **relationship** between the
clip and the content it governs — `InlineBlockOverflowClipPaintTests` does, since the broken clip has
real area and satisfies every "is a clip emitted" check.

## A second consumer: the whole-box rectangle and the transform's reference size

The overflow clip was the first reader of `Bounds` to go wrong; `BoxFragment.WholeBoxRect` was the second. It is
the `transform` pivot, the `clip-path` reference box, the filter/opacity raster extent and the 3D origin, and it
read `Bounds`, so a transformed `inline-block` was pivoted around the page origin and drawn off the page. And
`Size` is *not* assigned in the sense that matters for percentages: `ActualWidth`/`ActualHeight` of a flowed box are
its padding and border only (0 wide for a default-`inline` `<img>`), so `transform-origin: 50% 50%` and
`translate(%)` landed on the corner of a content-sized badge. Both now read the line rectangle:
`FragmentEmitter.ExtentOf` and `MarginBoxContentFragmentBuilder` through `RenderUtils.ClipSourceBoundsOf`, and
`CssValueParser.ReferenceWidth`/`ReferenceHeight` for the transform's own size. **A new reader of a flowed box's
position or size must take it from `Rectangles` the same way.** The measured symptom for the transform case is a
box that is laid out (its space is reserved in the line) and painted nowhere, or rotated about the wrong point.
