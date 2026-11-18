# PDF import: optional content, page labels, Type 3 text, vertical metrics, Symbol/Courier

Closed together (each had an accepted-gap file, now deleted):

- **Optional content** (`PdfOptionalContent`): default configuration only; `BDC /OC` pushes a hidden span (`_hiddenSpans`), and while one is open paint operators still reset the path and apply clips but draw nothing, the pen of hidden text still advances, and `Do`/annotations check `/OC`. Trap: the text reader skips every operator not in `TextOnlyOperators`, so `BMC`/`BDC`/`EMC` had to be added there or hidden text was still read.
- **Page labels** (`PdfPageSource.GetPageLabels`, public API snapshot updated). The reader's page box accepts a label first, then a number (`ReaderDocument.PageOf`).
- **Type 3 text**: ToUnicode first, then the glyph name through `PdfGlyphNames`; widths place the glyph.
- **Vertical metrics**: `/DW2` and `/W2` are read into `PdfShownGlyph` (`VerticalAdvance`, `OriginX`, `OriginY`); defaults are the spec's.
- **Symbol** without an encoding: a partial built-in table (`PdfFontSubstitution.SymbolText`), and the substitute prefers a font with real Greek. **Courier** without widths: 600 units. The other standard-14 widths are still the substitute's own advances.
- Not done: `/AS` usage application, column detection, true font weights, ZapfDingbats mapping, the Helvetica/Times AFM width tables.
