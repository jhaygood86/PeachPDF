# A no-break space drawn with the space glyph extracts as a space

Many fonts (Arial, Liberation Sans Narrow, the bundled Source Sans 3) map U+00A0 NO-BREAK SPACE to the same
glyph as U+0020. A PDF font's ToUnicode CMap gives each glyph one destination, and every word separator is
shown with the space glyph, so `CMapInfo.MapGlyphToText` keeps that glyph mapped to U+0020 whenever both
were drawn. A `&nbsp;` in such a font therefore extracts as an ordinary space. In a font with a glyph of
its own for U+00A0 (Liberation Sans, DejaVu Sans) it still extracts as U+00A0.

That matches what pypdf, PDFium and pdfminer.six extract from Chrome's PDFs; MuPDF reads U+00A0 from
Chrome's, so Chrome keeps the distinction some other way. The exact fix is a per-occurrence
`/ActualText` (or a duplicated glyph) for each no-break space drawn with the shared glyph - the mechanism
colour-font text already uses (`ColorGlyphPainter`) - which would have to nest inside tagged PDF's own
marked content. Before word separators were written, the collision existed too, but the space glyph was
rarely drawn, so whichever character was drawn last silently decided how both extracted.

Text fidelity only; nothing on the page is affected. Not a CSS deviation.
