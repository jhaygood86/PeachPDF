# A gradient/pattern fill silently inherited the previous solid fill's alpha

While verifying `background-clip: text` (issue #1117) by rendering the feature's own repro and
rasterizing it - not by any unit test - two consecutive gradient-clipped headings showed only the
first one. Structural (`RecordingGraphics`-mocked) tests couldn't have caught this: the mock never
exercises real `PdfGraphicsState`/content-stream generation.

**Root cause**: `PdfGraphicsState.RealizeBrush`'s gradient/pattern-brush branch never realized its own
fill alpha (`/ca`) unless the gradient itself had a semi-transparent stop (`pattern.AlphaExtGState !=
null`). A solid-color fill always realizes `/ca` via `RealizeFillColor` and tracks it in
`_realizedFillColor`, but a plain (fully-opaque) gradient/pattern fill emitted neither an explicit `/ca`
nor reset the tracked value - so it silently inherited whatever `/ca` the *previous* solid-color fill
left active in the PDF graphics state. `Realize(XImage, ...)` already had to guard against this exact
class of leak for images (`_gfxState.RealizeNonStrokeTransparency(1, _colorMode)`, with a comment
explaining why), and the stroke side of `RealizeBrush` was already fixed for the identical leak
(issue #134 - `pen.Brush != null ? 1.0 : color.A` in `RealizePen`) - but the fill side's gradient
branch had no equivalent.

`background-clip: text` is what actually exposed this in practice: it commonly pairs with `color:
transparent` (routine `RealizeFillColor` call with alpha 0), and paints a gradient/pattern fill
*of the next element* right after - the exact repro shape. Any other layout that happens to draw a
transparent-alpha fill immediately before an opaque gradient/pattern fill would trip the same bug,
regardless of `background-clip`.

**Fix**: `RealizeBrush`'s gradient/pattern branch now explicitly resets non-stroke transparency to
opaque (`RealizeNonStrokeTransparency(1, colorMode)`) whenever the pattern has no `AlphaExtGState` of
its own - mirroring `RealizePen`'s already-established fix on the stroke side, and reusing the exact
`RealizeNonStrokeTransparency` dedup-checked reset `Realize(XImage, ...)` already uses for the same
purpose.

**Do not "fix" this by asserting a blanket "no `/ca 0` anywhere in the document"** - a genuinely
transparent solid fill (e.g. the `color: transparent` text `background-clip: text` pairs with) still
legitimately needs its own `/ca 0` object elsewhere; the regression only concerns the *pattern* fill's
own realized alpha at the point it is drawn.

Verified by rendering the two-heading repro through `PdfGenerator` and rasterizing with both PDFium
and MuPDF (both agreed, before and after), and with three new regression tests in
`GradientFillAlphaTests.cs` (mirroring `SvgGradientStrokeAlphaTests.cs`'s own issue-#134 shape):
resolving the `gs` resource immediately preceding a pattern fill's own `scn` to its backing
`PdfExtGState` object and asserting `/ca` is never `0` there. Confirmed meaningful by temporarily
reverting the fix and re-running - the alpha-leak test failed exactly as expected (`Assert.NotEmpty`
on the resolved fills came back empty, since without the fix no `gs` is emitted before the pattern
fill at all), the other two passed unaffected (they check a semi-transparent gradient's own
pre-existing `AlphaExtGState` path, which this fix does not touch).
