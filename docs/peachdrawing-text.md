# PeachDrawing.Text

`PeachDrawing.Text` is the font and text engine PeachPDF renders HTML with, published as its own NuGet package so other
.NET applications can use it without PeachPDF. It has no package dependencies, is trimmable and Native AOT compatible,
and is versioned in lockstep with PeachPDF: the same version number for every release, and PeachPDF depends on it.

```bash
dotnet add package PeachDrawing.Text
```

> **Status: pre-1.0.** The library is being opened up area by area. Today the public surface is font loading and
> matching (`FontSet` and the types around it), what a `Typeface` says about itself (metrics, glyph mapping and advances),
> shaping, glyph outlines and colour glyphs, the `MATH` table, and the `PeachDrawing.Text.Unicode` namespace, all described
> below. Font subsetting for embedding, described below, is public too. Paragraph layout is still internal to the package, so
> PeachPDF is the only consumer of it, and it will be published in a later release. Until 1.0, the public API may change between releases.

## What the engine does

- **Fonts:** TrueType, OpenType (`glyf` and CFF), WOFF and WOFF2 loading; TrueType/OpenType collections; installed-font
  discovery on Windows, macOS, Linux (through fontconfig) and Android; CSS Fonts 4 face matching by weight, width and
  style; `unicode-range` and glyph-coverage fallback.
- **Shaping:** GSUB and GPOS (ligatures, kerning, mark attachment, contextual lookups), Arabic and Syriac joining, the
  Universal Shaping Engine for Devanagari, Bengali, Gujarati and Tamil, default-ignorable handling, and `cmap` format 14
  variation sequences.
- **Outlines and colour:** glyph outlines for `glyf` and CFF, COLR v0 and v1 with CPAL, and CBDT/CBLC and sbix bitmaps.
- **Variable fonts:** the axes of a font and reading it at a location (`Typeface.WithAxes`): TrueType outlines, advance widths and font-wide metrics follow the axes.
- **Mathematics:** the `MATH` table: layout constants, per-glyph italics corrections and accent attachment, and the
  variants and assemblies of stretchy glyphs.
- **Unicode:** the Unicode Bidirectional Algorithm, script itemization, vertical orientation, emoji presentation, and
  TeX/Liang hyphenation for 73 languages.

## Fonts: `FontSet`, families and matching

A `FontSet` is the fonts a piece of text can be set in: the fonts installed on the machine, plus the ones you add to it.
A font you add under the name of an installed family joins that family for that set only, taking the place of the face
with the same weight, slant, width and code point ranges. Two sets never see each other's fonts, so two callers can
register different data under one family name. A set is not safe for concurrent use: give each thread its own. Add
the fonts before you match: an answer a set has already given is remembered and does not change when a font is added
afterwards, so that measuring and drawing one piece of text cannot end up in different faces.

```csharp
using PeachDrawing.Text;

var fonts = new FontSet();

// TrueType, OpenType (glyf and CFF), WOFF and WOFF2 are recognised by their content.
TypefaceFamily brand = fonts.AddFile("Brand-Regular.otf", new AddOptions { FamilyName = "Brand" });
fonts.AddFile("Brand-Bold.otf", new AddOptions { FamilyName = "Brand", Weight = 700 });

// Ask a family for the face that fits a query. A query has a weight, a width class, a slant and, optionally, a
// character the face has to be able to draw.
if (brand.TryMatch(new TypefaceQuery(Weight: 600, IsItalic: true), out TypefaceMatch match))
{
    Typeface face = match.Typeface;              // the face that matched; it has no size
    SyntheticStyle toFake = match.Synthesis;     // what is still missing: Bold, Italic, both, or None
}
```

`TryMatch` follows CSS Fonts 4 face matching: the slant first, then the width, then the weight, taking the nearest face
when none is exact. A face is taken to cover the characters of its `unicode-range` if it has one, and the ones its
`cmap` maps otherwise. `Synthesis` says what the caller has to fake because the face falls short: bold when 600 or more
was asked for and the face is lighter, italic when italic was asked for and the face is upright.

A typeface has no size. Text size belongs to whoever draws the text, and the same `Typeface` serves every size.

