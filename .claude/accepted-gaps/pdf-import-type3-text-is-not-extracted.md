# PDF import: text in a Type 3 font is not extracted

`PdfPageSource.GetText` reads a page's text through the same interpreter that paints it, reporting each shown glyph with the Unicode text the font
gives for its code. A Type 3 font's glyphs are content streams, and the importer's `PdfType3Font.Glyph` carries a code, a width and a procedure but no
text, so a page set in Type 3 (some older TeX and scanned-and-recognized output) gives no text to select or search, though it paints.

**Why it is left:** the text of a Type 3 glyph comes from the font's `/Encoding` `/Differences` glyph names or its `/ToUnicode` map, which the Type 3 loader does
not read, and such a file is rare enough not to have been worth widening the loader for the first reader.

**To close it:** have `PdfType3Font.Decode` return the text of each code (`/ToUnicode` first, then the glyph name through `PdfGlyphNames`) and send it
to the text sink in `ShowType3`. Delete this file and the sentence about Type 3 in `docs/peachdrawing-pdf.md` when it is done.
