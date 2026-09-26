# PeachDrawing.Text

`PeachDrawing.Text` is the font and text engine PeachPDF renders HTML with, published as its own NuGet package so other
.NET applications can use it without PeachPDF. It has no package dependencies, is trimmable and Native AOT compatible,
and is versioned in lockstep with PeachPDF: the same version number for every release, and PeachPDF depends on it.

```bash
dotnet add package PeachDrawing.Text
```

> **Status: pre-1.0.** The library is being opened up area by area. Today the public surface is font loading and
> matching (`FontSet` and the types around it) and the `PeachDrawing.Text.Unicode` namespace, both described below.
> Metrics, glyph mapping, shaping, outlines and paragraph layout are still internal to the package, so PeachPDF is the
> only consumer of them, and they will be published in later releases. Until 1.0, the public API may change between
> releases.

## What the engine does

- **Fonts:** TrueType, OpenType (`glyf` and CFF), WOFF and WOFF2 loading; TrueType/OpenType collections; installed-font
  discovery on Windows, macOS, Linux (through fontconfig) and Android; CSS Fonts 4 face matching by weight, width and
  style; `unicode-range` and glyph-coverage fallback.
- **Shaping:** GSUB and GPOS (ligatures, kerning, mark attachment, contextual lookups), Arabic and Syriac joining, the
  Universal Shaping Engine for Devanagari, Bengali, Gujarati and Tamil, default-ignorable handling, and `cmap` format 14
  variation sequences.
- **Outlines and colour:** glyph outlines for `glyf` and CFF, COLR v0 and v1 with CPAL, and CBDT/CBLC and sbix bitmaps.
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