### What a typeface says about itself

Everything a `Typeface` reports is in design units: whole numbers on the grid the font was drawn on, `UnitsPerEm` of them
to the em. To get a length at a size, multiply by the size and divide by `UnitsPerEm`.

```csharp
TypefaceMetrics metrics = face.Metrics;
double size = 16;
double ascent = size * metrics.CellAscent / metrics.UnitsPerEm;

if (face.TryMapRune(new Rune('A'), out ushort glyph))
{
    double advance = size * face.GetAdvance(glyph) / metrics.UnitsPerEm;
}
```

- `Metrics` (`TypefaceMetrics`) has two sets of line dimensions, because platforms disagree about which one text is set
  with. `CellAscent`, `CellDescent` and `LineSpacing` are the rectangle Windows draws a line in. `NormalLineAscent`,
  `NormalLineDescent` and `NormalLineGap` are what browsers use for CSS `line-height: normal`. It also has the underline and
  strikeout stroke positions and thicknesses, `CapHeight`, `XHeight` (with `HasMeasuredXHeight` saying whether the font
  recorded it or it is an estimate), `ItalicAngle`, and the font's bounding box (`XMin`, `YMin`, `XMax`, `YMax`).
- `TryMapRune` finds the glyph a character is drawn with through the font's `cmap`, and `HasGlyph` asks only whether there
  is one. `GetAdvance` is the horizontal advance of a glyph.
- `HasVerticalMetrics`, `GetVerticalAdvance`, `HasVerticalOrigin` and `GetVerticalOrigin` are what vertical text needs. A font
  with no vertical metrics answers one em for every advance, which the OpenType specification allows.
- `TryGetScriptPosition` gives the size and offset the font's designer recommends for subscripts and superscripts.
- `HasColorGlyphs` says whether the face draws colour glyphs as vector fills, `SupportsFeatures` whether its `GSUB` table has
  an active lookup for every one of a set of OpenType feature tags, and `MatchesEmojiPresentation` whether it is a face to
  prefer for a character drawn as text or as emoji.

`AddOptions` is the counterpart of the descriptors of a CSS `@font-face` rule: the family name, weight, italic, width
class and `unicode-range` to register a font under in place of what the file itself declares.

Other things a `FontSet` does:

- `TryFindFamily` looks a family up by name, ignoring case. `MatchOrFallback` never fails for a set that holds a font: a
  family the set does not know is answered with a face of its first family.
- `TryFindCoveringFamily` is the last-resort search of CSS font matching: which family draws a character that none of
  the families asked for can, preferring the one whose coverage mostly lies in the character's own script, and, for a
  character with a text and an emoji form, a face that supports the presentation asked for (see `Emoji` below).
- `ResolveGeneric` names a family for a CSS generic such as `serif` or `monospace` on this platform: fixed names on
  Windows, macOS and Android, fontconfig on Linux, and the first available of a list of math fonts for `math`.
- `HasExplicitRanges` says whether any face of a family declares a `unicode-range`, which is when text has to be
  resolved character by character.
- `FontSet.InstalledFamilyNames` lists the installed families.
- Data that is not a font is reported with a `TypefaceFormatException`. Of a TrueType or OpenType collection, only the
  first face is added.

## Shaping: `PeachDrawing.Text.Shaping`

`Shaper.Shape` turns text into glyphs in one face: the `cmap` mapping, the substitutions of the font's `GSUB` table and the
positioning of its `GPOS` table.

```csharp
using PeachDrawing.Text.Shaping;

GlyphRun run = Shaper.Shape(face, "office", new ShapeSettings(Caps: CapsMode.SmallCaps));

foreach (PlacedGlyph glyph in run.Glyphs)
{
    // glyph.GlyphIndex is the glyph, glyph.ClusterStart and ClusterLength say which characters it stands for, and the
    // deltas and offsets are the GPOS adjustments, in design units.
    double advance = face.GetAdvance((ushort)glyph.GlyphIndex) + glyph.XAdvanceDelta;
}
```

