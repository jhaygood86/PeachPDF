# `@page` `bleed` / `marks`: grow the PDF media box, not the drawing coordinates

Load-bearing idea: the canvas origin stays at the page box's top-left corner, and the PDF `/MediaBox` is written with a **negative origin** (`[-s -s W+s H+s]`), with `/TrimBox` = the page box and `/BleedBox` = page box + bleed. Everything that already works in page-box coordinates (links, form fields, bookmarks, the PDF writer's y flip, the `PdfPageSource` readers) is untouched; only what is drawn at negative coordinates (the canvas fill into the bleed, the marks) lands in the extra area. The alternative, shifting the canvas by the extent, would have needed every annotation, destination and form field translated too.

Trap: the engine's graphics flip y by the media box *height* (`PdfPage.Height` reads the MediaBox), so growing the MediaBox while a page is still being drawn or while links/outline destinations are computed would put them off by the extent. `PdfComposerPage.SetPrintBoxes` therefore only records the request and the composer writes the boxes in `Save`, after `FinishPages`; `WidthPoints`/`HeightPoints` are cached at `AddPage` so they stay the page box's after that.

Marks geometry (not specified numerically by css-page-3, chosen to match Prince's look): a 3pt gap outside the bleed, crop strokes 12pt long at hairline width, registration targets (3pt radius circle, 6pt arms) centred on each edge midpoint, so the marks strip is 15pt. Crop marks without a declared bleed imply the 6pt bleed (§13). The canvas background extends to the bleed edge; the `@page` box's own background/border do not.

Verified by rasterizing the showcase with PDFium and MuPDF at the sheet size and by `PagePrintBoxesTests` (box values).
