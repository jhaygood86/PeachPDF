# Bitmap colour emoji fonts (`CBDT`/`CBLC`, `sbix`) now draw their pictures

**Before:** a font whose colour glyphs are bitmaps - the classic Noto Color Emoji build (`CBDT`/`CBLC`) or Apple Color Emoji (`sbix`) -
rendered blank or as the font's monochrome outline, because only the vector `COLR`/`CPAL` formats were read.

**Now:** each glyph is drawn as its picture from the font's best strike, at the text's size, placed by the picture's bearings; the text
stays selectable and searchable through an invisible-text layer. Repeats of a glyph share one image object. A document using such an
emoji under PDF/A-1 or PDF/X-1a/X-3 needs `TransparencyPolicy.Flatten` (the pictures have an alpha channel).

Verified at the previous release tag (v0.9.19): `docs/html-css-support.md` listed `CBDT`/`CBLC` and `sbix` under "Still unsupported".