- `ShapeSettings` folds every request into one: ligatures (`LigatureSet`), caps (`CapsMode`), numerals (`NumeralSet`),
  East Asian forms (`EastAsianSet`), sub- and superscripts (`SubSuperMode`), kerning, a language, an OpenType script tag,
  features asked for by tag (`FeatureSetting`), and which presentation of a text-or-emoji character to choose a glyph for.
  The names are CSS's, from `font-variant-*` and `font-feature-settings`. A tag that a typed group controls is always decided by
  the group and not by an explicit setting, which is CSS's precedence. Write `new ShapeSettings()` or `ShapeSettings.Default`
  for the defaults; `default(ShapeSettings)` is all zeros and means no ligatures and no kerning.
- A ligature is one glyph whose cluster covers the matched characters. A variation selector, and another invisible character
  (a joiner, a bidi control) that the font has no glyph for, takes part in substitution and positioning, so a lookup that
  matches on it still sees it, and is removed from the result at the end; a font that does map such a character keeps its glyph.
- Explicit features (`FeatureSetting`) are substitution features asked for by tag. Kerning is its own setting, and a value of 0
  leaves a feature unrequested; it cannot switch off one that shaping applies on its own, such as `ccmp` and `locl`.
- For a run of a joining script, `ArabicJoining.Resolve` gives the positional form of every character, and for an Indic
  script `UniversalShaping.Classify` gives its Universal Shaping Engine category; both go into `ShapeSettings` (`JoiningForms`
  and `UseCategories`). `ReverseForDisplay` asks for the glyphs in visual order for a right-to-left run.
- `GlyphRun.Advance` is the distance the pen travels along the run, in design units and without rounding.
- `Shaper.GetFeatureTags` names the `GSUB` tags behind a caps or position mode. With `Typeface.SupportsFeatures` it says whether a
  face has a feature for real, or a caller has to fall back to something synthesized.

## Outlines and colour glyphs: `PeachDrawing.Text.Outlines`

`Typeface.TryGetOutline` reads the shape of a glyph as data: a `GlyphOutline` of closed contours, each a start point and a list
of segments that are straight lines or cubic curves, in design units with the y axis up. A glyph is filled by the nonzero
winding rule, which is how it gets its counters. TrueType quadratic curves are raised to cubic ones, so a consumer has two kinds
of segment to draw. Nothing is grid-fitted.

```csharp
using PeachDrawing.Text.Outlines;

if (face.TryMapRune(new Rune('g'), out ushort glyph) && face.TryGetOutline(glyph, out GlyphOutline outline))
{
    foreach (OutlineContour contour in outline.Contours)
    {
        // move to contour.Start, then for each OutlineSegment draw a line to End, or a cubic through Control1 and Control2
    }

    // Where the ink lies across a band between two heights: what text-decoration-skip-ink needs.
    foreach (var (start, end) in outline.Crossings(bandLow: -200, bandHigh: -100)) { /* ... */ }
}
```

- **Colour glyphs from outlines.** `HasColorGlyphs` says a face has them, and `ColorPalette` gives its palettes: `TryGetColor`
  returns a `System.Drawing.Color`, and `FirstLightPalette` and `FirstDarkPalette` are what CSS `font-palette: light` and `dark`
  ask for. A version 0 `COLR` glyph is a list of layers from `TryGetColorLayers`, each a glyph filled with one palette colour.
  A version 1 glyph is a paint graph: `GetColorPaint` returns the root `ColorPaint`, and the sealed types that derive from it are
  named as the `COLR` specification names its paint formats (`PaintSolid`, `PaintLinearGradient`, `PaintRadialGradient`,
  `PaintSweepGradient`, `PaintGlyph`, `PaintTransform`, `PaintComposite`, `PaintColrGlyph`, and `PaintColrLayers`, whose layers
  are read with `GetColorLayerPaint`). Variable paints are read at the font's default instance.
- **Colour glyphs from pictures.** A font whose colour glyphs are bitmaps (`CBDT`/`CBLC` or `sbix`) reports
  `HasBitmapGlyphs`, and `TryGetBitmap` gives the picture of a glyph from the strike best suited to a size, with its bearings.

## Variable fonts

