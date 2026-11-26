# A page's bleed and printer's marks are not scaled with the content

`HtmlDocumentOptions.PageScale` (`PdfGenerateConfig.PixelsPerInch`, `ShrinkToFit`, `ScaleToPageSize`, `MinContentWidth`) lays a document out on a sheet
`scale` times the size and draws every page at `1 / scale` through `PdfComposerPage.SetContentScale`. The sheet, the margins and an `@page` rule's `size`,
`margin`, border and padding are multiplied, so the page that results has the size and margins the document asked for.

A page's **bleed and printer's marks** are not multiplied. `PagePainter` draws the marks at `BleedPt * LayoutUnitsPerPoint` in the layout space, which the
scale then shrinks, while `DocumentPage.BleedPoints`/`MarksPoints` (what `SetPrintBoxes` writes as the `/TrimBox`, `/BleedBox` and `/MediaBox`) stay the
physical values. At a scale other than 1 the marks therefore sit closer to the trim edge than the boxes say.

Print production content is laid out at its CSS size, so this was left rather than multiplied through the marks painter (the mark gap, crop length and cross
radius are constants in points as well). If a use appears: multiply `BleedPt`, the gap and the lengths by `container.PageScale` in `PagePainter`.
