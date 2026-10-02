# PeachDrawing.Text

Font loading, OpenType shaping, bidirectional text and Unicode text primitives for .NET, with no package
dependencies. It is a standalone package, usable on its own in any application.

**Status:** pre-1.0. The engine is complete and in production use, and its public API is being published area by area. Font loading and matching (`FontSet`, `TypefaceFamily`, `Typeface`), what a typeface says about itself (`TypefaceMetrics`, glyph mapping, advances), shaping (`Shaper`, `ShapeSettings`, `GlyphRun`), glyph outlines and colour glyphs (`GlyphOutline`, `ColorPalette`, the COLR paint graph, bitmap glyphs) and the `PeachDrawing.Text.Unicode` namespace (bidirectional text, script itemization, OpenType tags, vertical orientation, hyphenation, emoji presentation) the MATH table (`Typeface.MathData`) and font subsetting for embedding (`TypefaceExporter`) and variable fonts (`Typeface.WithAxes`) are public today; paragraph layout is still internal and will follow.
What is in it:

- **Fonts:** TrueType, OpenType (`glyf` and CFF), WOFF and WOFF2 loading; TrueType/OpenType collections; installed-font
  discovery on Windows, macOS, Linux (fontconfig) and Android; CSS Fonts 4 face matching by weight, width and style;
  `unicode-range` and glyph-coverage fallback.
- **Shaping:** GSUB and GPOS (ligatures, kerning, mark attachment, contextual and chaining lookups), Arabic and Syriac
  joining, the Universal Shaping Engine for Devanagari, Bengali, Gujarati and Tamil, default-ignorable handling, and
  `cmap` format 14 variation sequences.
- **Outlines and colour:** glyph outlines for `glyf` and CFF, COLR v0/v1 with CPAL, and CBDT/CBLC and sbix bitmaps.
- **Unicode:** the Unicode Bidirectional Algorithm (UAX #9, checked against `BidiCharacterTest`), script itemization,
  vertical orientation, and TeX/Liang hyphenation for 73 languages.

## Sponsorship

PeachPDF is free and open source. If it saves you time, you can [sponsor jhaygood86 on GitHub](https://github.com/sponsors/jhaygood86);
sponsors can get paid support, see [Sponsorship](https://peachpdf.net/sponsorship.html).

## Licence

BSD 3-Clause (see `LICENSE`). Parts of the font readers derive from PDFsharp (MIT), the TrueType bytecode interpreter
and Adobe's CFF engine that grid-fit outlines are ported from FreeType (FreeType Project License, `FTL.TXT`, with Adobe's patent licence grant), several shaping algorithms are
ports of HarfBuzz code (Old MIT), and the data tables come from the Unicode Character Database, the `hyph-utf8`
pattern collection and ICU's Thai and Khmer word lists; each of those notices is reproduced in `THIRD-PARTY-LICENSES.md`, which ships in this package.

Portions of this software are copyright © 1996-2026 The FreeType Project (https://freetype.org). All rights reserved.
This software is based in part on the work of the FreeType Team.