A variable font is one file that holds a whole design space: axes such as weight and width, and the outlines and metrics at every
point in between. `Typeface.IsVariable` says whether a face is one, `Typeface.Axes` lists its axes (a `VariationAxis` with a tag, a
range and a default; the tags the specification registers are in `AxisTags`), and `Typeface.NamedVariations` lists the named
locations the font declares. `Typeface.WithAxes` returns the typeface at a location.

```csharp
if (face.IsVariable)
{
    Typeface bold = face.WithAxes([new AxisSetting(AxisTags.Weight, 700)]);
    Typeface condensedBold = bold.WithAxes([new AxisSetting(AxisTags.Width, 80)]);   // builds on what bold has

    ushort glyph = ...;
    int advance = condensedBold.GetAdvance(glyph);                  // design units at that location
    bool hasOutline = condensedBold.TryGetOutline(glyph, out GlyphOutline outline);
}
```

- An axis you leave out keeps the value the typeface has, a tag the font has no axis for is ignored, and a value outside the axis's
  range is clamped to it. Asking for the same location again gives the same `Typeface`, and every axis at its default gives the
  font's own default typeface.
- Outlines (including composite glyphs), advance widths, the font-wide metrics of `Typeface.Metrics` and shaping advances follow the
  location. Reading `TypefaceMetrics.XMin` to `YMax` (the font bounding box) and the vertical advances gives the default design's
  values, `GPOS` kerning and mark positions and `GSUB` feature variations are not applied, and a variable font with CFF2 outlines
  has no outlines: what a location changes is what the `gvar`, `HVAR`, `MVAR` and `avar` tables of a font with TrueType outlines say.
- Embedding an instance in a PDF, and the CSS properties that would ask for one, come later.

## Mathematics: `PeachDrawing.Text.OpenType`

A face made for setting mathematics has a `MATH` table, and `Typeface.HasMathData` says so. `Typeface.MathData` returns it as a
`MathTable` with three parts. `Constants` holds the values a math layout algorithm positions fractions, radicals, scripts,
stacks and limits with, in design units apart from the percentages. `GlyphInfo` answers per glyph: the italics correction,
the horizontal position an accent attaches at, and whether the glyph is an extended shape. `Variants` gives the glyphs that
stretch (fences, radicals, accents, arrows) their pre-sized variants and, for a size beyond the largest, the parts to
assemble them from.

```csharp
using PeachDrawing.Text.OpenType;

if (face.MathData is MathTable math && face.TryMapRune(new Rune('('), out ushort paren))
{
    double axis = math.Constants.AxisHeight;                      // design units above the baseline
    MathGlyphConstruction? tall = math.Variants.GetVerticalConstruction(paren);

    foreach (MathGlyphVariant variant in tall?.Variants ?? [])    // smallest to largest
    {
        // the first variant whose AdvanceMeasurement reaches the size you need is the one to draw
    }

    if (tall?.Assembly is MathGlyphAssembly assembly)
    {
        // bottom to top: repeat the parts that IsExtender until the target height is reached,
        // overlapping neighbours by at most their connector lengths and at least Variants.MinConnectorOverlap
    }
}
```

The per-glyph corner kerning of `MathKernInfo` and the device tables that adjust a value at particular sizes are not read.

## Embedding: `PeachDrawing.Text.Export`

A document that embeds a font wants only the glyphs it uses. `TypefaceExporter.ExportSubset` cuts a typeface down to the glyph
indices you give it and returns the bytes of a font file, an `ExportedFont`.

```csharp
using PeachDrawing.Text.Export;

ExportedFont subset = TypefaceExporter.ExportSubset(face, usedGlyphs, keepCharacterMap: false);
byte[] fontFile = subset.Data.ToArray();
// subset.HasCffOutlines says which kind of font stream to write; subset.IsSubset says whether it was cut down.
```

- The glyphs keep their indices, so text already encoded as glyph indices stays valid against the subset. The glyphs a composite
  glyph is made of come along, and so does the notdef glyph.
- A colour glyph that has no outline of its own (its shapes are its layers) is given a small outline, so a reader can still
  select the text it stands for.
