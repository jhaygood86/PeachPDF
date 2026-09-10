# Color-font vector glyphs are now searchable and selectable

_Landed 2026-09-10._

PeachPDF still draws `COLR`/`CPAL` glyph artwork as native PDF vectors, so visible rendering and palette
behavior are unchanged. It now additionally emits rendering-mode-3 invisible text over those vectors,
with `/ToUnicode` and per-occurrence `/ActualText`, so color emoji can be searched, selected, and copied
as their exact source sequences in PDFium- and MuPDF-based viewers.

- *Before:* a color emoji was only vector paths. It looked correct, but had no selectable text geometry
  and could not be found or copied.
- *After:* the visible vectors are accompanied by an embedded subset of the color font used solely for
  invisible selection text. The subset increases PDF size; the exact increase depends on the font and
  glyphs used.

Many COLR base glyphs have no TrueType contour of their own because their artwork lives in separate
layer glyphs. The embedded subset gives each selected empty base glyph a tiny valid contour: it remains
invisible under PDF text rendering mode 3, but prevents PDFium (Chrome/Edge) from dropping that CID from
its text page. The real layer outlines are not embedded for this purpose.

Under PDF/A, a color-font source character which resolves to glyph 0 (`.notdef`) can now raise the same
missing-glyph conformance exception as ordinary text. Previously the vector-only color path emitted no
text-showing operator, so that PDF/A rule was not reached for color fonts.
