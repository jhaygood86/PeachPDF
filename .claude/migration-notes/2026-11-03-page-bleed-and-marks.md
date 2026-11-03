# `@page` `bleed` and `marks` now grow the PDF sheet

Before, `bleed` and `marks` in an `@page` rule were dropped as unknown descriptors and every PDF page was exactly the page box. Now a page that declares them (or whose `marks` is `crop`, which implies a 6pt bleed) is a larger PDF page: the page box is its `/TrimBox`, the bleed adds a `/BleedBox`, and the `/MediaBox` also holds the marks. Layout and pagination are unchanged. A document that already had these descriptors in its CSS for another renderer will now come out with larger pages and marks.

`PeachBrackets.DocumentPage` gained two optional trailing components, `BleedPoints` and `MarksPoints` (both 0 by default), and `PeachDrawing.PDF.PdfComposerPage` gained `SetPrintBoxes`.