- A subset carries no name table, so it is meant to be embedded, not loaded back into a `FontSet`.
- A font with CFF outlines is not cut down: it is returned whole, and `IsSubset` is `false`.
- `keepCharacterMap` says whether the character map stays. A font whose text is encoded as glyph indices is smaller without it.

What a font descriptor records about a face comes from the members you already have: `Typeface.Metrics` (with `IsSymbolic`,
`IsFixedPitch`, `HasSerifs`, `IsItalicStyle` and `FirstCharIndex` for the descriptor flags), `Typeface.GetAdvance` for widths,
`Typeface.FullName` for a base font name, and `Typeface.ContentHash` to key a cache of what you made from a face.

## The `PeachDrawing.Text.Unicode` namespace

Each entry point is a static class named for the algorithm or property it implements, and takes plain strings, runes
and arrays.

### Bidirectional text

[UAX #9](https://www.unicode.org/reports/tr9/) works in two steps, and so does the API. `Bidi.Analyze` resolves an
embedding level for every UTF-16 code unit of a paragraph. Once your layout has decided where lines break,
`Bidi.ReorderLine` puts one line's runs in the order they are drawn.

```csharp
using PeachDrawing.Text.Unicode;

string text = "abc אבג";
BidiAnalysis analysis = Bidi.Analyze(text, BaseDirection.Ltr);

foreach (BidiRun run in Bidi.ReorderLine(analysis.Levels, 0, text.Length))
{
    string piece = text.Substring(run.Start, run.Length);
    // A run at an odd level reads right to left: Mirror reverses it and swaps mirrored characters such as brackets.
    Console.WriteLine(run.IsRtl ? Bidi.Mirror(piece, run.Level) : piece);
}
```

`BaseDirection.Auto` detects the direction from the first strong character. A host with its own markup, such as CSS
`unicode-bidi` or an SVG `direction` attribute, passes `EmbeddingSpan` values to describe embeddings the text itself
does not spell out. `Bidi.ClassOf` returns a character's `Bidi_Class`, and `Bidi.TryGetMirror` finds a bracket's
counterpart.

The implementation is checked against Unicode's `BidiCharacterTest.txt` conformance file. Rule L1's last clause, which
resets the whitespace at the end of each line, is left to the caller because it depends on whether the caller lays
out in characters, words or glyphs.

### Scripts and OpenType tags

`Scripts.Of` returns the Unicode `Script` of a character (`Latin`, `Arabic`, `Han`, and the shared `Common` and
`Inherited`). `Scripts.Resolve` gives every character of a text the script it is to be treated as, so that a comma
between two Arabic words counts as Arabic (UAX #24 section 5.1). `OpenTypeTags.ForScript` and `OpenTypeTags.ForLanguage`
turn a script name or a BCP 47 language tag into the four-letter tag an OpenType font's layout tables are keyed by.
Both answer `null` for a script or language the built-in table does not cover.

### Vertical text, invisible characters, hyphenation and emoji

- `VerticalOrientation.IsEffectivelyUpright` says whether a character stays upright in vertical text set with CSS
  `text-orientation: mixed`; `VerticalOrientation.Of` returns the UAX #50 class behind it.
- `DefaultIgnorables.Contains` recognises the characters that carry meaning but draw nothing, such as joiners,
  variation selectors and bidi controls, for which drawing a "missing glyph" box would be wrong.
- `Hyphenator.FindBreakPoints("hyphenation", "en-US")` returns the indexes at which a hyphen may be inserted, using
  the TeX patterns for the closest supported language. An unsupported language, a word shorter than that language's
  minimums, or a word with non-letters in it, yields an empty list.
- `Emoji.Resolve` and `Emoji.ResolveAt` decide whether a character that has both a text and an emoji appearance is
  drawn as one or the other, from an `EmojiMode` (CSS `font-variant-emoji`) and any variation selector that follows.

## Licences

The package is BSD 3-Clause. It carries its third-party notices with it, in `THIRD-PARTY-LICENSES.md`: the font readers
derive from PDFsharp (MIT), several shaping algorithms are ports of HarfBuzz code, and the data tables come from the
Unicode Character Database and the `hyph-utf8` pattern collection. See [License](license.md) for the whole list.
