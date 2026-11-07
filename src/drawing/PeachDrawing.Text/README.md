# PeachDrawing.Text

Font loading, OpenType shaping, paragraph layout, bidirectional text and Unicode text primitives for .NET, with no third-party
dependencies (only its own data package, `PeachDrawing.Text.Data`, which comes along). It is the engine [PeachPDF](https://peachpdf.net/)
renders HTML with, published as a standalone package usable in any application: load a font, shape text into positioned glyphs, lay out a
styled paragraph into lines, read glyph outlines, or cut a font down to the glyphs a document uses. It is trimmable and Native AOT compatible,
and runs everywhere .NET runs.

```bash
dotnet add package PeachDrawing.Text
```

**Status:** pre-1.0, versioned in lockstep with PeachPDF (the same number for every release); until 1.0 the public API may change between releases.
Public today: font loading and matching (`FontSet`, `TypefaceFamily`, `Typeface`), what a typeface says about itself (`TypefaceMetrics`, glyph mapping,
advances), shaping (`Shaper`, `ShapeSettings`, `GlyphRun`), glyph outlines and color glyphs (`GlyphOutline`, `ColorPalette`, the COLR paint graph,
bitmap glyphs), variable fonts (`Typeface.WithAxes`), the `MATH` table, paragraph layout (`PeachDrawing.Text.Layout`), font subsetting for embedding
(`TypefaceExporter`) and the `PeachDrawing.Text.Unicode` namespace (bidirectional text, scripts, line breaking, hyphenation, emoji). Targets .NET 10 and later.

## What is in it

- **Fonts:** TrueType, OpenType (`glyf`, CFF and CFF2), WOFF and WOFF2 loading; collections; installed-font discovery on Windows, macOS, iOS, Linux
  (fontconfig) and Android; CSS Fonts 4 face matching by weight, width and style; `unicode-range` and glyph-coverage fallback.
- **Shaping:** GSUB and GPOS (ligatures, kerning, mark attachment, contextual lookups), Arabic and Syriac joining, the Universal Shaping Engine
  (Devanagari, Bengali, Gujarati, Tamil), Khmer, default-ignorable handling, and variation sequences.
- **Outlines and color:** glyph outlines, COLR v0/v1 with CPAL, CBDT/CBLC and sbix bitmaps, SVG glyph documents, and TrueType and CFF grid fitting (hinting).
- **Layout:** paragraphs laid out at any width with hit testing, carets and selection boxes.
- **Unicode:** the Bidirectional Algorithm (UAX #9), line breaking (UAX #14), script itemization, vertical orientation, TeX/Liang hyphenation for 73 languages.

## Quick start: load a font and shape text

```csharp
using System.Text;
using PeachDrawing.Text;
using PeachDrawing.Text.Shaping;

var fonts = new FontSet();                                   // the installed fonts, plus the ones you add
TypefaceFamily brand = fonts.AddFile("Brand-Regular.otf", new AddOptions { FamilyName = "Brand" });   // TrueType, OpenType, WOFF, WOFF2
fonts.AddFile("Brand-Bold.otf", new AddOptions { FamilyName = "Brand", Weight = 700 });

// CSS Fonts 4 matching: nearest width, slant, then weight; Synthesis says what is still missing (bold, italic).
if (brand.TryMatch(new TypefaceQuery(Weight: 600, IsItalic: true), out TypefaceMatch match))
{
    Typeface face = match.Typeface;                         // a typeface has no size; the same Typeface serves every size

    GlyphRun run = Shaper.Shape(face, "office", new ShapeSettings(Caps: CapsMode.SmallCaps));
    foreach (PlacedGlyph glyph in run.Glyphs)
    {
        double advance = face.GetAdvance((ushort)glyph.GlyphIndex) + glyph.XAdvanceDelta;   // design units
    }

    double ascent = 16 * face.Metrics.CellAscent / face.Metrics.UnitsPerEm;                  // metrics at 16 px
    bool hasGlyph = face.TryMapRune(new Rune('A'), out ushort glyphIndex);
}
```

Add fonts before you match: an answer a `FontSet` has already given is remembered. A `FontSet` is not safe for concurrent use; give each thread its own.

## Lay out a paragraph

```csharp
using PeachDrawing.Text.Layout;

var builder = new ParagraphBuilder(new RunStyle(face, 16))
    .SetStyle(new ParagraphStyle { Align = TextAlign.Start, OverflowWrap = OverflowWrap.BreakWord });
builder.AddText("Some ").PushRun(new RunStyle(bold, 24)).AddText("large").PopRun().AddText(" text.");
Paragraph paragraph = builder.Build();                      // immutable, safe to lay out from several threads

ParagraphLayout layout = paragraph.Layout(availableWidth: 300);
foreach (LineBox line in layout.Lines)
    foreach (PlacedRun run in line.Runs)                    // left to right, in drawing order
    {
        // run.Glyphs, run.X (left edge) and run.Baseline, in layout units
    }

TextPosition caret = layout.PositionAt(new System.Drawing.PointF(40, 10));     // hit testing, CaretRect, SelectionBoxes
```

Lines break where UAX #14 allows, bidirectional text is reordered per line, fallback fonts, letter and word spacing, alignment, justification, hyphenation,
`text-overflow` ellipsis and inline boxes are supported. `PeachDrawing.Text` stops at positions: with [PeachDrawing.Core](https://www.nuget.org/packages/PeachDrawing.Core/)
one call paints a layout on any canvas (`canvas.DrawParagraph(layout, origin, color)`).

## Outlines and color glyphs

```csharp
using PeachDrawing.Text.Outlines;

if (face.TryMapRune(new Rune('g'), out ushort g) && face.TryGetOutline(g, out GlyphOutline outline))
{
    foreach (OutlineContour contour in outline.Contours)
    {
        // move to contour.Start, then for each OutlineSegment a line to End, or a cubic through Control1 and Control2
    }
}
```

`ColorPalette` and the COLR paint graph (`GetColorPaint`) give color glyphs, `TryGetSvgGlyph` the SVG documents of an `SVG ` table, and `EmbeddedBitmap` bitmap glyphs.

## Variable fonts

```csharp
Typeface semiBold = face.WithAxes([new AxisSetting("wght", 650)]);   // outlines, advances and metrics follow the axes
```

## Embed a subset in a document

```csharp
using PeachDrawing.Text.Export;

IEnumerable<int> usedGlyphs = [3, 17, 42];                    // the glyph indices your document draws
ExportedFont subset = TypefaceExporter.ExportSubset(face, usedGlyphs, keepCharacterMap: false);
byte[] fontFile = subset.Data.ToArray();                      // subset.Format says what the bytes are
```

Glyph indices are preserved, composite glyph components come along, and a variable font is written as a static font at the chosen location.

## Unicode

```csharp
using PeachDrawing.Text.Unicode;

BidiAnalysis analysis = Bidi.Analyze("abc אבג", BaseDirection.Ltr);              // UAX #9; Bidi.ReorderLine orders a line's runs
LineBreakOpportunity[] breaks = LineBreaker.FindOpportunities("Wrap this line.");    // UAX #14, with CSS word-break and line-break tailorings
IReadOnlyList<int> points = Hyphenator.FindBreakPoints("hyphenation", "en-US");      // where a hyphen may go
```

Also: grapheme, word and sentence segmentation, script itemization, OpenType script and language tags, vertical orientation, default-ignorable characters and emoji presentation.

## Hosts without Brotli (WebAssembly)

The Unicode tables and hyphenation patterns are Brotli-compressed. On a host whose Brotli decoder does not work (a browser), register a managed decoder once at startup
with `PeachDrawing.Text.Compression.BrotliDecompression.SetDecompressor(...)`; without one, WOFF2 fonts, hyphenation and Thai/Lao/Khmer/Burmese line breaking degrade
rather than fail. The repository's `PeachDrawing.Text.Brotli` project is a ready-made managed decoder (it is not published as a package).

## More

- [The PeachDrawing.Text guide](https://peachpdf.net/peachdrawing-text.html): face matching, metrics, shaping settings, hinting, variable fonts, the MATH table, layout, every Unicode API.
- [API reference](https://peachpdf.net/api/PeachDrawing.Text/index.html)

## Sponsorship

PeachPDF is free and open source. If it saves you time, you can [sponsor jhaygood86 on GitHub](https://github.com/sponsors/jhaygood86);
sponsors can get paid support, see [Sponsorship](https://peachpdf.net/sponsorship.html).

## License

BSD 3-Clause (see `LICENSE`). Parts of the font readers derive from PDFsharp (MIT), the TrueType bytecode interpreter
and Adobe's CFF engine that grid-fit outlines are ported from FreeType (FreeType Project License, `FTL.TXT`, with Adobe's patent license grant), several shaping algorithms are
ports of HarfBuzz code (Old MIT), and the data tables come from the Unicode Character Database, the `hyph-utf8`
pattern collection and ICU's Thai, Lao, Khmer and Burmese word lists; each of those notices is reproduced in `THIRD-PARTY-LICENSES.md`, which ships in this package.

Portions of this software are copyright © 1996-2026 The FreeType Project (https://freetype.org). All rights reserved.
This software is based in part on the work of the FreeType Team.
