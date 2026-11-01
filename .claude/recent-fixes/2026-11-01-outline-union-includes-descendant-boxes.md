# An inline's outline unions its non-inline descendants' border boxes (#1207)

`FragmentPainter.PaintBoxContent` collected only the outlined inline's own per-line rectangles. Blink's
`PhysicalBoxFragment::AddOutlineRectsForInlineBox` also feeds every non-inline box fragment inside the
inline (atomic inlines *and* block children and floats, not only atomic inlines as the issue first said)
into the same union; nested inline boxes only recurse, list markers are skipped, and out-of-flow boxes
count only when the inline is their containing block. CSS UI 4 §3.1 says only "minimum set of outlines
that encloses all the element's boxes" and that descendants "may affect" the position.

`AddOutlineDescendantRects` (FragmentPainter.Outlines.cs) walks the fragment's `Children` and adds each
`PrimaryRect`, fragmentainer-local like the line rectangles (never `CssBox` geometry). Two deliberate
choices: out-of-flow descendants are always left out (the containing-block exception is not modelled),
and a rectangle already inside a collected one is not added, so an ordinary outline stays on its
single-rectangle path instead of switching to the region painter for no change.

Evidence: `OutlineDescendantBoxTests` (the tall-box case fails with the call removed); engine 17023 and
PeachPDF.Tests green on net8.0. WebKit/Gecko sources were not compared.
