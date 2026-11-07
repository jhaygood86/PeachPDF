# PeachPDF Architecture

PeachPDF converts HTML and CSS into PDF documents entirely within .NET, with no external process dependencies. It is no longer one assembly: it is a stack of packages, each with one job and a public surface of its own, and the PDF library sits at the top of that stack. This page is organized the same way the stack is — from the bottom (fonts and text, the drawing contract, the drawing backends) up through the HTML/CSS/SVG/MathML engine, the views that host it on a screen, and finally the PDF product — and each package section says what it owns, how its source is laid out, what flows in and out, and where its boundaries are.

## Contents

- [Overview](#overview)
  - [The packages](#the-packages)
  - [Dependency direction](#dependency-direction)
  - [One document, end to end](#one-document-end-to-end)
  - [Public surfaces and internals](#public-surfaces-and-internals)
- [PeachImage](#peachimage)
- [PeachDrawing.Text.Data and PeachDrawing.Text](#peachdrawingtextdata-and-peachdrawingtext)
  - [What it contains](#what-it-contains)
  - [PeachDrawing.Text.Data](#peachdrawingtextdata)
  - [PeachDrawing.Text.Brotli](#peachdrawingtextbrotli)
  - [Font pipeline](#font-pipeline)
- [PeachDrawing.Core](#peachdrawingcore)
  - [The contract](#the-contract)
  - [Value types](#value-types)
  - [Shared drawing logic](#shared-drawing-logic)
  - [Rules that keep it a contract](#rules-that-keep-it-a-contract)
- [Canvas backends](#canvas-backends)
  - [PeachDrawing (CPU raster)](#peachdrawing-cpu-raster)
  - [PeachDrawing.PDF](#peachdrawingpdf)
  - [PeachDrawing.Svg](#peachdrawingsvg)
  - [PeachDrawing.Skia](#peachdrawingskia)
  - [PeachDrawing.Win2D](#peachdrawingwin2d)
  - [PeachDrawing.CoreGraphics](#peachdrawingcoregraphics)
- [PeachPDF.SourceGenerators](#peachpdfsourcegenerators)
- [PeachBrackets (the HTML engine)](#peachbrackets-the-html-engine)
  - [Public surface](#public-surface)
  - [Source map](#source-map)
  - [How a host drives it](#how-a-host-drives-it)
  - [Resource access](#resource-access)
  - [Lineage](#lineage)
  - [HTML parsing](#html-parsing)
  - [DOM model — CssBox](#dom-model--cssbox)
  - [CSS parsing](#css-parsing)
  - [Stylesheet application](#stylesheet-application)
  - [Layout](#layout)
  - [Per-character font matching](#per-character-font-matching)
  - [Fragmentation](#fragmentation)
  - [Painting](#painting)
  - [Tagged PDF (PDF/UA) structure tree](#tagged-pdf-pdfua-structure-tree)
  - [SVG rendering](#svg-rendering)
  - [MathML rendering](#mathml-rendering)
  - [Named pages and margin boxes](#named-pages-and-margin-boxes)
- [PeachBrackets views](#peachbrackets-views)
  - [PeachBrackets.WinUI](#peachbracketswinui)
  - [PeachBrackets.Android](#peachbracketsandroid)
  - [PeachBrackets.Apple](#peachbracketsapple)
- [PeachPDF (the product)](#peachpdf-the-product)
  - [Generating a PDF](#generating-a-pdf)
  - [Resource loading](#resource-loading)
  - [Registering fonts at runtime](#registering-fonts-at-runtime)
  - [The declarative builder](#the-declarative-builder)
  - [Tagged PDF, forms and annotations](#tagged-pdf-forms-and-annotations)
  - [Embedded files (attachments)](#embedded-files-attachments)
  - [Annotations from CSS](#annotations-from-css)
  - [PeachPDF.Cli](#peachpdfcli)
- [Testing](#testing)

---

## Overview

### The packages

| Package | Role | Depends on | Used by | Guide |
|---|---|---|---|---|
| **PeachImage** (separate repository) | Decodes and encodes raster images (JPEG, PNG, GIF, WebP, AVIF, TIFF, BMP, JPEG XL...) | — | every package that touches pixels | [PeachImage](peachimage.md) |
| **PeachDrawing.Text.Data** | The Unicode, hyphenation and dictionary data the text engine reads; no public API | — | `PeachDrawing.Text` | — |
| **PeachDrawing.Text** | Font loading and matching, shaping, bidi, line breaking, paragraph layout, outlines, colour glyphs, subsetting, hyphenation | `PeachDrawing.Text.Data` | everything below | [PeachDrawing.Text](peachdrawing-text.md) |
| **PeachDrawing.Text.Brotli** | Pure-managed Brotli decoder and encoder for hosts with no native one (not a published package; the browser demo references it) | `PeachDrawing.Text` | the Blazor demo | — |
| **PeachDrawing.Core** | The drawing contract: `Canvas`, `RenderContext`, paint value types, layers, links and semantic regions | `PeachDrawing.Text` | every backend, `PeachBrackets` | [PeachDrawing.Core](peachdrawing-core.md) |
| **PeachDrawing** | CPU software rasterizer: `RasterCanvas`, filters, blend modes, warps | `PeachDrawing.Core`, `PeachImage` | the PDF, SVG, Skia, Win2D and CoreGraphics backends; `PeachBrackets` | [PeachDrawing](peachdrawing.md) |
| **PeachDrawing.PDF** | The PDF `Canvas` and the `PdfComposer` that writes a PDF file; also reads, merges and rewrites existing PDFs | `PeachDrawing`, `PeachDrawing.Core`, `PeachDrawing.Text`, `PeachImage` | `PeachPDF` | [PeachDrawing.PDF](peachdrawing-pdf.md) |
| **PeachDrawing.Svg** | A vector SVG `Canvas` | `PeachDrawing.Core`, `PeachDrawing`, `PeachImage` | applications; the test harness | [PeachDrawing.Svg](peachdrawing-svg.md) |
| **PeachDrawing.Skia** | A SkiaSharp `Canvas` over a surface the host supplies | `PeachDrawing.Core`, `PeachDrawing`, `PeachImage`, SkiaSharp | `PeachBrackets.Android`; applications | [PeachDrawing.Skia](peachdrawing-skia.md) |
| **PeachDrawing.Win2D** | A Win2D `Canvas` (Windows only) | `PeachDrawing.Core`, `PeachDrawing`, `PeachImage`, Win2D | `PeachBrackets.WinUI` | [PeachDrawing.Win2D](peachdrawing-win2d.md) |
| **PeachDrawing.CoreGraphics** | A CoreGraphics `Canvas` (macOS and iOS) | `PeachDrawing.Core`, `PeachDrawing`, `PeachImage` | `PeachBrackets.Apple` | [PeachDrawing.CoreGraphics](peachdrawing-coregraphics.md) |
| **PeachBrackets** | The HTML, CSS, SVG and MathML engine: parse, cascade, lay out into pages, paint a page onto any `Canvas` | `PeachDrawing`, `PeachDrawing.Core`, `PeachDrawing.Text`, `PeachImage`, MimeKit | `PeachPDF` and the three views | [PeachBrackets](peachbrackets.md) |
| **PeachBrackets.WinUI / .Android / .Apple** | `HtmlView` controls that show a document on a screen | `PeachBrackets` plus the Win2D, Skia or CoreGraphics backend | applications | [WinUI](peachbrackets-winui.md), [Android](peachbrackets-android.md), [Apple](peachbrackets-apple.md) |
| **PeachPDF** | The PDF product: `PdfGenerator`, the declarative page builder, resource loaders, PDF feature builders | `PeachBrackets`, `PeachDrawing.PDF`, `PeachDrawing`, `PeachDrawing.Core`, `PeachDrawing.Text`, `PeachImage`, MimeKit | applications, `peachpdf` | [Getting started](getting-started.md) |
| **PeachPDF.Cli** | The `peachpdf` command-line tool (Native AOT) | `PeachPDF` | users | [Command-line tool](cli.md) |
| **PeachPDF.SourceGenerators** | Build-time analyzer that generates the CSS/SVG property registry; never shipped | — | `PeachBrackets` | — |

### Dependency direction

Dependencies only point downward: nothing below `PeachBrackets` knows what HTML is, nothing below `PeachDrawing.PDF` knows what a PDF is, and the PDF writer, the SVG writer and the screen backends are siblings that never reference each other. That is what lets each one be used with no other package in the picture.

```
PeachDrawing.Text.Data
        │
        ▼
PeachDrawing.Text ──────────────► (PeachDrawing.Text.Brotli, optional, by registration)
        │
        ▼
PeachDrawing.Core  ◄─────────────── PeachImage (decoders, used by the layers above)
        │
        ▼
PeachDrawing  (CPU raster)
        │
        ├──► PeachDrawing.PDF ─────────────┐
        ├──► PeachDrawing.Svg              │
        ├──► PeachDrawing.Skia ──► PeachBrackets.Android
        ├──► PeachDrawing.Win2D ─► PeachBrackets.WinUI
        └──► PeachDrawing.CoreGraphics ─► PeachBrackets.Apple
                                           │
PeachBrackets (HTML/CSS/SVG/MathML) ───────┤  (it draws onto any Canvas; it does not reference the PDF writer)
                                           ▼
                                        PeachPDF ──► PeachPDF.Cli
```

### One document, end to end

| Stage | Package that owns it |
|---|---|
| Fetch the HTML and its stylesheets, images and fonts (`RNetworkLoader`) | PeachPDF (the loaders); PeachBrackets asks for resources through the render context |
| Decode images | PeachImage |
| HTML parse, DOM (`CssBox`), CSS parse, cascade | PeachBrackets |
| Font matching, shaping, bidi, hyphenation, word measurement | PeachDrawing.Text, driven by PeachBrackets through `Canvas.MeasureString` |
| Layout and fragmentation into pages | PeachBrackets |
| Painting a page | PeachBrackets (`FragmentPainter`, `PagePainter`) onto a `Canvas` from PeachDrawing.Core |
| Effects PDF cannot express (blur, backdrop-filter, perspective) | PeachDrawing (`RasterCanvas`), reached through `Canvas.BeginRasterSurface` |
| Writing PDF content, fonts, images, annotations, structure, conformance | PeachDrawing.PDF |
| Mapping `PdfGenerateConfig` onto all of the above | PeachPDF |

### Public surfaces and internals

Every package publishes a reviewed `PublicApi.txt` snapshot beside its project, checked by a public-surface test, and everything not in it is `internal`. The cross-package rules are strict on purpose:

- `PeachPDF` reaches the PDF engine only through `PeachDrawing.PDF`'s public surface, the HTML engine only through `PeachBrackets.HtmlDocument` plus its annotation, tagging and building seams, and the text engine only through `PeachDrawing.Text`'s public API. None of those packages grants `InternalsVisibleTo` to `PeachPDF`; the only grants are to each package's own test project (and `PeachBrackets.Engine.Tests`, which asserts on the box tree).
- A contract that two packages must agree on is public and documented rather than shared through internals. The raster fallback (`BeginRasterSurface`/`DrawRaster`), the unit conventions (`PixelsPerPoint`, `TransformScale`), the tile-cache owner and the SVG glyph painter hook are all ordinary `Canvas`/`RenderContext` members, so a third-party `Canvas` can implement the same contracts the PDF one does.
- Each public surface is designed on its own terms, deliberately not mirroring any competing library's naming. The reviewed snapshots and the naming register that go with them are what a change to a public API is checked against.

---

## PeachImage

PeachImage is a separate repository and NuGet package, pinned by version in `src/Directory.Packages.props`. Every package that handles pixels decodes and encodes through it: the engine (`<img>`, `background-image`, `content: url()`), the PDF writer (embedding and re-encoding), the raster backend (decoded sources), and the Skia, Win2D and CoreGraphics backends (their default image codec). The formats, load/convert/resize/save API and the animation and metadata model are described in [PeachImage](peachimage.md); how the engine caches and routes decoded images is under [Image loading and decoding](#image-loading-and-decoding).

---

## PeachDrawing.Text.Data and PeachDrawing.Text

**Guide:** [PeachDrawing.Text](peachdrawing-text.md) · **Source:** [src/drawing/PeachDrawing.Text/](https://github.com/jhaygood86/PeachPDF/tree/main/src/drawing/PeachDrawing.Text/)

`PeachDrawing.Text` is the font and text engine, and the lowest layer anything else builds on. It has no third-party dependencies (its one dependency is its own sibling data package) and cannot reference any other PeachPDF package, which is what keeps the engine usable on its own. It owns everything between "a font file and a string" and "positioned glyph outlines".

### What it contains

| Folder | What it holds | Public? |
|---|---|---|
| `Typefaces/` | `FontSet`, `TypefaceFamily`, `Typeface`, `TypefaceQuery`/`TypefaceMatch`, `TypefaceMetrics`, `AxisRange`/`Variations` (variable fonts), `SyntheticStyle` | yes |
| `Shaping/` | `Shaper`, `ShapeSettings`, `GlyphRun`, `PlacedGlyph` | yes |
| `Layout/` | `Paragraph`, `ParagraphLayout`, `ParagraphLayoutBuilder`, `LineFlow`, `TextRuler`: itemize, break lines and place a paragraph | yes |
| `Outlines/` | `GlyphOutline`, the COLR paint graph (`ColorPaint`, `ColorPalette`), `EmbeddedBitmap`, `SvgGlyph`, `GridFitting` | yes |
| `OpenType/` | `MathTable` (the `MATH` table) and `BaselineTable` | yes |
| `Export/` | `TypefaceExporter`: font subsetting for embedding | yes |
| `Unicode/` | `Bidi`, `LineBreaker`, `Segmenter`, `Scripts`, `Hyphenator`, `Emoji`, `VerticalOrientation`, `DefaultIgnorables`, and the Arabic-joining, Universal Shaping Engine and Khmer category lookups: the Unicode algorithms and property tables | yes |
| `Compression/` | `BrotliDecompression`/`BrotliCompression`: a pluggable Brotli codec | yes |
| `Internal/Fonts/` | font discovery, `FontResolver`, `FontFactory`, `LoadedTypeface`, the OpenType table readers under `OpenType/`, WOFF/WOFF2 conversion, system font storage per platform | no |
| `Internal/Text/` | `GsubShaper`, `GposPositioner`, `ScriptRunResolver`, the Arabic, Universal Shaping Engine and Khmer shapers under `Shaping/`, the bidi and segmentation engines, `HyphenationEngine` | no |
| `Internal/Hinting/` | the TrueType and CFF hinting engine (a port of the FreeType interpreters) | no |

A `Typeface` is size-independent: it wraps the internal `LoadedTypeface`, which owns the parsed OpenType tables, metrics, glyph mapping, shaping entry points and outlines in design units. Core's `TypefaceFont` pairs a matched typeface with an em size, and that is the `Font` every `Canvas` draws with.

### PeachDrawing.Text.Data

All the data the engine reads at run time — the Bidi, Script, Universal Shaping Engine, vertical-orientation and Arabic-joining tables, the `hyph-utf8` hyphenation patterns for 73 languages, the OpenType and BCP 47 language-tag maps, and the Thai, Lao, Khmer and Burmese dictionary word lists — lives in a sibling package, `PeachDrawing.Text.Data`. It targets a single framework (`netstandard2.0`), has no public API (every member is `internal`, visible only to `PeachDrawing.Text`), and is Brotli-compressed. It is separate because NuGet never merges an embedded resource across a package's per-framework folders: embedded in `PeachDrawing.Text` itself, the same megabytes of data would ship once per target framework.

### PeachDrawing.Text.Brotli

WOFF2 fonts and the compressed hyphenation and dictionary data need a Brotli decoder. .NET's own is native, and a browser (WebAssembly) host has none. `PeachDrawing.Text` therefore exposes `Compression.BrotliDecompression.SetDecompressor` (and a `BrotliCompression` counterpart used for `/BrotliDecode` PDF streams) as plug-in points; the engine uses .NET's codec where it works, quietly degrades where it does not (an empty Unicode table, unhyphenated text, a WOFF2 font skipped), and a host without a working one registers a managed implementation. `PeachDrawing.Text.Brotli` is that implementation: a pure-managed decoder and encoder (`ManagedBrotliDecompressor`, `ManagedBrotliCompressor`) that a host registers once at startup. It is a source project in the repository (it is not published as a NuGet package); the Blazor browser demo references it, which is why WOFF2 fonts and `hyphens: auto` work there.

### Font pipeline

The font subsystem is built around OpenType:

- `FontSet` ([Typefaces/FontSet.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/drawing/PeachDrawing.Text/Typefaces/FontSet.cs)) — the public entry point for fonts: the installed fonts plus the ones added to it, per instance. Fonts are added with `AddData`, families are found with `TryFindFamily` and `TryFindCoveringFamily`, and a query (`TypefaceQuery`: weight, width class, slant, an optional character to cover) is matched with `TypefaceFamily.TryMatch` or `FontSet.MatchOrFallback`, giving a `TypefaceMatch` (a public `Typeface` and the `SyntheticStyle` still to be faked). A `RenderContext` (the PDF one, for example) owns one `FontSet` and goes through nothing else to register and match fonts. `FontSet` is a thin layer over the internal `FontResolver`.
- `LoadedTypeface` ([Fonts/LoadedTypeface.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/drawing/PeachDrawing.Text/Internal/Fonts/LoadedTypeface.cs)) — one resolved font face, independent of any size, behind the public `Typeface` (its `Face` property): it owns the `OpenTypeFontface` and the `OpenTypeDescriptor` (metrics, glyph mapping, shaping and outlines in design units). The font a `Canvas` draws with is Core's `TypefaceFont`: a matched typeface plus an em size.
- `OpenTypeFontface` — reads raw OpenType/TrueType tables (`cmap`, `glyf`, `hmtx`, `loca`, `OS/2`, etc.) from font binary data.
- `TypefaceCache` — caches resolved typefaces keyed by family+style to avoid repeated file reads (system families globally, custom `@font-face` families per `FontResolver` instance).
- Font subsetting (`Export/TypefaceExporter`) — a backend that embeds fonts (the PDF and SVG writers) embeds only the glyphs actually used, significantly reducing output size for documents that use a small part of a large font.

The font resolver honours the family mappings registered on the render context (`AddFontFamilyMapping`) and discovers system fonts at startup: from the operating system's font directories on Windows and Android, through fontconfig on Linux, and through CoreText (every installed font, with the fonts that have no file put together from CoreText's tables) on macOS and iOS. On top of that base, font resolution is **codepoint-aware**: matching, layout, and PDF text emission all operate on Unicode codepoints (`System.Text.Rune`), not on the requested family alone. This is what makes per-character font fallback, `@font-face unicode-range`, and supplementary-plane (emoji) text work.

#### Codepoint-aware resolution

A font family is a `FontFamilyModel` ([Fonts/FontFamilyModel.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/drawing/PeachDrawing.Text/Internal/Fonts/FontFamilyModel.cs)) whose `Faces` is a list of `FontFaceEntry`, each carrying the codepoint ranges it covers. Those ranges come either from an explicit `@font-face` [`unicode-range`](https://developer.mozilla.org/en-US/docs/Web/CSS/@font-face/unicode-range) descriptor or — for a system or rangeless font — from the font's *actual* `cmap` coverage, computed lazily by `CMapCoverage.Extract` ([Fonts/OpenType/CMapCoverage.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/drawing/PeachDrawing.Text/Internal/Fonts/OpenType/CMapCoverage.cs)). An entry also carries the weights, widths and oblique angles it covers: a single value for an ordinary face, the range of an `@font-face` `font-weight`/`font-stretch`/`font-style: oblique` descriptor, or, for a variable font that was given none, the range of its own axes. Matching measures a face at the value of its range nearest to the request, and a variable face is then set to that same value on its `wght`, `wdth` and `slnt` axes.

`FontResolver.ResolveTypeface` ([Fonts/FontResolver.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/drawing/PeachDrawing.Text/Internal/Fonts/FontResolver.cs)) takes an optional `Rune`; when supplied, it first filters a family's faces to those that cover that codepoint, then style-matches (weight/stretch/style, per the CSS Fonts Level 4 §5.2 nearest-match already used elsewhere). A call without a code point matches on the family alone. The code point threads through `FontResolvingOptions.Codepoint` to `LoadedTypeface.GetOrCreateForCodepoint`, which keys the resolved typeface by the *face it resolved to*, not by the codepoint — so a multi-script run reuses one typeface per face rather than spawning one per character.

#### Content-addressed font identity

PeachPDF identifies a custom font by its bytes, not by its self-reported internal name — the same model browsers use. Two different files that happen to share an internal name (a common webfont-subset pattern, where each subset file is named identically) therefore do not collide: `FontResolver` disambiguates on a content hash (a SHA-256 of the bytes, so a font made to collide with another cannot take its place; only when a genuine collision occurs, so the common case keeps `FaceName == internal name`), the PDF writer folds `Typeface.ContentHash` into its font-table cache key, and the name-keyed cache (`FontFactory.CacheFontSource`) tolerates a second same-name/different-bytes entry instead of throwing.

#### Astral codepoints and emoji

Supplementary-plane code points above U+FFFF — where nearly all emoji live — resolve through a `cmap` **format-12** subtable ([Fonts/OpenType/OpenTypeFontTables.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/drawing/PeachDrawing.Text/Internal/Fonts/OpenType/OpenTypeFontTables.cs)): `CMapTable.Read` selects a format-12 subtable alongside the format-4 one, `OpenTypeDescriptor.CharCodeToGlyphIndexCore` routes any codepoint `> 0xFFFF` to `CMap12.MapCodeToGlyph`, and `CMapCoverage.Extract` adds the format-12 ranges so astral characters participate in coverage-based fallback too. In the engine, `CommonUtils.IsAsianCharacter` is `Rune`-based, so an astral character's surrogate pair is never split into two lone-surrogate (→ U+FFFD) words before the font sees it.

#### OpenType shaping and bidirectional layout

Shaping is a multi-stage pipeline in the text engine's `Text` and `Text.Shaping.*` namespaces ([Internal/Text/](https://github.com/jhaygood86/PeachPDF/tree/main/src/drawing/PeachDrawing.Text/Internal/Text)), not a single method — the font-table reader (`OpenTypeDescriptor`, [Fonts/OpenType/OpenTypeDescriptor.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/drawing/PeachDrawing.Text/Internal/Fonts/OpenType/OpenTypeDescriptor.cs)) only orchestrates it:

1. **CSS → shaping request.** `TextShapingFeatureResolver` ([Html/Core/Utils/TextShapingFeatureResolver.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Html/Core/Utils/TextShapingFeatureResolver.cs)) parses `font-variant-ligatures`/`-caps`/`-numeric`/`-east-asian`, `font-feature-settings`, and `font-kerning` into a `ShapeSettings` request struct — the single shared front end both HTML (`DerivedStyle`) and SVG (`SvgTreeBuilder`) text resolve through.
2. **Per-run script and joining resolution.** `ScriptRunResolver` determines each run's Unicode script (folding `Common`/`Inherited` codepoints into the surrounding script per [UAX #24](https://www.unicode.org/reports/tr24/) §5.1) and maps it to an OpenType script tag. An Arabic/Syriac-family run additionally resolves per-character joining forms via `ArabicJoiningShaper` ([Text/Shaping/Arabic/ArabicJoiningShaper.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/drawing/PeachDrawing.Text/Internal/Text/Shaping/Arabic/ArabicJoiningShaper.cs), a direct port of HarfBuzz's `hb-ot-shaper-arabic.cc` state machine); a Devanagari/Bengali/Gujarati/Tamil run resolves Universal Shaping Engine syllable categories via `UseCategoryClassifier`/`UseSyllableScanner` ([Text/Shaping/Use/](https://github.com/jhaygood86/PeachPDF/tree/main/src/drawing/PeachDrawing.Text/Internal/Text/Shaping/Use)); a Khmer run resolves its own, separate (pre-Universal-Shaping-Engine) shaping categories via `KhmerCategoryClassifier`/`KhmerSyllableScanner` ([Text/Shaping/Khmer/](https://github.com/jhaygood86/PeachPDF/tree/main/src/drawing/PeachDrawing.Text/Internal/Text/Shaping/Khmer)).
3. **`OpenTypeDescriptor.Shape`** — the pipeline entry point — calls, in order: `GsubShaper.Shape` (GSUB), `GposPositioner.Apply` (GPOS), drops hidden-ignorable glyphs (variation selectors, unmapped default-ignorables - after `GsubShaper.MapToGlyphs` has let a variation selector swap in the preceding character's dedicated `cmap` format-14 glyph, when the font has one), and, only for a run marked for right-to-left display, reverses the shaped glyph list and remaps mirrorable glyphs (e.g. parentheses) — the final step, so it never disturbs the logical-order matching GSUB/GPOS just did.
4. **`GsubShaper.Shape`** ([Text/GsubShaper.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/drawing/PeachDrawing.Text/Internal/Text/GsubShaper.cs)) maps codepoints to nominal glyphs via `cmap`, runs the Arabic-joining, USE, or Khmer pre-stage from step 2 if requested (Khmer's own stage reorders coeng+RO/pre-base-vowel sequences *before* applying the font's `locl`/`ccmp`/basic features - the reverse of USE's own stage order, since Khmer's syllable structure is script-specific rather than governed by the shared USE grammar), then the general ordered GSUB feature pass: default-on `ccmp`/`locl`, ligatures (`liga`/`clig`/`rlig`/`dlig`/`hlig`/`calt`), caps (`smcp`/`c2sc`/`pcap`/`c2pc`/`unic`/`titl`), numeric (`lnum`/`onum`/`pnum`/`tnum`/`frac`/`afrc`/`ordn`/`zero`), east-asian (`jp78`/`83`/`90`/`04`/`smpl`/`trad`/`fwid`/`pwid`/`ruby`), and arbitrary `font-feature-settings` tags — dispatched by real GSUB lookup type (single/multiple/alternate/ligature/contextual/chaining-context/reverse-chaining), honoring GDEF mark-filtering (`GlyphSequenceFilter`) and per-language `LangSys` selection (`OpenTypeLanguageTags`, a curated set of roughly 80 languages, falling back to a font's `DefaultLangSys`).
5. **`GposPositioner.Apply`** ([Text/GposPositioner.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/drawing/PeachDrawing.Text/Internal/Text/GposPositioner.cs)) applies pair/class kerning, cursive attachment, mark-to-base/mark-to-ligature/mark-to-mark attachment, and contextual/chained-context positioning, with the same GDEF mark filtering GSUB uses.

Shaped glyphs (`PlacedGlyph`) retain their source-cluster spans and GPOS positioning deltas so measurement, visible painting, `/ToUnicode`, and `/ActualText` all describe the same result. Because GSUB rules like `rlig` (e.g. Arabic lam-alef ligation) are authored against logical adjacency, an Arabic-family run is shaped in true logical order and reversed only as the pipeline's last step (3, above); a plain right-to-left script without contextual shaping (Hebrew) is instead pre-mirrored/reversed before shaping (`CssLayoutEngine.MirrorWordTextIfNeeded`).

Bidirectional layout is a separate engine, `BidiResolver` ([Text/Bidi/](https://github.com/jhaygood86/PeachPDF/tree/main/src/drawing/PeachDrawing.Text/Internal/Text/Bidi)), implementing the full [Unicode Bidirectional Algorithm](https://www.unicode.org/reports/tr9/) — paragraph-level detection (including `unicode-bidi: plaintext`'s first-strong-character rule), explicit embeddings/overrides/isolates via a full stack model (CSS `unicode-bidi: embed`/`isolate`/`bidi-override`/`isolate-override`/`plaintext` are synthetic pushes onto that same stack), weak- and neutral-type resolution, bracket-pair resolution, implicit levels, and paragraph-level reordering, with line-level visual reordering (`ReorderLine`) exposed separately since it's scoped per line rather than per paragraph. `CssBidiParagraphResolver` (`Html/Core/Dom/`) is the CSS-facing driver: it flattens a DOM subtree into paragraph text plus the override stack `BidiResolver.Resolve` consumes. Mirrored paired characters (e.g. parentheses under RTL) are resolved as part of the same pass. Shaping runs in logical order where the script requires it; display reordering happens afterward, with the positionally aligned logical source retained for text extraction. See [Text shaping](html-css-support.md#text-shaping) for the complete feature and script boundaries.

#### Process-wide font state and thread safety

A render context and everything it owns (font, brush and pen caches, its `FontResolver`) is instance-scoped and not safe to share across threads — but the engine is designed so that one context (and, in PeachPDF, one `PdfGenerator`) per thread is safe, including the process-wide state it touches:

- System font discovery (scanning OS font directories, asking fontconfig or CoreText, and parsing TrueType/OpenType `name` tables) runs exactly once per process, in `FontResolver`'s static constructor, into immutable `FrozenDictionary` structures. Every `FontResolver` instance (one per render context) reads this once-built data without locking; `AddFont`/`AddFontFromStream` clone a family's data before overriding a style, so a custom font registered on one context can never mutate the shared system-font data seen by other instances.
- `FontFactory`'s process-wide caches (`TypefaceCache` and the font sources with their `OpenTypeFontface` records) are guarded by a single reentrant `FontLock` monitor, so concurrent contexts resolving different fonts at the same time don't corrupt each other's cache entries.

A note on tests: the engine's font caches are shared process-wide, so a test that touches `FontFactory` or its OpenType neighbours risks order-dependent flakiness under parallel test execution; this package's own test project is the one granted access to its internals.

---

## PeachDrawing.Core

**Guide:** [PeachDrawing.Core](peachdrawing-core.md) · **Source:** [src/drawing/PeachDrawing.Core/](https://github.com/jhaygood86/PeachPDF/tree/main/src/drawing/PeachDrawing.Core/)

`PeachDrawing.Core` is the drawing contract. It defines what any drawing surface must do and the paint-ready value types used to ask it, and contains no output format of its own. Layout and paint logic (the engine) targets this contract; the backends implement it. It depends only on `PeachDrawing.Text`, so the text types (`Font`, shaped runs, glyph outlines) are in the signature of the contract.

### The contract

`Canvas` ([Canvas.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/drawing/PeachDrawing.Core/Canvas.cs)) is an abstract class with a small set of drawing primitives and a few optional capabilities with inert defaults. A backend overrides what it can do and the caller falls back for the rest.

| Group | Members | Notes |
|---|---|---|
| Text | `MeasureString`, `DrawString`, `DrawStringInRect`, `DrawGlyphs`, `GetTextOutline`, `CountShapedGlyphs`, `GetInkCrossings` | `DrawGlyphs` draws by glyph id (a stretchy MathML operator variant has no code point of its own); `GetTextOutline` returns the exact outline for effects such as `text-stroke` |
| Shapes | `DrawLine`, `DrawRectangle` (pen or brush), `DrawPolygon`, `DrawPath`, `GetGraphicsPath` | A `GraphicsPath` is backend-neutral geometry (lines, béziers, elliptical arcs) with flatten, combine, measure and clip operations under `Geometry/` |
| Images | `DrawImage` (with source rectangle and `ImageSampling`), `DrawImageWithOpacity`, `DrawImageMasked`, `DrawImageAlphaMasked`, `DrawImageWithColorMatrix`, `DrawImageBlendedOver` | Images come from `RenderContext.ImageFromStream` and are opaque to the engine |
| Paint | `GetSolidBrush`, `GetLinearGradientBrush`, `GetRadialGradientBrush`, `GetConicGradientBrush`, `GetPen` | Brushes and pens are `IDisposable` objects created by the canvas so each backend keeps its own representation |
| State | `PushClip` (rectangle or path) / `PopClip`, `PushClipExclude`, `PushTransform` / `PopTransform`, `PushBlendMode` / `PopBlendMode`, `SuspendClipping` / `ResumeClipping`, `PushAntiAlias` / `PopAntiAlias` | `CurrentTransform`, `TransformScale` and `GetClip` let a caller read the state back |
| Layers and tiles | `BeginLayer(LayerOptions)` (opacity, blend, mask, clip and a `LayerEffect` such as `BlurEffect`, `DropShadowEffect`, `ColorMatrixEffect`), `CreateTile` (a reusable vector sub-canvas used for patterns and masks), `CreateRetainedLayer` (a recorded layer a host can redraw with a changing opacity/transform without repainting) | `HasNativeLayerEffects`, `PrefersRasterGroups`, `FlattensTransparency` tell a caller which route a backend wants |
| Raster fallback | `BeginRasterSurface(bounds, dpiOverride)` returns a child canvas drawing into a bitmap in the requester's own coordinate system; `DrawRaster(surface)` places the result | The seam for effects a backend cannot express as vector content; the default returns null so a measure-only or test canvas simply declines |
| Document features | `AddLink`, `AddDestinationLink`, `AddDestination`, `AddCursor`, `AddSemanticRegion`; `BeginMarkedContent`/`EndMarkedContent`, `BeginArtifact` | Default no-ops. The PDF canvas writes annotations and structure, the SVG canvas writes `<a>`, the screen backends record the regions in a map the host hit-tests |

`RenderContext` ([RenderContext.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/drawing/PeachDrawing.Core/RenderContext.cs)) is the per-document owner of resources and policy: the font set and its family mappings (`AddFontFamilyMapping`, `AddFontFromBytes`, `AddFontFamilyFromUrl`, `AddLocalFontFamily`, `GetFont`, `GetFontForCodepoint`), `ImageFromStream`, `GetResourceStream(Uri)` (resource loading seam, `RNetworkResponse` is a Core type), the text-rendering knobs (`TextHinting`, `TextStemDarkening`), the raster knobs (`RasterizationDpi`, `MaxRasterPixels`, `RasterAntiAliasing`), `LayoutUnitsPerPoint`, and `CreateSvgGlyphPainter(host)` (the hook for fonts with SVG glyphs; the engine supplies the implementation).

### Value types

Everything crossing the contract is a small plain value: `Rect`, `Size`, `PaintPoint`, `PaintColor`, `Matrix3x2` (the BCL type), `IntRect`, `ColorMatrix`, `DashStyle`, `LineCap`/`LineJoin`, `PaintBlendMode`, `FillMode`, `FontPalette`, `CursorShape`/`CursorImage`, `SemanticInfo`, `TextHinting`, `BoxAlignment`. No type in the surface names a format-specific concept.

### Shared drawing logic

The package also holds the backend-neutral logic every canvas shares, so each backend does not re-derive it:

- `ColorGlyphs/`: `ColorGlyphPainter` walks COLR v0/v1 artwork (gradients, clips, composite modes, transforms) into an `IColorGlyphTarget`; `CanvasColorGlyphTarget` draws it on any `Canvas`, and the PDF backend drives the same walker with its own target that writes Form XObjects.
- `CanvasParagraphExtensions` (`DrawParagraph`, `DrawGlyphRun`): paint a `PeachDrawing.Text` `ParagraphLayout` or shaped `GlyphRun` onto any `Canvas`.
- `RasterSurface`, `RasterRegion`, `PixelBuffer`: the premultiplied-RGBA bitmap the raster fallback hands between canvases.

### Rules that keep it a contract

- **Units.** A canvas states its unit through `PixelsPerPoint` and `TransformScale`; callers divide layout units by it. A standalone raster canvas is plain device pixels (one unit is one pixel) and takes any other convention as a constructor parameter, so nothing in a standalone backend may assume it is being driven by the PDF pipeline.
- **No format leaks.** A new primitive is added here first (as a virtual member with an inert default where a backend can reasonably decline) and then implemented per backend; a member that names a PDF, SVG or Skia type does not belong in this package.
- **Test doubles.** Because the surface is abstract, the painting tests drive recording `Canvas` implementations and assert on the ordered call log.

---

## Canvas backends

Every backend implements the same `Canvas`/`RenderContext` contract from [PeachDrawing.Core](#peachdrawingcore) and can be used on its own, with no HTML engine and no PeachPDF reference. They differ in where the output goes, who owns the surface, and which effects they express natively versus through the raster fallback.

| Backend | Output | Who owns the surface | Text engine | Platforms |
|---|---|---|---|---|
| `PeachDrawing` | A premultiplied-RGBA bitmap | The package (`RasterSurface`) | `PeachDrawing.Text` | all |
| `PeachDrawing.PDF` | A PDF file | `PdfComposer` | `PeachDrawing.Text` (embedded as subset fonts) | all |
| `PeachDrawing.Svg` | One SVG document | `SvgComposer` | exact glyph outlines, or selectable `<text>` with an embedded subset font | all |
| `PeachDrawing.Skia` | An `SKCanvas` | The host (GPU or CPU surface) | `PeachDrawing.Text` or Skia's own fonts | all (the app picks the SkiaSharp native assets) |
| `PeachDrawing.Win2D` | A `CanvasDrawingSession` | The host, or an offscreen `Win2dComposer` | `PeachDrawing.Text` or DirectWrite | Windows |
| `PeachDrawing.CoreGraphics` | A `CGContext` | The host | `PeachDrawing.Text` or CoreText | macOS, iOS |

The screen backends share one rule: **every native resource has exactly one owner and a deterministic disposer**, and an internal resource counter proves in tests that none leaks. Each guide has a lifecycle table that must be read before adding a native allocation.

### PeachDrawing (CPU raster)

**Guide:** [PeachDrawing](peachdrawing.md) · **Source:** [src/drawing/PeachDrawing/](https://github.com/jhaygood86/PeachPDF/tree/main/src/drawing/PeachDrawing/) · **Key types:** `RasterCanvas` ([RasterCanvas.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/drawing/PeachDrawing/RasterCanvas.cs)), `RasterRenderContext`, `RasterSurfaceFactory`

A standalone software rasterizer: a `Canvas` that paints into a premultiplied RGBA bitmap, exportable to the image formats PeachImage encodes. Its source is the scanline engine (`ScanlineRasterizer`, `Stroker`/`PathStroker`, `PolygonSet`, `ClipState`), the compositing kernels (`PixelKernels*`, `BlendModes`, with scalar and `Vector128`/`Vector256` implementations), the text path (`RasterCanvas.Text.cs`, `TextOutlineBuilder`, `BitmapGlyphImages`), and the effects under `Filters/` (`GaussianBlur`, `DropShadow`, `Turbulence`, colour matrices) plus `Warp`/`Homography`/`DepthBuffer` for projective and 3D compositing. Its raster-region, filter and warp primitives are public so the PDF pipeline can use them without any internal access.

It is also the engine of the PDF writer's raster fallback. PDF cannot express every effect as vector content — a blur needs per-pixel convolution — so for those, `PdfCanvas.BeginRasterSurface` returns a `RasterCanvas` (built through `RasterSurfaceFactory.Create` with the PDF canvas's own `PixelsPerPoint`). It is deliberately a subordinate of a PDF render rather than a parallel pipeline: layout and text measurement still run through the PDF context, so metrics cannot drift, and the raster canvas is asked for only when a subtree needs pixels.

**Resolution.** A surface covers a layout-space rectangle at the render context's `RasterizationDpi` (`PdfGenerateConfig.RasterizationDpi` in PeachPDF) pixels per inch of paper. Its bounds are snapped outwards to whole pixels on a single grid anchored at the local origin, and the surface records the snapped rectangle; that rectangle is what the bitmap is placed at, so every pixel is exactly `1/dpi` inch and neighbouring surfaces abut without a seam. `PixelsPerPoint` is read at the moment the surface is created, so a document scaled by `ShrinkToFit` still gets the configured physical resolution, and the unit convention is the requesting canvas's, not an assumption of the raster canvas (a standalone `RasterCanvas` is plain device pixels).

**The rasterizer.** Fills use an anti-aliased scanline rasterizer (`ScanlineRasterizer`) with 16 vertical samples per pixel row and exact horizontal span coverage, evaluated per sample against either fill rule, so the even-odd rule and overlapping same-direction contours work. Strokes (`Stroker`) are built in user space — a non-uniform transform gives an elliptical pen, as in PDF — as a union of per-segment, join and cap polygons. Compositing (`PixelKernels`, `BlendModes`) is premultiplied source-over plus all sixteen blend modes. Text is filled from the same glyph outlines and shaping every canvas uses, including bitmap colour glyphs.

**Determinism.** The per-pixel compositing kernels are exact 8-bit fixed-point arithmetic with a scalar reference implementation and a `Vector128` one that are tested to produce identical bytes, and the colour-matrix and blur passes avoid fused multiply-adds and reordered reductions for the same reason. Geometry setup uses the platform's floating-point math (trigonometry, matrix composition), which is not bit-guaranteed across platforms, so output is stable for a given platform and runtime and equal across platforms to within rounding at anti-aliased edges.

### PeachDrawing.PDF

**Guide:** [PeachDrawing.PDF](peachdrawing-pdf.md) · **Source:** [src/drawing/PeachDrawing.PDF/](https://github.com/jhaygood86/PeachPDF/tree/main/src/drawing/PeachDrawing.PDF/)

The PDF writer. It has two faces: a `Canvas` implementation (`PdfCanvas`, with `PdfRenderContext`) that turns drawing calls into PDF content, and a document-level composer (`PdfComposer`) that owns pages, metadata, conformance, encryption and saving. It can be used with no PeachPDF reference at all.

| Folder | What it holds |
|---|---|
| `Backend/` | `PdfCanvas`, `PdfRenderContext`, `PdfCanvasFont`/`PdfCanvasFontFamily`/`PdfCanvasImage`, `RasterEmbedding` (how a raster-fallback bitmap is embedded), `TransparencyProbe` (asks the writer's own guard whether a paint needs transparency), `ImageDecodeCache`, `Units` |
| `Engine/` | The embedded PDF writing engine, derived from [PDFsharp](https://github.com/empira/PDFsharp) (MIT; its own [license](https://github.com/jhaygood86/PeachPDF/blob/main/src/drawing/PeachDrawing.PDF/Engine/LICENSE.md)): the object model (`PdfDocument`, `PdfDictionary`, `PdfPage`...), `Rendering/` (`PdfGraphicsRenderer` writing content-stream operators, color glyph forms), `Drawing/` (`PdfGraphics`, `PeachImageSource`, font helpers), `Advanced/` (catalog, fonts, resources, shadings, soft masks, form XObjects), `Structure/` (tagged PDF), `AcroForms/`, `Annotations/`, `Security/`, `IO/` (writer, `PdfLinearizer`) and `Filters/` |
| `Operations/` | `PdfOperation`: merge, take, overlay, attach, encrypt and linearize existing PDFs; `PdfPageSelector`, `PdfCopyMode` (`Redraw` or `ObjectCopy`) |
| `Import/` | A PDF reader (`PdfSourceDocument`, `PdfPageSource`, lexer, content interpreter, font and image importers) used by the operations and by `PdfCopyMode.Redraw`; `PdfObjectCopier` for object-level copies |
| top level | The public options and models: `PdfComposer`, `PdfComposerPage`, `PdfComposerOptions`, `PdfVersion`, `PdfAConformance`, `PdfXConformance`, `PdfUAConformance`, `ColorOptions`, `PdfEncryptionOptions`, `PdfPermissions`, `PdfBookmark`, `PdfEmbeddedFile`, `PdfFormBuilder`, `PdfAnnotationBuilder`, `PdfTagRecorder` |

**Public surface in format-neutral terms.** The API is points with a top-left origin and y pointing down; the flip to PDF's bottom-left origin happens inside the package, at the content-stream layer. The engine mirrors the option enums and classes the public surface exposes (`PdfAConformance`, `ColorOptions`...), so a new option is added in both places. The writer renumbers every object at save time, so cross-references are stored as real references, never as literals carrying object numbers.

**How a `Canvas` call becomes PDF.** `PdfCanvas` translates each call into `PdfGraphics` and then `PdfGraphicsRenderer` operators. Fills, strokes and clips become path operators; linear, radial and conic gradients become native shadings; patterns, masks and layers become Form XObjects with transparency groups; blend modes and opacity become `ExtGState` entries; images become image XObjects. The Form XObject for a `CreateTile` canvas is Y-flipped relative to its own height, and a `/Luminosity` soft mask's `/G` form and the content form it masks both need their own transparency-group dictionary or conforming readers ignore the mask. Anything PDF cannot express (see [the raster fallback](#rasterized-regions)) arrives as a bitmap through `BeginRasterSurface`/`DrawRaster`, which `PdfCanvas` encodes as a PNG and embeds through the same pass-through path any PNG takes.

**Fonts.** The writer embeds subsets of the typefaces `PeachDrawing.Text` matched. Text is written as composite `PdfType0Font`/`PdfCidFont` objects and the whole emission path is code-point based: `CharacterMapInfo.AddChars`, `FontHelper.MeasureString` and `PdfGraphicsRenderer.DrawString` iterate `Rune`s, glyph indices are keyed by an `int` code point, and `PdfToUnicodeMap` emits a UTF-16 `bfrange` `ToUnicode` map (an astral code point becomes a surrogate pair, keeping copy/paste and text extraction correct). A font is identified by its bytes: `PdfFontTable` folds the typeface's content hash into its cache key, so two different files that share an internal name do not collide.

**Colour glyphs.** For `COLR`/`CPAL` fonts the writer decodes the COLR paint graph (through Core's shared `ColorGlyphPainter`) and writes the visible glyph as native PDF paths, gradients, clips and compositing. Each distinct glyph's artwork is written once as a Form XObject at a canonical em size and each occurrence invokes it with a placement transform, so one stored copy serves every font size and page. The artwork is keyed by everything that changes how it looks — font, glyph, selected `CPAL` palette and `font-palette` overrides, and the text colour a paint can name through the "use foreground" sentinel — but not by size or position. The text stays selectable through an invisible (render mode 3) text object per occurrence, with `/ToUnicode` and a per-glyph `/ActualText` span; that text belongs to the page, not to the shared artwork, and an empty COLR base glyph gets a tiny hidden contour in the selection subset because PDFium otherwise omits its CID from selection and extraction. `CBDT`/`CBLC` and `sbix` bitmap glyphs are drawn as the font's own pictures, and OpenType SVG glyphs as vector artwork through the render context's SVG glyph painter. See [Color emoji and font coverage](html-css-support.md#per-character-font-matching-and-coverage-fallback) for the author-facing boundaries.

**Images.** Every image arrives through PeachImage. JPEG, PNG, GIF and JPEG XL sources that qualify are embedded byte for byte; anything else is decoded to a 32-bit bitmap that `PdfImage` splits into RGB samples, a 1-bit `/Mask` and an `/SMask`, then compresses and writes as an image XObject. `PeachImageSource` (`Engine/Drawing/`) routes a CMYK or YCCK JPEG around the usual forced-RGBA decode, which has no colour management and would destroy print separations; a CMYK JPEG, and an RGB or grayscale JPEG carrying a usable ICC profile, are embedded by pass-through instead (see [Color and ICC profiles](usage-examples.md#color-and-icc-profiles)). Decoders report EXIF orientation without applying it, so a pass-through JPEG is never re-encoded to rotate it: orientation becomes a matrix around the draw call.

**Conformance.** PDF/A (parts 1–4), PDF/X (X-1a, X-3, X-4, X-6), PDF/UA, PDF 1.x/2.0, encryption, linearization and ICC output intents are applied by `PdfComposer` and enforced by guards inside the engine (`PdfATransparencyGuard`, `PdfACmykImageGuard`, `PdfXColorSpaceGuard`, `PdfColorConversionGuard`), which throw when a paint would violate the chosen standard. The guards are also what the transparency-flattening probe consults, so there is one list of "constructs that need transparency".

**Tagged PDF.** `PdfTagRecorder` owns the structure tree objects: one `PdfStructureTreeRoot` with a synthetic root `/Document` element, a box → `PdfStructureElement` cache (so a box painted across a page break attaches its continued content to the same element through a `/MCR`), a per-page MCID counter (MCIDs are unique only within a page), and the page-keyed `/ParentTree` (`PdfNumberTreeNode`) bookkeeping. `Canvas.BeginMarkedContent`/`EndMarkedContent`/`BeginArtifact` bracket the content; links and form widgets are tied to their structure element with an `/OBJR` and their own `/StructParent` entry, drawn from the same `ParentTreeNextKey` counter. The engine side of tagging is described under [Tagged PDF](#tagged-pdf-pdfua-structure-tree).

**Forms and annotations.** `PdfFormBuilder` writes AcroForm fields (text, checkbox, radio, select, list, push button and unsigned signature fields) with appearance streams; `PdfAnnotationBuilder` with `PdfAnnotationAppearance` writes text marks, shapes, free text, stamps, ink and file attachments. Both take plain models, so a caller with no HTML can write the same objects PeachPDF does.

**Thread safety and caches.** Font discovery is process-wide and immutable; a `PdfComposer` and its `PdfRenderContext` are instance-scoped. A `PdfComposer` is not safe to share across threads.

### PeachDrawing.Svg

**Guide:** [PeachDrawing.Svg](peachdrawing-svg.md) · **Source:** [src/drawing/PeachDrawing.Svg/](https://github.com/jhaygood86/PeachPDF/tree/main/src/drawing/PeachDrawing.Svg/)

A vector SVG backend with no PeachPDF dependency. `SvgCanvas` (partials: main, `.Images`, `.Text`, `.Links`) writes each call as SVG: paths from their curves, brushes as gradients and patterns, clips as `<clipPath>` groups, layers and blend modes as CSS compositing and `<filter>`, images embedded as they came and defined once, links as `<a>`. `SvgDefs` is the shared definitions block and id allocator. Text is written as exact glyph outlines (each glyph a `<use>` of one definition) or, with `SvgTextMode.Text`, as selectable `<text>` with an embedded subset font (`SvgFontPackager` adds the `name`, `OS/2` and `post` tables a browser needs). The one non-vector path is the raster fallback (`BeginRasterSurface`/`DrawRaster`, a PNG). `SvgComposer` is the document-level surface: one SVG, no pages. The test harness renders every HTML showcase through it (`--svg`).

### PeachDrawing.Skia

**Guide:** [PeachDrawing.Skia](peachdrawing-skia.md) · **Source:** [src/drawing/PeachDrawing.Skia/](https://github.com/jhaygood86/PeachPDF/tree/main/src/drawing/PeachDrawing.Skia/)

A SkiaSharp backend that **draws into a surface the host already has** — a GPU view's, a bitmap's, a picture recorder's — and never creates a window, a GPU context or a surface of its own. `SkiaCanvas.FromCanvas` borrows an `SKCanvas` (its save level, matrix and clip are restored on dispose; it is never disposed or flushed); `SkiaComposer` borrows an `SKSurface`; `SkiaRenderContext` is `IDisposable`, owns the native font faces and may be told the host's `GRContext` so images upload once. Layers are `SaveLayer`, tiles are `SKPictureRecorder` pictures, blend modes are `SKBlendMode`, conic gradients are sweep shaders. Text is the shared `PeachDrawing.Text` engine (`SkiaFont`) by default, or Skia's own fonts (`NativeSkiaFont`) shaped by a basic built-in shaper (`PeachTextShaper`) or one the host supplies through `ISkiaTextShaper`. Images decode with PeachImage or Skia's codecs (`SkiaImageCodec`, `Auto` by default). Links, cursor areas and semantic regions are recorded in `SkiaLinkMap`; there is **no default launcher**, because a platform-neutral package cannot open a URI, so the host supplies an `ISkiaLinkLauncher` or handles `LinkActivating`.

### PeachDrawing.Win2D

**Guide:** [PeachDrawing.Win2D](peachdrawing-win2d.md) · **Source:** [src/drawing/PeachDrawing.Win2D/](https://github.com/jhaygood86/PeachPDF/tree/main/src/drawing/PeachDrawing.Win2D/)

A Win2D backend (Windows only, `net10.0-windows10.0.19041.0`). `Win2dCanvas` (partials: main, `.Brushes`, `.Images`, `.Layers`, `.Text`, `.GpuText`, `.Links`) draws each call into a `CanvasDrawingSession` — a caller's (`FromSession`, borrowed, never closed) or a `Win2dComposer`'s own offscreen `CanvasRenderTarget`. `Win2dRenderContext` is `IDisposable` and owns or borrows the `CanvasDevice`. `Win2dRetainedLayer` keeps a `RetainedLayer` as a render-target texture, and `Shaders/` holds the pixel shaders (conic gradients, text masks). Text is the shared `PeachDrawing.Text` engine by default (`Win2dFont`) or DirectWrite (`DirectWriteFont`, `Win2dTextEngine`), the latter deliberately not feature complete and documented in the guide's table. Images decode with PeachImage or WIC (`Win2dImageCodec`). `Win2dLinkMap` records links, cursor areas and semantic regions for the host to hit-test and open with `Windows.System.Launcher` (through a scheme allowlist). `Win2dResourceCounts` counts native allocations so the lifecycle tests can prove nothing leaks.

### PeachDrawing.CoreGraphics

**Guide:** [PeachDrawing.CoreGraphics](peachdrawing-coregraphics.md) · **Source:** [src/drawing/PeachDrawing.CoreGraphics/](https://github.com/jhaygood86/PeachPDF/tree/main/src/drawing/PeachDrawing.CoreGraphics/)

A CoreGraphics backend for macOS and iOS (`net10.0-macos`, `net10.0-ios`; it needs a Mac with the workloads, so it is outside the main solution file). **The consumer supplies the context**: `CoreGraphicsCanvas.FromContext` borrows a `CGContext`, flips a bottom-left origin to y-down unless told otherwise, and restores its graphics state on dispose. Layers, tiles and masks are offscreen `CGBitmapContext`s composited back (`CoreGraphicsOffscreen`); blur, drop shadow, colour matrices and luminance masks run in Core Image (`CoreGraphicsEffects`, Metal when a device exists); conic gradients are computed per pixel on the CPU (`ConicPixels`). Text is the shared engine (`CoreGraphicsFont`) or CoreText (`CoreGraphicsNativeFont`, not feature complete); images decode with PeachImage or ImageIO (`CoreGraphicsImageCodec`: `Native`, or `Auto`, which keeps encoded bytes only when the context has a Metal device). Native interop the bindings leave a gap in is source-generated `[LibraryImport]` only. Links, cursors and semantic regions are in `CoreGraphicsLinkMap`, again with no default launcher.

---

---

## PeachPDF.SourceGenerators

**Source:** [src/PeachPDF.SourceGenerators/](https://github.com/jhaygood86/PeachPDF/tree/main/src/PeachPDF.SourceGenerators/) · input: [css-properties.json](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/css-properties.json)

How the engine applies a CSS or SVG property to a box — inheritance, initial value, where it is stored, what it accepts — is data, not code. `css-properties.json` (with its schema) is the single source of truth; a Roslyn incremental source generator (`CssPropertyGenerator`, a `netstandard2.0` analyzer referenced by `PeachBrackets`, never shipped) reads it at build time and emits:

- `CssPropertyRegistry` and `SvgPropertyRegistry`: a bool-returning `TrySet` and `Get` per longhand, plus a validation-only `SupportsDeclaration` that real `@supports` evaluation calls to ask whether this renderer accepts a declaration;
- the `ComputedStyle` area records and the `CssBox` style-property accessors;
- one CSS-OM `Property` class per longhand under `CSS/StyleProperties/`, wrapping the grammar named in the entry's `cssom` block (the CSS-wide keywords added around it).

An entry's `cssDataType` names a grammar (`length`, `color`, `keyword` with its supported values, `integer`, an enum, `svg-paint`...) that the generator validates by calling the engine's own real parsers, never a second derived grammar. A custom setter or validator (raw C#) exists for the few properties that fit no grammar — a multi-field write like `font-family`, or a value remap — and is kept rare. The generator checks each entry's property path and data type against the real `CssBox`/`SvgElement` shape at build time, so a mismatch is a build error naming both files (`PPG010`/`PPG011`) rather than a silently wrong table. The `PPGxxx` diagnostics (`Diagnostics.cs`) are the first thing to read when a build breaks on a JSON change. Hand-written code still handles the few properties that are not a named longhand (`CustomProperty`, `UnknownProperty`, vendor aliases) and the SVG properties whose handling has side effects (`clip-path`, `mask`, `marker`).

---

---

## PeachBrackets (the HTML engine)

**Guide:** [PeachBrackets](peachbrackets.md) · **Source:** [src/engine/PeachBrackets/](https://github.com/jhaygood86/PeachPDF/tree/main/src/engine/PeachBrackets/)

PeachBrackets is the part that understands HTML, CSS, SVG and MathML. It parses a document, resolves styles, lays the content out into pages (or a continuous screen flow), and paints any page onto any [`Canvas`](#peachdrawingcore). It depends on the text engine, the drawing contract, the raster backend and PeachImage, but **not on the PDF writer**: the same engine drives the PDF product, the three screen views and the SVG harness. The pipeline below is the heart of the library; the sections that follow walk it phase by phase.

```
HTML input
    │
    ▼
HTML parsing (MimeKit tokenizer, HTML Standard tree construction)
    │
    ▼
DOM construction (CssBox tree)
    │
    ▼
CSS parsing (CSS-OM, an ExCSS-derived parser)
    │
    ▼
Stylesheet application (cascade, @page, @font-face)
    │
    ▼
Layout (CssLayoutEngine: block, inline, flex, grid, columns, tables, pagination)
    │
    ▼
Fragmentation (the immutable FragmentTree)
    │
    ▼
Painting (FragmentPainter / PagePainter onto a Canvas)
```

SVG and MathML are cross-cutting subsystems that plug into the DOM, layout and painting phases as "foreign content"; see [SVG rendering](#svg-rendering) and [MathML rendering](#mathml-rendering).

### Public surface

The public surface is `HtmlDocument` and its small satellite types; everything else is `internal`.

- **`HtmlDocument`** — `LoadAsync` (HTML in, with a `RenderContext` and `HtmlDocumentOptions`), `LayoutAsync(measuringCanvas)`, `PaintPageAsync(pageIndex, canvas)`, `PaintPageRegionAsync` (a region and a `DocumentPaintLayer`, for views), `PaintLinks`, plus the screen-facing members a view needs: `GetCursorAt`, `GetInteractionAt`, `HitTest`, selection and text (`GetText`, `FindText`, `GetSelectionRects`), scrolling, form state and submission, editing, validation, `GetAccessibilityTree`, and `AdvanceAnimations`.
- **Options and results** — `HtmlDocumentOptions`, `DocumentPage`, `HtmlDocumentMetadata`, `ColorSchemePreference`, `ClippedTextEntry` and `HtmlRenderException`/`HtmlRenderErrorType`.
- **The annotation model** (`PeachBrackets.Annotations`: `DocumentAnnotations`, `PageLink`, `Bookmark`, `PageFormField`, `FormFieldClassification`...) — a format-neutral description of what a reader of the pages can click or navigate. `HtmlDocument` computes it; a PDF host writes it as link annotations, an outline and AcroForm fields, an SVG host as `<a>`, a screen host as hit targets.
- **The tagging seam** (`PeachBrackets.Tagging`: `IStructureTagSink`, `StructureNode`) — the engine reports structure (a heading, a table cell, a figure) as it paints; the host decides what to do with it. The PDF product turns it into a tagged-PDF tree.
- **The building seam** (`PeachBrackets.Building`: `DeclarativePage`, `DocumentBuildContext`, `DocumentNode`, `StyleSheet`, `SvgImage`) — what the declarative page builders in PeachPDF fill; the engine lays a node tree out exactly as it would parsed HTML.
- **Host helpers** — `FontDefaults`, `DataUri`, `HostResourceLoader`, `IResourcePrefetcher`, `Svg.SvgGlyphPainter`, `InputCapabilities` and `UserPreferences`.

### Source map

| Folder | What it holds |
|---|---|
| `CSS/` | The CSS object model: tokenizer and parser (`Parser/`, `Tokens/`), selectors, rules (`Rules/`), value converters and grammars, `calc()`, media features, and `StyleProperties/` (generated, one class per longhand). The `css-properties.json` source of truth and its schema sit beside the project |
| `Html/Core/Parse/` | `HtmlParser` (+ `.TreeConstruction`), `DomParser` (HTML parse and cascade orchestration), `CssParser`, `CssValueParser`, `CssVarResolver` |
| `Html/Core/Dom/` | `CssBox` (partials), `ComputedStyle`/`DerivedStyle`, the layout engines (`CssLayoutEngine`, `…Flex`, `…Grid`, `…Table`, `…Columns`), generated-content engines (`CssContentEngine`, `CssCounterEngine`, `CssNamedStringEngine`), margin boxes, running elements, footnotes |
| `Html/Core/Fragmentation/` | The break-token model: `BreakToken`, `FragmentainerContext`, `FragmentEmitter`, `EarlyBreak`, the per-engine tokens and records |
| `Html/Core/Fragments/` | The immutable output: `Fragment.cs` (`FragmentTree`, `FragmentainerFragment`, `BoxFragment`, `LineFragment`, `TextFragment`) |
| `Html/Core/Paint/` | `FragmentPainter` (partials by concern: decorations, text, clips, masks, outlines, raster effects, backdrop, 3D, scrollbars, selection), `StackingOrder`, and `Content/` (one `IFragmentContentPainter` per replaced or special box kind) |
| `Html/Core/Handlers/` | Border, background, image and outline draw handlers, `ImageLoadHandler`, `StylesheetLoadHandler`, the structure-tag mapper, annotation placement |
| `Html/Core/Animation/`, `Updates/`, `Semantics/`, `Annotations/` | Animation clock and interpolation; restyle on interaction state; the accessibility-tree builder and role resolver; annotation collectors |
| `Html/Pages/` | `PagePainter`: paints one page's layers (the `@page` background and border, canvas background, content, footnotes, margin boxes) onto a `Canvas` |
| `Svg/`, `MathML/` | The native SVG and MathML tree builders, layout and renderers |
| `Building/`, `Accessibility/`, `Selection/`, `Updates/`, `Text/` | The public building, accessibility-tree, selection, update-result and text-run types |

### How a host drives it

A host loads once, lays out once for its page geometry and then paints as often as it likes: `LoadAsync` parses and cascades; `LayoutAsync` fills fragmentainers until all content is placed and materializes the fragment tree; `PaintPageAsync` creates a `FragmentPainter` for one page and paints that page's fragments. Because painting reads only the immutable fragment tree, a view can repaint regions, tiles or whole pages at will. `HtmlDocumentOptions.AnimationsEnabled` additionally creates an `AnimationClock` that `AdvanceAnimations` steps each frame, sampling animations, transitions and animated images onto the boxes without a layout pass; a box animating `transform` or `opacity` is kept as a `RetainedLayer` so the backend redraws it instead of repainting.

### Resource access

The engine never opens a file or a socket itself. Every external resource — a stylesheet, an image, a font, a `<link>` target — is requested through the `RenderContext` it was given (`GetResourceStream(Uri)`), which is how the host decides what is reachable. `HostResourceLoader` is the shared helper views use for `file:`, `data:` and HTTP; `IResourcePrefetcher` lets a host start fetches ahead of layout. How PeachPDF answers those requests is under [Resource loading](#resource-loading).

### Lineage

The core rendering engine originally derived from [HtmlRenderer](https://github.com/ArthurHub/HTML-Renderer). It has since been substantially rewritten: modern CSS standards are far better supported, the output is a paginated fragment tree rather than a single strip, and the engine is format-neutral. Behavior that matters only to a screen (scroll containers, form editing, hit testing, animation) is part of the engine for the hosts that show it, and is inert when a document is rendered to a PDF.

---

### HTML parsing

**Library:** [MimeKit](https://github.com/jstedfast/MimeKit)

**Entry point:** `HtmlParser.ParseDocument` ([Html/Core/Parse/HtmlParser.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Html/Core/Parse/HtmlParser.cs))

Raw HTML is tokenized by MimeKit's `HtmlTokenizer`, which produces a flat stream of `HtmlTagToken` and `HtmlDataToken` objects. `HtmlParser` walks that stream and builds a `CssBox` tree in a single pass, following the in-body rules of the HTML Standard's [tree construction](https://html.spec.whatwg.org/multipage/parsing.html#tree-construction).

#### Token handling

The parser keeps the standard's _stack of open elements_ (its top is the _current box_ new content goes into; a stack of its own rather than the chain of parents, because an element moved in front of a table by foster parenting is on the stack above the table without being inside it) and a _list of active formatting elements_ (`HtmlParser.TreeConstruction.cs`), which remembers the formatting elements (`<b>`, `<i>`, `<a>`...) that were opened, with markers for the table cells, captions, `<applet>`, `<object>` and `<marquee>` they must not outlive:

- **Opening tag** — a new `CssBox` is created as a child of the current box. If the tag is a container element the cursor descends into the new box; if it is a void element (`<br>`, `<img>`, `<hr>`, etc.) the cursor stays at the current level.
- **Closing tag** — a formatting element's end tag runs the [adoption agency algorithm](https://html.spec.whatwg.org/multipage/parsing.html#adoption-agency-algorithm): when a block is open inside the element, the block is moved out and the element is copied around its content, so `<b>1<p>2</b>3</p>` is a bold `1` and a paragraph holding a bold `2` and a plain `3`. Every other end tag closes the nearest open element of its name that is _in scope_ (a table cell, `<button>`, `<caption>`... stop the search), and is ignored when a special element such as a `<div>` is in the way. Content after an ignored end tag stays where it was.
- **Optional end tags** — HTML5 allows certain end tags to be omitted (e.g. `</p>`, `</li>`, `</dt>`/`</dd>`, `</td>`/`</th>`, `</tr>`, `</thead>`/`</tbody>`, `</option>`, `</optgroup>`). A start tag that ends an open `<p>` (in button scope), `<li>`, `<dd>`/`<dt>` or heading does so wherever that element is open; cells, rows, table sections and options are closed by the table and select insertion modes below. A formatting element still open when its container ends is _reconstructed_: it is opened again, as a copy, inside the next element or text that follows, so `<li><b>a<li>b` is two items, both bold.
- **Document modes** — a whole document goes through the standard's _initial_, _before html_, _before head_, _in head_, _after head_, _in body_, _after body_ and _after after body_ modes (`DocumentPhase`), so the `html`, `head` and `body` elements the source leaves out exist (an empty document is `<html><head></head><body></body></html>`), head content after the head goes into it, a repeated `<html>` or `<body>` only adds its missing attributes, and content after `</body>` or `</html>` still goes into the body. A fragment (an `innerHTML`-style splice, or the tests' direct calls) starts in _in body_ and gets no skeleton. `<template>` keeps a stack of template insertion modes: its contents parse by their first tag (a `<tr>` as a row, a `<td>` as a cell) and stay inert.
- **Table and select modes** — the insertion mode is not stored: it is the innermost table part, cell, caption or `<select>` on the stack (the standard switches mode exactly when it pushes or pops those). Each mode answers the tokens the standard gives it a rule for: a `<tr>` in a `<table>` gets a `<tbody>`, a `<td>` a `<tr>`, a `<col>` a `<colgroup>`; a `<td>`, `<tr>` or `<caption>` outside a table is dropped; a cell closes when a sibling cell, a row or a section starts or its table ends; options close each other. Anything else a table cannot hold (text, `<div>`, `<b>`...) is _foster parented_: inserted in front of the table instead, with the formatting elements reconstructed there.
- **Text data** — a lightweight anonymous `CssBox` is created to hold the raw text string; the text is later tokenised into individual words during the layout phase. **HTML character references** (`&amp;`, `&nbsp;`, `&#65;`, etc.) are decoded once by `HtmlTokenizer` at parse time; the text stored in `CssBox.Text` is always post-decode, so later phases (word-splitting, rendering) never decode again.
- **Ignored token kinds** — `Comment`, `DocType`, and `ScriptData` tokens are discarded; `CData` content is also silently dropped.
- **`<noscript>`** — its text content is recursively re-parsed as HTML so that the fallback markup is included in the box tree (PeachPDF never executes JavaScript).

#### MHTML support

MimeKit also drives MHTML ingestion. `MimeKitNetworkLoader` opens the MIME multipart, extracts the root HTML part, and makes all embedded resources (stylesheets, images, fonts) available by their `Content-Location` URL before the rendering pipeline begins. This means the HTML parser and all subsequent phases see a normal document; the resource-loading layer transparently resolves references into the MIME archive.

### DOM model — CssBox

**Key types:** `CssBox` ([Html/Core/Dom/CssBox.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Html/Core/Dom/CssBox.cs)), `ComputedStyle` ([Html/Core/Dom/ComputedStyle.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Html/Core/Dom/ComputedStyle.cs)), `DerivedStyle` ([Html/Core/Dom/DerivedStyle.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Html/Core/Dom/DerivedStyle.cs))

Each HTML element becomes a `CssBox`. The tree mirrors the source HTML structure and is the central data structure that all subsequent phases read from and write to. Every box is assigned a monotonically increasing `Id` so boxes can be identified and ordered throughout the pipeline.

#### Properties

`CssBox` doesn't store CSS property state directly — `box.Color`, `box.ActualFont`, and every other property accessor are thin members (split across hand-written `CssBox.StyleProperties.cs` and generated `CssBox.StyleProperties.g.cs`, the same source generator described under [Stylesheet application](#stylesheet-application) below) that read and write two records the box owns:

- **`ComputedStyle`** holds the cascaded (specified) value of every CSS property the engine understands, split into 16 small immutable "area" records (`ComputedStyleAreas.cs` plus generated `ComputedStyleAreas.g.cs`), each covering roughly one CSS module (`BorderArea`, `FontArea`, `TextArea`, `GridArea`, and so on). Every area is copy-on-write: changing one property clones only the area it lives in, not the other ~130 properties, and `CssBox.InheritStyle` can adopt a whole area directly by reference from the parent when nothing in it differs anywhere up the ancestor chain — so a subtree that never overrides any font property ends up with every box's `Font` area `ReferenceEquals` the same object as the ancestor that last set it.
- **`DerivedStyle`** holds the `Actual*` family (`ActualColor`, `ActualFont`, `ActualBorderTopWidth`, actual padding/corner-radii/word-spacing/text-indent/border-spacing, and so on) — computed from `ComputedStyle` and cached lazily the first time each is read, invalidated when the cascaded property it derives from is rewritten. One `DerivedStyle` instance per `CssBox`, created once in the box's constructor.

Layout-engine output (`Location`, `Size`, `Bounds`, and similar) is not part of either record — those are assigned directly by the layout algorithm and stay as plain members on `CssBox`.

#### Box display and flow

`CssBox` exposes several boolean helpers that the layout and painting phases query constantly:

| Property | Meaning |
|---|---|
| `IsBlock` | `display: block` |
| `IsInline` | `display: inline`, `inline-block`, or `inline-table` |
| `IsFloated` | `float: left` or `float: right` |
| `IsOutOfFlow` | floated, absolutely, or fixed positioned |
| `IsFixed` | this box or any ancestor has `position: fixed` |
| `IsTableRowGroupBox` | `display: table-row-group/header-group/footer-group` |
| `IsTableCell` | `display: table-cell` |

The _containing block_ for a box is found by walking up the parent chain to the nearest block-level, table, or table-cell box.

#### Inline content

Text inside a box is held in a `Words` list of `CssRect` objects:

- `CssRectWord` — a single word or whitespace run. Its dimensions are set during word measurement.
- `CssRectImage` — an inline-replaced image whose dimensions are set by `CssLayoutEngine.MeasureImageSize`.

Words are collected into `CssLineBox` instances during layout. After layout, each box records its per-line paint rectangles in a `Dictionary<CssLineBox, Rect> Rectangles` map so the painting phase knows exactly where each fragment of the box appears on each line.

#### Specialised subtypes

| Subtype | Purpose |
|---|---|
| `CssBoxImage` | `<img>` — manages an `ImageLoadHandler` to load and decode the image |
| `CssBoxSvg` | `<svg>` — foreign content; see [SVG rendering](#svg-rendering) below |
| `CssBoxMath` | `<math>` — foreign content; see [MathML rendering](#mathml-rendering) below |
| `CssBoxFrame` | `<iframe>` |
| `CssSpacingBox` | Anonymous spacing boxes injected into inline formatting contexts |
| `CssProxyBox` | Wrapper boxes created by the table layout engine to satisfy CSS anonymous box rules |

#### Pseudo-elements and generated content

`::before` and `::after` pseudo-elements are represented as real `CssBox` instances with `IsBeforePseudoElement` / `IsAfterPseudoElement` flags set. The `CssContentEngine` evaluates the `content` CSS property on these boxes, resolving string literals, `counter()` references, `string()` named-string lookups, and `attr()` expressions into plain text that is injected as anonymous text boxes.

When the `content` value is an image — `url()` or any CSS gradient function — `CssContentEngine.ApplyContent` sets `CssBox.ContentImage` instead of `CssBox.Text`. The box is kept in the tree as a non-text box; its image is loaded via `EnsureLoadedAsync` during word measurement and painted via `CssImagePainter.Paint` into the box's client rectangle. This uses the same `CssImage` pipeline as `background-image` and `list-style-image`. Image content requires `display: inline-block` with explicit `width`/`height` on the pseudo-element.

CSS counters (`counter-reset`, `counter-increment`) are tracked per-box by `CssCounterEngine`. Named strings (`string-set`) used for running headers and footers are tracked by `CssNamedStringEngine`.

#### Pagination metadata

Because the output is paginated, `CssBox` carries a `PageBreakBottoms` dictionary that `CssLayoutEngineTable` populates when a table row group breaks across pages. The painting phase uses this to clip table borders to the actual content height on each page rather than drawing them beyond the page break.

### CSS parsing

**Library:** ExCSS (forked and merged into PeachBrackets)

**Entry point:** `CssParser.ParseStyleSheet` ([Html/Core/Parse/CssParser.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Html/Core/Parse/CssParser.cs))

CSS is parsed by a fork of [ExCSS](https://github.com/TylerBrinks/ExCSS) merged directly into the PeachBrackets source tree under `src/engine/PeachBrackets/CSS/`. The fork exists for two reasons:

1. **Internal API access.** ExCSS does not expose the internals of a parsed stylesheet through its public API. PeachPDF needs direct access to the parsed token and rule structures to efficiently resolve selectors, apply the cascade, and evaluate property values. Merging the library removes the public-API boundary and gives the rendering engine full access to those internals.

2. **Independent CSS property support.** Adding support for a new CSS property (e.g. `background`, gradients, `container`) requires changes to the parser. With ExCSS as an external dependency this would mean waiting on an upstream release; with the fork merged in, new properties can be added and shipped immediately.

#### Rule types

The parsed output is a `Stylesheet` object containing a typed collection of at-rules and style rules. The rule types are defined under [src/engine/PeachBrackets/CSS/Rules/](https://github.com/jhaygood86/PeachPDF/tree/main/src/engine/PeachBrackets/CSS/Rules/):

| Rule class | CSS construct |
|---|---|
| `StyleRule` (via `IStyleRule`) | Regular selector + declaration block |
| `MediaRule` | `@media` — wraps child rules with a media query |
| `FontFaceRule` | `@font-face` |
| `ImportRule` | `@import` |
| `ContainerRule` | `@container` |
| `KeyframesRule` | `@keyframes` (sampled once per animation at `PdfGenerateConfig.AnimationProgress` (or the host's animation clock); see [Animations](html-css-support.md#animations)) |
| `ViewportRule` | `@viewport` |
| `DocumentRule` | `@document` |

#### Property definitions

Every CSS property PeachPDF understands has a corresponding class under [src/engine/PeachBrackets/CSS/StyleProperties/](https://github.com/jhaygood86/PeachPDF/tree/main/src/engine/PeachBrackets/CSS/StyleProperties/), organised by category (Background, Border, Font, Text, etc.). Each property class declares the property name, whether it is inherited, its initial value, and a value converter that validates and normalises the parsed token stream.

#### Value parsing

`CssValueParser` ([Html/Core/Parse/CssValueParser.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Html/Core/Parse/CssValueParser.cs)) translates raw CSS value strings into the numeric/computed forms that `DerivedStyle` caches in its `Actual*` properties (see [DOM model](#dom-model--cssbox) above). It handles length resolution (px, em, rem, %), colour parsing, and shorthand expansion. All unit→number conversion is centralized in the CSS-OM `Length` struct's `ToPixels` ([CSS/Values/Length.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/CSS/Values/Length.cs)): the engine's internal layout unit is 1 PDF point, and every unit — including spec-correct CSS `px` at `1px = 1/96in = 0.75pt` via the single `Length.PointsPerPx` constant — resolves through that one implementation, so body layout, font sizes, `@page` geometry, and image intrinsic sizes all agree by construction (see [Length units](html-css-support.md#length-units) for the full unit table).

### Stylesheet application

**Key types:** `CssData` ([Html/Core/CssData.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Html/Core/CssData.cs)), `DomParser` ([Html/Core/Parse/DomParser.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Html/Core/Parse/DomParser.cs))

`DomParser.GenerateCssTree` orchestrates both the HTML parse and the full stylesheet application in a single method call, returning the styled `CssBox` root ready for layout.

#### Stylesheet collection

Stylesheets are collected and merged from four sources in cascade order:

1. **User-agent defaults** — `CssDefaults.DefaultStyleSheet` is a string constant that mirrors the [CSS 2.1 default stylesheet for HTML](https://www.w3.org/TR/CSS21/sample.html). It sets `display` values for all HTML structural elements, default margins, font sizes for headings, monospace for `<pre>` and `<code>`, and so on. It is always loaded first.
2. **Caller-supplied stylesheet** — before rendering, the caller can pre-parse a CSS string into a `PeachPdfCssContent` object using `PdfGenerator.ParseStyleSheet(css)` and pass it to `GeneratePdf` as the `cssData` parameter. When provided, this stylesheet becomes the starting `CssData` that document stylesheets are subsequently merged into, making it behave like an additional author stylesheet applied before any document-level styles. The `combineWithDefault` parameter on `ParseStyleSheet` controls whether the caller's stylesheet is merged on top of the W3 user-agent defaults (`true`, the default) or replaces them entirely (`false`).
3. **Author stylesheets** — `CascadeParseStyles` does a depth-first walk of the raw `CssBox` tree and accumulates parsed stylesheets from `<link rel="stylesheet">` and `<style>` elements in document order. External stylesheets are fetched through the configured `RNetworkLoader` (HTTP, file system, or MHTML archive). Each parsed `Stylesheet` is appended to `CssData.Stylesheets`.
4. **Inline styles** — handled per-box during `CascadeApplyStyles` (see below).

#### @page rules

Before style cascading begins, `CascadeApplyPageStyles` reads `@page` rules from the collected stylesheets and writes their margin values (`margin-top`, `margin-right`, `margin-bottom`, `margin-left`) onto the `HtmlContainerInt`, overriding any margins specified in the host's options (`PdfGenerateConfig` in PeachPDF). It also captures a `PageLengthContext` snapshot (root em/rem size and page width and height, in true points) that per-page `@page` rules resolve their relative-unit margins against later, at band-geometry/paint time — the same bases the base rule used, so identical declarations produce identical geometry in base and per-page rules.

##### Per-page content bands (and full-page bleed)

Per-page `@page` rules (`:first`, `:left`/`:right`, or a named page) can override margins for individual pages, and a top/bottom override is **layout-affecting**: it changes that page's content-band height, so content paginates against variable bands rather than one uniform page height. That variable geometry lives in `PageGeometryTable` ([Html/Core/PageGeometryTable.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Html/Core/PageGeometryTable.cs)), which holds one `PageBandGeometry` per pagination slot: the slot's document-space band top and height (internal-pixel layout space) plus its four resolved margins (true PDF points). Slot *k*+1 begins where slot *k*'s band ends, so a page with a taller-than-base top margin shifts everything after it down.

The table is built forward-incrementally and lazily, with no fixpoint relayout: a slot's applicable rule depends only on the page number (known a priori for `:first`/`:left`/`:right`) and the named page active at the slot's *start* — and because a page-name change always forces a break onto a fresh slot (`CssBox.PerformLayoutImp`), registered at that slot's top *before* the named box's children lay out, that name is fully determined by content laid out earlier. A named-page registration invalidates only cached slots at or after its own Y (`InvalidateFrom`). Vertical overrides vary the band *height*: `HasVerticalMarginOverrides` OR `HasSizeOverrides` (a per-page `size` change alters a slot's own sheet height too, typically with no margin override at all — e.g. a landscape named page) gates the height machinery, so a document with neither stays on `HtmlContainerInt`'s closed-form uniform page arithmetic and never consults it.

Left/right overrides vary the content-box *width*: each page's own margins (and, via the same `HasSizeOverrides` gate, its own sheet width) define its measure (CSS Paged Media's "the edges of the page area act as a containing block for the layout that occurs between page breaks"), so main-column block content re-wraps to that page's own width rather than merely being shifted at paint time. `HasHorizontalMarginOverrides OR HasSizeOverrides` (`HtmlContainerInt.UseVariableInlineMeasure`) gates this the same way, and `HtmlContainerInt.PageContentRightOf(y)` is its horizontal analogue of `PageBandHeightOf` — it returns a page's own right edge (at the base left origin, computed from *that slot's own* `PageBandGeometry.SheetWidthPt`, not the document's base sheet width), which `CssLayoutEngine.GetBoxWidth` substitutes for the containing block's right edge when the containing block is the main column (`<html>`/`<body>`/the root). Layout keeps content anchored at the base left origin in layout space, so the painter's existing per-page `deltaX` translate still moves it to the page's physical left edge — with the content already at the page's own width, the right edge lands correctly and paint needs no change. Because a box's width is resolved before its position is assigned, `HtmlContainerInt.PerformLayout` runs one bounded reflow pass (keyed off each box's previous-pass `Location.Y`) until every box's page assignment stabilises; for `:first`/`:left`/`:right` this converges in a single re-pass, and a box that opens a named page now measures against that page's own geometry immediately rather than the previous page's stale, pre-registration one (`CssBox._measureResolvedAgainst`). A box's own outer border box still keeps a single (start-page) measure across its fragments, but its *text* content now re-wraps line-by-line to whichever page each line actually lands on (css-break-3 §5.1), rather than sharing one measure the way CSS Fragmentation's general model describes for a box as a whole. See the [`@page` rule](html-css-support.md#page-rule) reference for the authoring-facing behavior and its known boundaries.

**Full-page bleed** falls out of this model directly: `@page :first { margin: 0 }` collapses that slot's content band to the entire physical sheet, so a cover element sized to the full sheet reaches all four physical edges with no margin gutter. Margins are resolved in true points by `PageRuleResolver.ResolvePageMargins`, then scaled into layout space exactly once. See the [`@page` rule](html-css-support.md#page-rule) reference and the broader [CSS Paged Media](html-css-support.md#css-paged-media) section for the authoring-facing rules and their known boundaries (e.g. only main-column blocks reflow to a page's own width and a spanning block's own outer size keeps its start-page measure even though its text re-wraps; percentage heights resolve against the base band).

#### @font-face rules

`CascadeApplyStyleFonts` iterates every `@font-face` rule and resolves each font family name and source. If a `local()` source matches an installed system font it is used directly; otherwise the `url()` source is fetched through the adapter and the font file is loaded into the font subsystem before layout begins.

#### Style cascade

`CascadeApplyStyles` applies styles to every box with a recursive tree walk:

1. **Initial values** — `CssDefaults.InitialValues` are written onto the box first so every property has a defined starting point.
2. **Inheritance** — `CssBox.InheritStyle` copies inheritable properties from the parent box.
3. **Matching rules** — `CssData.GetStyleRules` yields every `IStyleRule` from every stylesheet (filtered to the `print` media type) whose selector matches the current box.
4. **`!important` tracking** — property names marked `!important` are recorded in a `HashSet<string>`. Subsequent rules cannot overwrite them.
5. **Global keywords** (`inherit`, `initial`, `unset`, `revert`, `revert-layer`) — all five are resolved at assignment time in `DomParser.AssignCssBlock`. `inherit` reads the property value from the parent box (or `initial`'s value at the root); `initial` reads from `CssDefaults.InitialValues`; `unset` acts as `inherit` for properties in `CssDefaults.InheritedProperties` and as `initial` otherwise, per spec. `revert`/`revert-layer` both resolve against a snapshot of the box's own property values taken immediately before the current cascade phase (UA/author/inline × normal/`!important`, six phases run in origin order per box) — `RulesUseRevertKeyword` skips the snapshot entirely for phases that don't use either keyword, since `CssUtils.SnapshotProperties` is otherwise a needless per-box allocation. PeachPDF doesn't model CSS cascade layers, so `revert-layer` collapses to the same behavior as `revert`. Custom properties (`--foo`) are resolved separately in `AssignCustomPropertyDeclaration`, where `initial` removes the property entirely (matching `--foo`'s guaranteed-invalid initial value) rather than resolving to a stored default.
6. **Custom properties and `var()`** — `--foo` declarations are kept in a per-box dictionary, cloned (not shared) from the parent at inheritance time so a child's local override never leaks to its parent or siblings. Regular declarations whose value contains `var()` are deferred until the box's entire cascade (UA, author, inline) has finished, then resolved in one pass via a graph-based, memoized, cycle-safe substitution against the box's final custom-property values — this makes resolution correct regardless of declaration order and safely short-circuits cyclic references (`--a: var(--b); --b: var(--a);`) instead of looping.
7. **Presentational attributes** — `TranslateAttributes` maps HTML attributes such as `align`, `width`, `border`, `bgcolor`, `valign`, `cellspacing`, and `cellpadding` to their CSS equivalents so they participate in the cascade.
8. **Inline `style` attribute** — parsed on the fly and applied last (with highest author specificity).
9. **`currentColor`** — `CssUtils.ApplyCurrentColor` resolves the `currentColor` keyword against the box's own `color`. The four `border-*-color` longhands are the exception: they keep the keyword and are resolved per box at used-value time by `DerivedStyle`, because a bevelled (`inset`/`outset`/`groove`/`ridge`) side resolves against a fixed light base rather than `color`, and because `border-color: inherit` must hand a child the unresolved keyword so the child resolves it against its own `color`.
10. **Text decoration propagation** — because `text-decoration` does not inherit through the CSS `inherit` mechanism but does visually propagate to inline children, it is explicitly copied down to child boxes that contain actual text.

`CssDefaults.InitialValues`/`InheritedProperties` and the actual per-property parse-validate-and-assign
step behind `CssUtils.SetPropertyValue`/`GetPropertyValue` (and the equivalent for SVG presentation
properties, `SvgTreeBuilder.ApplyCommon`) are generated at build time from a single JSON source,
`src/engine/PeachBrackets/css-properties.json`, by a Roslyn source generator (`src/PeachPDF.SourceGenerators`, see [PeachPDF.SourceGenerators](#peachpdfsourcegenerators)) rather
than hand-written per property. This keeps every property's inheritance flag, initial value, and value
grammar in one place instead of several hand-synchronized tables — and is exactly what real `@supports`
evaluation (§[CSS At-Rules](html-css-support.md#css-at-rules)) queries for "does this renderer actually
accept this declaration," via `CssPropertyRegistry`/`SvgPropertyRegistry`'s `SupportsDeclaration`.

#### Post-styling corrections

After the cascade, `DomParser` runs a series of correction passes to make the tree structurally valid for layout:

| Method | What it fixes |
|---|---|
| `CorrectTextBoxes` | Removes whitespace-only anonymous boxes that cannot affect layout |
| `CorrectImgBoxes` | Ensures `<img>` boxes have the correct display and size constraints |
| `CorrectLineBreaksBlocks` | Wraps `<br>` elements correctly in their inline context |
| `CorrectInlineBoxesParent` | Ensures block children of inline parents get a block wrapper (CSS anonymous block generation) |
| `CorrectAbsolutelyPositionedInlineElements` | Promotes absolutely positioned inlines to block |
| `CorrectBlockInsideInline` | Splits inline boxes that contain block-level descendants, as required by the CSS spec |
| `CorrectAnonymousTables` | Generates missing table wrapper boxes (anonymous `table`, `tbody`, `tr`) to satisfy the CSS table model |

### Layout

**Key types:** `CssLayoutEngine` ([Html/Core/Dom/CssLayoutEngine.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Html/Core/Dom/CssLayoutEngine.cs)), `CssLayoutEngineTable` ([Html/Core/Dom/CssLayoutEngineTable.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Html/Core/Dom/CssLayoutEngineTable.cs)), `CssLayoutEngineFlex` ([Html/Core/Dom/CssLayoutEngineFlex.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Html/Core/Dom/CssLayoutEngineFlex.cs)), `CssLayoutEngineGrid` ([Html/Core/Dom/CssLayoutEngineGrid.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Html/Core/Dom/CssLayoutEngineGrid.cs)), `CssLayoutEngineColumns` ([Html/Core/Dom/CssLayoutEngineColumns.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Html/Core/Dom/CssLayoutEngineColumns.cs))

Layout computes the position and size of every box. It runs in two sub-passes: word measurement and box layout.

#### Word measurement

`CssLayoutEngine.MeasureWords` does a depth-first walk of the tree and calls `CssBox.MeasureWordsSize` on each box. This calls `Canvas.MeasureString` to ask the PDF graphics context for the pixel width of each word using the box's resolved font. Image sizes are resolved by `MeasureImageSize`, which respects `width`, `height`, `min-width`, `max-width`, `min-height`, `max-height`, and aspect-ratio constraints.

#### Box sizing constraints (min/max-width, min/max-height)

`min-width`/`max-width`/`min-height`/`max-height` are enforced differently for each axis, because width and height are resolved in opposite directions:

- **Width is resolved top-down, before children lay out.** `CssLayoutEngine.GetBoxWidth` computes a box's width and hard-sets `ActualRight` *before* recursing into its children, so a `max-width`/`min-width` clamp there (max applied first, then min — min wins on conflict per CSS §10.4) correctly constrains how children wrap and position themselves, matching real browsers.
- **Height is resolved bottom-up, after children lay out.** A box's natural height comes from its already-laid-out children. `CssLayoutEngine.ApplyHeight` (called for every box right after its children finish) replaces that natural height with a definite `height`, so taller content overflows rather than growing the box; table cells are the exception because CSS tables treat their declared height as a minimum. `min-height` and `max-height` then clamp the result, with `min-height` winning if they conflict.
- **Percentages against an indefinite containing block are ignored** (treated as unset), per `CssLayoutEngine.IsHeightDefinite` — a pure, recursive function of declared CSS (definite by CSS Sizing 3 §4's own recursive definition), not a value that depends on `ApplyHeight` having already run for the ancestor in question. `CssLayoutEngine.ResolveDefiniteHeightValue` resolves the actual basis the same way, without reading the ancestor's own (bottom-up-only) `Size.Height` — except for a table cell (whose row can still stretch it, CSS 2.1 §17.5.3) or a box establishing an independent formatting context around a non-`height`-declared (ratio- or flex/grid-algorithm-derived) size that a contained float could still grow (§10.6.7), where callers fall back to the live value.
- These constraints apply uniformly to general blocks, replaced elements (images, via `MeasureImageSize`), absolutely/fixed positioned boxes, and flex items (flex items are additionally clamped on the main axis by `CssLayoutEngineFlex.ClampMainAxis`). Table cells apply `min-width`/`max-width` to the table's auto column-width distribution in `CssLayoutEngineTable`.

#### Who assigns a block box's position

Sizing a block-level box and positioning it are two different questions, and only one of them is the box's own. How wide it is follows from its own style and its containing block (`CssBox.ResolveOwnInlineSize`, over `CssLayoutEngine.GetBoxWidth`). *Where* it goes does not: the answer is margin collapsing against whatever came before it, which fragmentainer that predecessor ended in, and the run of preceding boxes chained to it by break avoidance — none of which a box can see from inside itself. So the position is assigned by the frame above it, and that is where a block box's layout is *entered*: the frame's own child loop drives each child's pass, rather than each child running its own and reaching back out mid-layout to ask where it goes.

A pass has three phases, and `CssBox.DriveBlockChildPass` runs them in the order their dependencies force:

1. **The child opens the pass** (`BeginBlockPass`): it picks up any resumption record left for it and runs its once-per-layout prologue — measuring its words, applying `string-set`, resolving its used page name, and settling whether a forced break falls before it.
2. **The frame places and sizes it** (`PlaceAndSizeBlockChild`): `ResolveBlockChildOffset` decides the offset, the child resolves its inline size against the page that offset lands on, and `CommitBlockChildOffset` writes the position. Both halves need what phase 1 settled; the size needs the offset, and nothing in the offset needs the size.
3. **The child lays out its own content** (`LayoutContents`), inside the position it was given. Everything laid out there resolves against an origin and a width that are already final.

Deciding an offset is synchronous, which is the same statement in another form: everything it depends on has already been measured by the time it runs. The frame can also decline to place the child at all — [CSS Fragmentation 3 §5.2](https://www.w3.org/TR/css-break-3/#break-margins)'s margin truncation may conclude that the break falls *before* the box, in which case no position is written, phase 3 is skipped, and the box contributes no fragment to the fragmentainer being filled.

Which children a frame positions is likewise the frame's question, asked once where the pass is entered rather than by each child about itself. A flex or grid item at its engine's commit pass is simply a child driven with the placement phase off; the block-flow arithmetic that would otherwise reposition it against a "previous sibling" the engine never used is not run at all.

The document root is the one box with no frame above it, so it stands in for its own. Nothing about it is special-cased: `PreviousInFlowSibling` reports null for a box the frame does not own, which is the answer a box with no parent has always had. Callers that are not a block-flow child loop — a layout engine measuring an item, the out-of-flow walk, the document root itself — go through `CssBox.PerformLayout`, which names the frame on the box's behalf and is otherwise the same path.

Margin collapsing is resolved there too, by `CssBox.CollapsedMarginBefore`, because [CSS 2.1 §8.3.1](https://www.w3.org/TR/CSS21/box.html#collapsing-margins)'s adjoining-margin set spans two frames. Half of it is what precedes the box — a predecessor's bottom margin and, through a run of self-collapsing predecessors, everything those fold in — and only the frame can walk its own child list backwards to find it (`FoldMarginsPrecedingChild`). The other half is the box's own top margin and the chain of first-in-flow-child margins adjoining it, which is a walk into its own subtree and stays there (`FoldOwnAdjoiningBlockStartMargins`). The spec collapses the whole set at once, so the two halves fold into one running set rather than being resolved separately and combined.

`<hr>` shows what the seam buys: a rule is an ordinary block-level box whose visible part is its border, so there is no class for it now: it takes the generic pass, and what it inherits is exactly what an empty `<div>` gets. Two box kinds still replace the generic pass rather than being driven through its phases — an `outside` `::marker` (positioned beside its item rather than in any flow) and a repeated table row group's proxy (whose content was laid out elsewhere and is only translated here) — neither of which has a prologue, a placement and a content phase that could be separated. Each still asks the frame for whatever is genuinely the frame's.

#### Block formatting context

Block boxes are stacked vertically, with margin collapsing implementing all five CSS2.1 §8.3.1 scenarios: adjoining sibling margins (`CssBox.CollapseMargins` — same-sign margins take the larger magnitude, mixed-sign margins sum, matching `Max(a,b,0) + Min(a,b,0)`); a parent's top margin escaping into its first in-flow child's when the parent has no top border/padding and the child has no clearance; a box's own bottom margin folding into its own reported height when it is its parent's last in-flow child with no bottom border/padding (`CssBox.MarginBottomCollapse`) — deliberately gated on being the last child specifically, since a box with a following sibling already has its margin accounted for via that sibling's own adjoining-margin collapse, and folding it into the box's own height too would double-count it; self-collapsing empty boxes (zero height/border/padding, no in-flow content) whose own top and bottom margins merge into one pass-through value (`CssBox.IsMarginCollapseThrough`); and floats, absolutely/fixed-positioned boxes, and boxes establishing a new block formatting context (e.g. via a non-`visible` `overflow`) never participating in any of the above. The resolved top margin for a box is cached in `_collapsedMarginTop`; a box that is itself part of a multi-level chain of adjoining first-in-flow-child margins gets its position resolved by the outermost (anchor) box's own lookahead rather than independently, since only the anchor can see the whole chain's true collapsed value before any box in it is positioned. Auto horizontal margins (`margin: … auto`) are resolved by `GetActualMarginLeft` / `GetActualMarginRight` per [CSS 2.1 §10.3.3](https://www.w3.org/TR/CSS21/visudet.html#blockwidth): on a block with a **definite** width (an explicit `width`, or an auto width clamped smaller by `max-width`) they split the free space to centre it; on an **auto-width** block they resolve to `0` so the width fills the containing block. A `max-width`d `margin: 0 auto` responsive wrapper therefore fills the page when the page is narrower than its `max-width`, and only begins centring once the page grows past it.

#### Inline formatting context

`CssLayoutEngine.CreateLineBoxes` breaks inline content into `CssLineBox` instances. Each word is placed onto the current line until it would exceed `ClientRight`, at which point a new line box is started. The algorithm then applies:

- **Horizontal alignment** — `ApplyHorizontalAlignment` shifts words for `text-align: center`, `right`, or `justify`.
- **RTL support** — `ApplyRightToLeft` mirrors word order for right-to-left content.
- **Rectangle bubbling** — `BubbleRectangles` propagates each word's rectangle up through any inline ancestor boxes so that borders and backgrounds can be painted over the correct spans.
- **Vertical alignment** — `ApplyVerticalAlignment` adjusts each word's vertical position within the line for `vertical-align` values (`top`, `middle`, `bottom`, `baseline`, `sub`, `super`, numeric offsets).
- **`overflow: hidden`** — after line boxes are created, if a box has an explicit height and `overflow: hidden`, its `ActualBottom` is clamped to `Location.Y + ActualHeight`.

#### Hyphenation

**Key type:** `HyphenationEngine` ([Text/HyphenationEngine.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/drawing/PeachDrawing.Text/Internal/Text/HyphenationEngine.cs))

`hyphens: auto` is implemented as real pattern-based automatic hyphenation — Frank Liang's classic TeX algorithm — rather than a dictionary or a heuristic. It lives in the text engine (the `PeachDrawing.Text` package, not `Html.Core.Dom`) because the algorithm itself is general text processing with no layout-engine dependency; only its two call sites are layout code:

- **Candidate generation** — `CssBox.ParseToWords` reads `HtmlContainerInt.DocumentLanguage` (populated from `<html lang="...">`, with `PdfGenerateConfig.DefaultLanguage` as a fallback for documents that declare none) and, only for `hyphens: auto` text, calls `HyphenationEngine.FindHyphenationPoints(word, language)`. The returned break-point indices are stored on the word's `CssRectWord.HyphenationCandidates` — computed once per word, not on every line-break attempt.
- **Break selection** — `CssLayoutEngine.TryHyphenateWord`, called from the line-breaking loop only when a word would otherwise overflow the line, picks the widest candidate break (measuring the prefix + a literal `-` glyph via `Canvas.MeasureString`) that still fits the remaining line width, splitting the word into a `prefix`/`suffix` `CssRectWord` pair. If no candidate fits, the word wraps whole rather than forcing an ill-fitting split.

**Pattern data.** ~70 languages' pattern sets are sourced from CTAN's `hyph-utf8` package (see [tools/Update-HyphenationPatterns.ps1](https://github.com/jhaygood86/PeachPDF/blob/main/tools/Update-HyphenationPatterns.ps1) for the reproducible download/build pipeline) and embedded as Brotli-compressed resources under `Text/Resources/Patterns/`, one file per language. Only permissively licensed pattern sets (MIT/LPPL/BSD-style/public-domain) are included; languages whose upstream pattern file is GPL/LGPL-licensed or carries no stated license are intentionally excluded — see [HTML/CSS Support: `hyphens`](html-css-support.md) for the full exclusion list. Each language is decompressed and parsed lazily on first use and then cached for the process's lifetime (`ConcurrentDictionary<string, LanguagePatternSet?>`), so a document using one language never pays to load the other ~70.

**Language resolution.** A document's language tag doesn't need to exactly match a pattern file's own tag: `HyphenationEngine.ResolveLanguageTag` tries the tag verbatim, then progressively shorter subtag prefixes (`de-AT` → `de-at` → `de`), checking at each step whether that prefix is itself an available pattern tag and, if not, consulting a small `bcp47-tag=pattern-tag` alias table (`Text/Resources/language-tags.txt`) for cases where a base language ships multiple pattern variants (e.g. `de` defaults to `de-1996`, the reformed orthography) or its real BCP-47 code differs from the pattern set's own legacy tag (e.g. `sr` → `sh-cyrl`). Each pattern file also carries its own hyphenation minimums (leftmost/rightmost characters that must remain unbroken), parsed from a `# hyphenmins: left=N right=N` comment line and applied per-language rather than as a single hard-coded constant, since they genuinely vary (e.g. Afrikaans ships `left=1 right=2` against English's `left=2 right=3`).

The alphabet-membership check that gates hyphenation (rejecting digits/punctuation/apostrophes) uses `char.IsLetter`, not an ASCII range — this is what makes non-Latin pattern sets (Cyrillic, Greek, Armenian, Georgian, Ethiopic, Thai, …) actually activate rather than silently matching zero words.

#### Float layout

Floated boxes are removed from normal flow. `CssLayoutEngine.FloatBox` positions them at the left or right edge of their containing block and records their extent in `CssFloatCoordinates` on the container. Subsequent inline content queries these coordinates to wrap around the float. The `clear` property is handled by `ClearBox`, which advances the current Y position past all floats of the specified side.

#### Fit-content / min-content / max-content

`GetFitContentWidth`, `GetMinContentWidth`, and `GetMaxContentWidth` implement the intrinsic sizing keywords used by table column widths and `width: fit-content`. They work by measuring all words and recursively summing child widths without line-breaking (max-content) or by finding the longest single word (min-content).

#### Flex layout

**Key type:** `CssLayoutEngineFlex` ([Html/Core/Dom/CssLayoutEngineFlex.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Html/Core/Dom/CssLayoutEngineFlex.cs))

Boxes with `display: flex` or `inline-flex` are laid out by a dedicated engine implementing CSS Flexbox Level 1, entered from `CssBox.PerformLayoutImp` in place of the normal block/inline formatting context. It runs as a sequence of phases per the spec: collect and order items (respecting `order`), measure each item's hypothetical main size from `flex-basis`/`width`/`height` or its content size, wrap items into lines (`flex-wrap`), resolve flexible lengths via `flex-grow`/`flex-shrink` clamped to `min`/`max-width`/`height`, size and align lines on the cross axis (`align-content`), position items on the main axis (`justify-content`, with `auto` margins absorbing free space first), and align items on the cross axis (`align-items`/`align-self`). Flex items are blockified before measurement per spec §9.2. `align-items`/`align-self: baseline` aligns items by their first font baseline — each item's baseline is found by descending into its first in-flow child (in document order) for the first line box, using `Font.Ascent` for the offset from that box's top — and only applies for row-direction flex; column-direction flex has no vertical baseline concept and falls back to `flex-start`, as does an item with no discoverable line-box content.

#### Grid layout

**Key type:** `CssLayoutEngineGrid` ([Html/Core/Dom/CssLayoutEngineGrid.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Html/Core/Dom/CssLayoutEngineGrid.cs))

Boxes with `display: grid` or `inline-grid` are laid out by a dedicated engine implementing CSS Grid Layout, entered from `CssBox.PerformLayoutImp` the same way flex/table/columns are. Track sizing follows the spec's algorithm: explicit tracks come from `grid-template-columns`/`-rows`/`-areas`, implicit tracks are created and sized from `grid-auto-columns`/`-rows` when an item is placed outside the explicit grid, and each track resolves to a fixed length, a percentage, `auto`, an intrinsic (`min-content`/`max-content`) size, or a flexible `fr` length — the last resolved via the standard flex-fraction algorithm once every non-`fr` track has claimed its space, honoring `minmax()`/`fit-content()` floors and ceilings and `repeat()` (including `auto-fill`/`auto-fit`). An item spanning multiple intrinsic tracks distributes its content contribution across them so a spanned track doesn't collapse to zero.

Placement resolves line-based positioning (`grid-column`/`-row`/`-area`, explicit line numbers, `span <n>`, named lines) and `grid-template-areas`'s named regions first, then auto-places whatever remains via `grid-auto-flow` (`row`/`column`, with `dense` packing). **Subgrid** ([CSS Grid Level 2 §9](https://developer.mozilla.org/en-US/docs/Web/CSS/CSS_grid_layout/Subgrid)) lets a grid item that is itself a grid adopt its parent's tracks along one or both axes, so a subgrid's own content sizes into the parent's shared tracks rather than tracks of its own. Box and content alignment (`justify-items`/`align-items`/`justify-content`/`align-content`/the `justify-self`/`align-self` per-item overrides) distribute leftover space the same way flexbox's alignment properties do; `align-items`/`align-self: baseline` aligns a row of items to a shared first text baseline, growing the row to the baseline group's max-ascent-plus-descent, the block-axis analogue of flexbox's row-direction baseline alignment above.

Grid's role in pagination — each row committing its content as a [§2.1](https://www.w3.org/TR/css-break-3/#parallel-flows) parallel flow in block-axis (`RowStart`-ascending) order, with a `subgrid` item's adopted track geometry re-threaded across a resumed pass — is covered in [Pagination](#pagination) below rather than repeated here. See [Grid](html-css-support.md#grid) for the full property and value reference.

#### Multi-column layout

**Key type:** `CssLayoutEngineColumns` ([Html/Core/Dom/CssLayoutEngineColumns.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Html/Core/Dom/CssLayoutEngineColumns.cs))

A box that establishes a CSS Multi-column formatting context (`column-count`/`column-width` resolved to other than `auto`) is entered from `CssBox.PerformLayoutImp` the same way flex/table are, in place of the normal block-children loop. A column is a fragmentainer in [CSS Fragmentation Level 3](https://www.w3.org/TR/css-break-3/#fragmentainer) terms, and this engine is a **driver over its own fragmentainers** — the same shape `HtmlContainerInt.LayoutDocument` has for pages:

1. **Measurement pass** — every child is laid out once, unmodified, as one tall flow at the resolved column width, with breaking suppressed. Its only product is how tall the content is, which is what `column-fill: balance` needs before it can choose a column height. Bisecting over real per-column fills would not be an acceptable cost, so the estimate is made against a whole-child packing of this pass.
2. **Per-column fill** — for each column, a nested `FragmentainerContext` is established (banded to the chosen height, with the container's own inline extent narrowed to that column), the ordinary block-children loop is run into it, and the resumption record it leaves says where the next column picks up. What the last column cannot hold travels up the ordinary chain, so the page driver opens the next page and the container resumes rather than starting over.

Every column shares one band: columns differ in the inline axis, not the block axis, which is what lets the block-axis break machinery be reused untouched — a resumed column starts at the same content edge a resumed page would. So `break-inside: avoid`, `orphans`/`widows`, [§4.1](https://www.w3.org/TR/css-break-3/#possible-breaks) monolithic content and [§5.2](https://www.w3.org/TR/css-break-3/#break-margins) margin truncation all work inside a column without knowing about columns.

Balancing belongs to the fragment holding the *end* of the flow, which cannot be known before filling it — so the first fragment starts from the estimate and a continuation starts from the full page budget and is re-balanced once the fill shows the remainder ends there. Where the estimate lands under what the real fill needs, the target is grown and the fill run again.

**A top-level child is atomic per column**, and this is a limit of the box model rather than a simplification. A `CssBox` carries a single `Location`, and columns sit side by side inside one page band — so a box split across two of them would have both halves at the same document Y and the same X, its continuation lines laid out over the ones already there, and the emitter, whose band membership is a question about Y alone, could not tell the halves apart to draw its background in both. A child too tall for its column overflows it instead. Splitting a child across columns needs geometry held per fragment rather than per box.

`column-rule` is painted as real line segments (`CssBox.ColumnRuleSegments`, set by this engine and drawn by `FragmentPainter`), one per gap between the columns actually used.

#### Table layout

`CssLayoutEngineTable` implements the CSS 2.1 fixed and auto table layout algorithms:

- **Column widths** — explicit `width` on `<col>` or `<th>`/`<td>` cells is honoured first; remaining width is distributed proportionally. Min-content widths are respected.
- **Row heights** — rows grow to fit their tallest cell. Cells with `rowspan > 1` accumulate height across rows.
- **Cell vertical alignment** — `ApplyCellVerticalAlignment` shifts cell content for `vertical-align: top`, `middle`, `bottom`, and `baseline` after row heights are known.
- **Page breaking** — when a row would cross a page boundary, the row is moved to the next page. The `<thead>` group is re-emitted on each new page, where [css-tables-3 §6.2](https://www.w3.org/TR/css-tables-3/#repeated-headers) permits repeating it — an avoiding `break-inside`, which the UA print stylesheet supplies, and a height under a quarter of the page band. `PageBreakBottoms` on the table box records the bottom Y of the last row on each page for use during border painting.
- **Repeating on a page no break falls on** — §6.2 repeats the groups on each page the table *spans*, and every site above is keyed to a break being *taken*. The one page those two questions disagree about is one a row overflows *through*, which no pass either fills or leaves. Such a row is sliced instead: `BandsSpannedBy` maps its depth onto the bands below it once each has given the groups their room, and `SliceARowAcrossTheBandsItOverflows` states one displacement per band through `FragmentEmitter.RecordFragmentDisplacement` — the row keeps its size and its single `Location`, and each fragmentainer draws it from a different origin, confined to that band's strip, so the strips meet exactly. This is [css-break-3 §4.3](https://www.w3.org/TR/css-break-3/#possible-breaks)'s "fragment the contents of monolithic elements by slicing the element's graphical representation", and it is applied only to a row no move could help — one taller than a whole band, tested against the same quantity the straddle correction fits a row against.
- **Fragmentation** — a table fills one fragmentainer per pass like any other content. Where a cell runs out of room the row loop stops at that row and publishes a `TableBreakToken` naming the row to re-enter, which of its cells to continue and from where, the ones that already finished, and the rowspan bookkeeping. The cells of one row are [§2.1](https://www.w3.org/TR/css-break-3/#parallel-flows) parallel flows, so each carries its own stopping point and a cell that finished is not entered again. What the table settled *once* — the detached `<thead>`/`<tfoot>`, their measured heights, and whether §6.2 lets each repeat — is kept in `TableSetup` and inherited by every later pass rather than re-derived, because re-deriving it would destroy the earlier pages' repeated headers.

#### Pagination

Because the output is a paginated PDF, layout must determine page breaks for all block content, not just tables. Elements with `page-break-inside: avoid` (or `break-inside: avoid`) are kept together as a unit when possible.

**Layout fills one fragmentainer at a time.** `HtmlContainerInt.LayoutDocument` is the driver: it targets a fragmentainer, runs layout into it, and where content does not fit reads back a **break token** — a resumption record naming where layout stopped — then opens the next fragmentainer and re-enters at exactly that point ([CSS Fragmentation Level 3 §2/§4.4](https://www.w3.org/TR/css-break-3/#breaking-controls)). The token is a chain, one link per ancestor between the fragmentation-context root and the box that stopped, so every ancestor on the path re-enters mid-flight while boxes off the path are untouched. Its `ResumeSlotIndex` comes from where the break actually fell rather than from "the pass after this one" — a box can be placed far down the document, past a tall spacer or by an engine positioning its own children, so the fragmentainer it overflows is not in general the next one. A document that fits without overflowing takes a single pass, so the common case costs what the old single-flow model did.

**A pass does not always fill exactly one fragmentainer, and the one it is filling is a cursor.** A forced break is realized by *placement* — the box is put at the content top of the page the break names and the pass carries straight on from there — so a pass that opened on one page can be flowing content into a page two further on with no resumption record in between (a directional `break-before: right` steps over the page it refuses, [§3.1](https://www.w3.org/TR/css-break-3/#break-between)'s "one or two page breaks"). The context naming the fragmentainer being filled moves on with it. Everything that asks about that fragmentainer — where its band ends, whether anything precedes a box inside it — then answers about the page the content is actually on rather than the one the pass started from. It moves forward only: a pass fills pages in document order, and a pass that has to reconsider an earlier one is re-entered by the driver with a context of its own.

A box's layout pass splits along the seam this needs, and the frame above it is what drives the split (see [Who assigns a block box's position](#who-assigns-a-block-boxs-position)): a **prologue** (word measurement, `string-set`, used page name, any forced break before the box) that runs once per box and must not be repeated; **placement**, which the frame performs and which a resumed pass replaces with a move to the fragmentainer now being filled; **contents**, which a resumed pass re-enters; and an **epilogue** (height, the keep-with-next first-line retry, `break-inside: avoid`, `orphans`/`widows`, the absolute right/bottom fallbacks, named-page and named-string bookkeeping) that can only judge a finished box and so waits for the pass that completes it.

One thing stays deliberately outside the token model. (A multi-column container drives fragmentainers of its own — see [Multi-column layout](#multi-column-layout) — and a table's row loop stops where a cell stopped and records where, so a later pass continues it.) **Flex and grid** measure every item at the container's content origin and translate it into place afterwards, so breaking stays suppressed for those measurements — but a final pass, once the items are where they will finally be, moves a line or row that a break value or [§4.1](https://www.w3.org/TR/css-break-3/#possible-breaks) forbids cutting onto the next page. A further, per-engine **commit pass** follows once that placement is settled, running each item's content out at its now-final position, attached to a real fragmentainer rather than a detached one, so an item's content genuinely continues onto the next page where it does not fit — the same nested `FragmentainerContext` a `column-count` descendant already establishes for itself (see [Multi-column layout](#multi-column-layout)) now reaches a live one instead of an inherited-suppressed one. `CssLayoutEngineGrid`'s runs per row (in block-axis order, which — unlike flex — needs no reordering trick, since `RowStart` ascending already is that order): every row's items are [§2.1](https://www.w3.org/TR/css-break-3/#parallel-flows) parallel flows, committed together, with a row-spanning item grouped at the row it starts and a `subgrid` item's adopted track geometry re-threaded across a resumed pass. `CssLayoutEngineFlex`'s runs per line for `flex-direction: row`/`row-reverse` (any line count, walked in block-axis order via the same index-flip `wrap-reverse` line relocation already uses) the same parallel-flows way, and separately, per line, for `column`/`column-reverse`: there each line's items are a *sequential* flow instead, so that pass walks and commits them in turn — `flex-wrap`'s several side-by-side lines each run their own sequence independently of the others. A resumed pass landing in a new fragmentainer (a multicolumn column boundary, concretely) repositions every not-yet-committed item by the same delta `CssBox.ResumeInTheNextFragmentainer` moved the container by, since that method moves only the container, not its subtree. Left out of the commit pass, for now: a forced or avoided break value *between two items* of the same column-direction line — every item there still commits unconditionally, stopping a line's walk only where an item's own content does not fit. This is an implementation constraint rather than a claim about the content, and `MonolithicContent` keeps it apart from [§4.1](https://www.w3.org/TR/css-break-3/#possible-breaks)'s own monolithic set (replaced elements, plus scroll containers that have a definite logical height or maximum, or that sit inside a multi-column container: see `MonolithicContent.IsMonolithic`; an unconstrained auto-height overflow container in ordinary block flow retains ordinary break points). And the **§4.3 corrections** — `break-inside: avoid`, [§4.1](https://www.w3.org/TR/css-break-3/#possible-breaks) monolithic content, `orphans`/`widows`, and the keep-with-next run pull they share — judge content that has *already* been placed, which a forward-only record cannot express; they stay bounded corrections within a single pass, which [§4.3](https://www.w3.org/TR/css-break-3/#possible-breaks) sanctions. Each is stated as an `EarlyBreak` (`Html/Core/Fragmentation/EarlyBreak.cs`) naming the box the break falls before, where it lands and why, and is carried out by laying that box out again there rather than by moving it — a translation would carry the fragmentainer gap into a box whose text had already crossed the boundary. Three cases keep the move: a box that fits in no fragmentainer (starting it elsewhere cannot help, and re-flowing it would restart fragmentation from its new top), a box inside an engine that owns its placement (its coordinates are provisional, so re-flowing changes the measurement being taken), and a run whose head belongs to a fragmentainer already filled.

The driver is bounded two ways, and both end at the same recovery rather than spinning or dropping content: a check that a run which cannot advance lays the remainder out monolithically instead of re-entering, and a hard cap on the pass count for the rarer case of a run that never repeats a fragmentainer/record pair yet also never finishes — an overflowing fragmentainer beats dropped content either way, which is what §4.3's own last-resort relaxation amounts to. A pass is a function of the fragmentainer it fills and the record it resumes from, so "cannot advance" is stated as arriving at a pair the run has already been entered with: a pass reproducing the record it was handed is the one-pass case of that, and a cycle two or more passes long — which comparing only against the previous pass cannot see — is the rest of it. Running out of the pass-count cap without ever reproducing a pair is treated the same way rather than left to fall out of the loop silently.

Three further paged-media behaviors keep the break points spec-conformant rather than naive:

- **Blank-page skipping** ([CSS Paged Media Level 3 §3.2](https://www.w3.org/TR/css-page-3/#renderer-defaults), "user agents should avoid generating a large number of content-empty pages"). Page count is not a bare `ceil(height / pageHeight)`: a page-slot that no printable fragment landed in never becomes a fragmentainer, so no `PdfPage` is created for it — see [Fragmentation](#fragmentation), which is where this now falls out of. One subtlety is load-bearing: an inline or text box's real per-line position lives in its `Rectangles` map (one rect per line it spans), not in its `Location`/`ActualBottom` (which stay at a bogus line-local value for such boxes), so the fragment builder reads `Rectangles` when present — reading `Location` instead once collapsed every page of a multi-page document into one range. This is the section [CSS Paged Media](html-css-support.md#css-paged-media)'s "(see pagination)" note points to.
- **Margin truncation at unforced breaks** ([CSS Fragmentation Level 3 §5.2](https://www.w3.org/TR/css-break-3/#break-margins), "margins adjoining a break are truncated to zero"). A collapsed vertical margin large enough to push a box across one or more page boundaries *by itself* is discarded and the box starts flush at the top of the very next page — rather than the margin paginating through as literal blank vertical space. This is taken as a real break *before* the box, so it produces no fragment at all in the fragmentainer it leaves; the target the decision worked out travels with the token rather than being re-derived, since re-deriving it would reach the same "does not fit" conclusion and break again forever. Margins after a **forced** break (`break-before: page`, etc.) are explicitly preserved, per the same section. See [Page Breaks](html-css-support.md#page-breaks).
- **Line boxes are monolithic** ([§4.1](https://www.w3.org/TR/css-break-3/#possible-breaks)). A block's inline flow is resumable: where a line does not fit, `CssLayoutEngine.CreateLineBoxes` stops, records an ordinal into `FlowBox`'s walk, and the next pass fast-forwards past everything an earlier fragmentainer already kept. The break falls at the *start* of the line, so a line mixing an image or a larger font with body text moves whole instead of relocating only the taller words and stranding the shorter ones on the page the rest of their line had left. Content taller than a whole band has nowhere to fit and overflows rather than moving forever.
- **Keep-with-next relocation** ([CSS Fragmentation Level 3 §3.1](https://www.w3.org/TR/css-break-3/#break-between)). Whenever content is relocated to the next page — an unforced margin break, a `break-inside: avoid` box, an `orphans`/`widows` push, or a table body/first-row that would cross the boundary — the maximal run of preceding siblings chained to it by `break-after`/`break-before: avoid` goes with it (`DomUtils.GetPrecedingKeepWithNextRun`, folded into `EarlyBreak.Discover`; `CssLayoutEngineTable.PullKeepWithNextRun` for the table pre-checks), so a heading is never stranded at the bottom of the page its content just left. This is the one correction a box cannot carry out for itself — the break falls before a sibling already placed — so it is handed to the parent's child loop, which restarts at the run head. The guard is structural rather than geometric: the head's index must be at or after the index the pass began at, since everything below that belongs to a fragmentainer already filled. The UA print stylesheet's `h1–h6 { break-after: avoid }` gives this behavior by default; chains are transitive and an unsatisfiable `avoid` is relaxed per §5.3.

After layout every `CssBox` has a final bounding rectangle and each `CssLineBox` has absolute document coordinates. Layout's last phase turns that into its real output — the fragment tree.

### Per-character font matching

Cross-family fallback needs the whole authored `font-family` stack, which the ordinary cascade collapses to the first existing family. The engine retains it as `CssBox.FontFamilyList`. During word building, `CssBox.AddWord` ([Html/Core/Dom/CssBox.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Html/Core/Dom/CssBox.cs)) splits a word into per-face fragments **only when needed** — a fast-path scan (`NeedsPerCodepointFont`, using `Font.HasGlyph`) checks whether the primary font already covers every codepoint in the word, and ordinary fully-covered text stays a single word at zero added cost. When a split is required, each fragment resolves to the first family in the stack that both covers its codepoints and has a glyph for them; this split composes with the small-caps split rather than duplicating it. See [Per-character font matching](html-css-support.md#per-character-font-matching-and-coverage-fallback) for the user-facing behavior.

### Fragmentation

**Key types:** `FragmentTree` / `FragmentainerFragment` / `BoxFragment` ([Html/Core/Fragments/Fragment.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Html/Core/Fragments/Fragment.cs)), `FragmentEmitter` ([Html/Core/Fragmentation/FragmentEmitter.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Html/Core/Fragmentation/FragmentEmitter.cs))

The phases above compute geometry *on* the box tree; this phase collects it into an immutable **fragment tree**, which is what layout actually hands downstream. It implements the box-fragment model of [CSS Fragmentation Module Level 3 §2](https://www.w3.org/TR/css-break-3/).

#### The model

A **fragmentainer** is one slot content flows into — a page, or a multi-column column, which [§2](https://www.w3.org/TR/css-break-3/#fragmentainer) names in the same breath ("a column in multi-column layout, or a page in paged media"). It is not a DOM element. A **box fragment** is the portion of one box that lives in one fragmentainer: a box crossing a page boundary produces one fragment per page it appears on, and one split at a column boundary produces one fragment per column. Each fragment owns its geometry and keeps a read-only reference to its `CssBox` for style and paint-handler dispatch, so fragments stay cheap and style keeps a single home.

`FragmentainerFragment` is a *page*, because that is what gets materialized as one output page; a column's fragments are ordinary `BoxFragment`s inside the page that holds it.

| Type | What it is |
|---|---|
| `FragmentTree` | the document's fragmentainers, in page order |
| `FragmentainerFragment` | one page: its pagination slot, its resolved band geometry, and its root box fragment |
| `BoxFragment` | one box's portion of one page — its decoration rectangles, its words, its child fragments |
| `LineFragment` | one decoration rectangle (one line box's, or the whole border box for a block-level box) |
| `TextFragment` | one positioned word |

`BoxFragment` also records `IsFirstFragment`/`IsLastFragment`, which say where a box's fragments begin and end (the edges a `box-decoration-break` value applies at are finer than this — they are per decoration rectangle, on `LineFragment.Slice`); `WholeBoxRect`, the unfragmented border box that whole-box effects (the `transform` pivot, the `clip-path` reference box) resolve against; `OverflowClip`, the rectangle an `overflow: hidden` ancestor clips this fragment to; and `OverflowClipCurve`, that same ancestor's rounded-corner curve when it also has a `border-radius` — carried separately since a displaced fragment's confinement band further narrows only the rectangle, never the curve. `OverflowClip`/`OverflowClipCurve` are resolved by the emitter rather than looked up from the boxes at paint time, because a box can be shown at several places in one document — a repeated table header is one source subtree standing in for every page's proxy, and its boxes hold only the last position layout gave them. The emitter is the only thing that still knows which of those positions a given fragment came from.

#### Coordinates

Fragment rectangles are **fragmentainer-local**: `local.Y = documentY - (PageTopOf(k) - MarginTop)`, X unchanged, since a page's horizontal margin offset is applied by the painter's own page translate. A `position: fixed` fragment subtracts nothing and is emitted in every fragmentainer, which is how fixed content repeats on every page.

#### How it is emitted

**The fragmentainers are stated by the driver, not rediscovered geometrically.** `HtmlContainerInt.LayoutDocument` hands each fragmentainer pass's slots to `FragmentEmitter` as that pass ends: the slots from the one the pass began filling up to the later of the one it ended up filling and one below where the next resumes. That gap is not always empty — a monolithic subtree is laid out in a single pass and can cover bands past the one it started in, and a box placed far down the document means the next pass's slot is not this one plus one. After the *final* pass the emitter is given the highest band any geometry reaches, which is the one geometric question the model still asks: content that no break record names (a monolithic subtree, a box that simply overflows its page) extends past the slot the pass that laid it out was filling.

Within a slot the rule is the same one as before: a box emits a fragment where its own geometry lands in that slot's content band, or where any descendant emits. Band membership uses the painter's own minimum-overlap epsilon, so the tree contains exactly the rectangles the painter would draw.

**A fragment's geometry is its own, which for a nested fragmentainer means layout has to state it.** Every fragmentainer of the page grid differs from the last in the block axis only — §2 shares one inline size and position across a box's fragments — so a page fragment's rectangles can be read off the box and separated by the band. A column cannot be described that way: columns sit side by side inside one page band, so two column-fragments of one box have the same Y range *and* would have the same X, since a `CssBox` carries a single position. So the columns engine hands each column over as it finishes filling it (`FragmentEmitter.RecordNestedFragmentainer`) with the geometry its content had at that moment, plus the column's own block and inline extent; membership becomes a question about **both** axes, and the box a column continues is moved to the next column's origin rather than keeping the position of the one it left. Two consequences worth knowing: a box that continues has not had its height applied yet, so its fragment's decoration area is measured from the content it placed there; and a column's content bottom is read from the line boxes the pass kept, never from its words, because a flow that stops re-places only the words up to the break and the rest still hold the measurement pass's positions.

**A pass's output is not always final, and the emitter can un-freeze it.** The §4.3 corrections are bounded within the pass that *discovers* them, but a box only reaches its epilogue on the pass that *completes* it — for a box spanning several fragmentainers, a later pass. So `break-inside: avoid` can relocate a box out of a fragmentainer already frozen. Rather than forbid that, a relocation that reaches back into a frozen slot drops it, and it is emitted again once layout has settled. A pass never invalidates the slot it is itself filling or anything after it, so ordinary forward layout re-emits nothing.

**Everything defined over the whole box, rather than over one slot, is resolved when the tree is materialized.** A block-level box's decoration area is its own border box, and a box that continues into a later fragmentainer has not had its height applied yet on the pass that freezes this slot; §6.2's unbroken box is the sum of *every* rectangle the box produces, including ones a later pass has yet to add. Both are read at the end, from the emitter's own record of what each slot held rather than from the live boxes — a box laid out again has its rectangles reset, so a line box a frozen slot recorded need not still exist. Materialization grows a page-grid decoration to its content only for a flex/grid item whose commit pass pinned its size before its overflowing content was known; an ordinary author-declared definite height remains fixed and its content paints as overflow.

`IsFirstFragment`/`IsLastFragment` come from the break record where there is one: at the end of a pass the outgoing token names exactly the boxes continuing into the next fragmentainer, and the incoming one exactly those continuing from the previous. Monolithic subtrees appear in no record, so their span comes from the slots they were emitted in.

**The page grid needs layout to state a fragment too, in one case: a box whose continuation holds none of its content.** [css-tables-3 §6.1](https://www.w3.org/TR/css-tables-3/#fragmentation) fragments a row by fitting as much as each of its cells can take independently, so a row can continue with one of its cells already finished — and §6.1 continues that cell's box with the row's, as borders and background running the fragment's depth with nothing in them. Nothing can be read off the box for this: a continuation deliberately leaves the finished cell's single `Location` naming the fragmentainer that placed it, and a cell that finished is indistinguishable from one no pass ever entered by geometry alone. So the row loop states the rectangle (`FragmentEmitter.RecordContinuationShell`) once the row's own bottom is settled, and the emitter builds a decoration-only fragment from it wherever that rectangle lands. Two properties keep it from widening anything else: the statement is honoured only for a box that already holds a frozen fragment somewhere, so it can continue a fragment but never invent one and `_frozen` membership is unchanged; and it never counts as printable content (CSS Paged Media Level 3 §3.2 excludes backgrounds and borders by name), so it cannot turn an otherwise empty slot into a page.

Two box kinds are not reachable by walking `CssBox.Boxes` and get explicit structure here. A repeating table `<thead>`/`<tfoot>` lives in a `CssProxyBox` whose source subtree is deliberately outside the live tree; the emitter descends into it through the proxy's own captured geometry, so the repeated header becomes real fragments, one set per page. A rowspan placeholder (`CssSpacingBox`) gets the cell that spans into it as a fragment child, so that cell appears once per row it spans.

**Blank-page skipping falls out of the build.** [CSS Paged Media Level 3 §3.2](https://www.w3.org/TR/css-page-3/#renderer-defaults) asks user agents to avoid generating content-empty pages; a page-slot that no printable fragment landed in simply never becomes a fragmentainer. Whether a box counts as printable is `DomUtils.HasOwnPrintableContent` — text, generated content, an image, a visible background or border — excluding `position: fixed` content (which repeats everywhere and would make every slot look non-empty) and the box promoted to fill the page canvas (see [Stacking order](#stacking-order-css21-appendix-e-and-acid2)). This is what lets Acid2's own intentionally-huge `100em` margins, meant to be scrolled off-screen in a single-viewport browser, skip straight past the gap instead of paginating through several blank pages.

The one exception is a page left blank *on purpose*. A directional forced break (`break-before: recto` and friends, [CSS Fragmentation Level 3 §3.1](https://www.w3.org/TR/css-break-3/#break-between)) may need to step over a slot so its content lands on a left- or right-hand page; layout records that slot on the container, keyed by the box that took the break so the record can be retracted when a box's prologue re-runs, and the emitter materializes it despite having no content to put on it. Such a reservation is write-only during layout — only the emitter reads it — so it annotates the final layout rather than feeding back into it.

#### Fragmentation happens during layout

Layout fills one fragmentainer at a time: a pass targets a fragmentainer, and where content does not fit it stops, records where it stopped, and the next pass resumes from exactly that point ([§2](https://www.w3.org/TR/css-break-3/#fragmentainer), [§4.4](https://www.w3.org/TR/css-break-3/#break-between)) — see [Pagination](#pagination) above. The emitter does not decide where breaks fall; it collects the already-fragmented result into the immutable tree. Which pages exist, and where content sits on each, are facts produced by layout, not reconstructed by paint, and a fragment's rectangles are stated by whoever filled the fragmentainer — which is why a box can genuinely split across multi-column columns.

### Painting

**Key types:** `FragmentPainter` ([Html/Core/Paint/FragmentPainter.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Html/Core/Paint/FragmentPainter.cs)), `IFragmentContentPainter` ([Html/Core/Paint/Content/IFragmentContentPainter.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Html/Core/Paint/Content/IFragmentContentPainter.cs)), `StackingOrder` ([Html/Core/Paint/StackingOrder.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Html/Core/Paint/StackingOrder.cs)), `Canvas` ([PeachDrawing.Core/Canvas.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/drawing/PeachDrawing.Core/Canvas.cs)), `BordersDrawHandler` ([Html/Core/Handlers/BordersDrawHandler.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Html/Core/Handlers/BordersDrawHandler.cs)), `CssImagePainter` ([Html/Core/Handlers/CssImagePainter.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Html/Core/Handlers/CssImagePainter.cs)), `CssImage` ([Html/Core/Entities/CssImage.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Html/Core/Entities/CssImage.cs)), `BackgroundImageDrawHandler` ([Html/Core/Handlers/BackgroundImageDrawHandler.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Html/Core/Handlers/BackgroundImageDrawHandler.cs))

#### The Canvas contract

Painting draws only through the abstract `Canvas` of [PeachDrawing.Core](#peachdrawingcore) (see its table of primitives), so the same painting logic drives the PDF canvas, the SVG canvas, the raster canvas and the three screen backends. Brushes and pens are created through the canvas (`GetSolidBrush`, `GetPen`, `GetLinearGradientBrush`, `GetRadialGradientBrush`, `GetConicGradientBrush`) so each backend keeps its own representation. A page's hyperlinks reach every canvas the same way: `HtmlDocument.PaintLinks` makes `Canvas.AddLink`/`AddDestinationLink`/`AddDestination` calls once all pages are painted, and the PDF canvas writes link annotations and named destinations while the SVG canvas writes `<a href>` elements; cursor areas and semantic regions travel the same route to the screen backends.

#### The painter

**Painting is its own phase, and it consumes the fragment tree rather than the box tree.** `FragmentPainter` takes one `FragmentainerFragment` — one page — and paints its fragment subtree. Every method receives the `BoxFragment` being painted and reads all geometry from it, using the box only for computed style. Because fragment rectangles are already fragmentainer-local, there is no per-page coordinate offset to apply anywhere in the painter.

One painter instance paints one page, so per-page state (which fragments have already been drawn — a fragment can be reached twice, once nested and once hoisted for stacking order) belongs to the painter and is never recorded on the box tree or the container. Painting is fully synchronous: everything it draws was resolved during layout.

`FragmentPainter.PaintFragment` applies the CSS painters algorithm in the correct order, skipping boxes with `display: none` or `visibility: hidden`. Fixed-position boxes suspend the clip stack so they paint relative to the page rather than within any page margin clip. A fragment exists only where its box has something on that page, so the off-screen cull reduces to intersecting the fragment's own rectangles with the current clip — a fragment that carries only descendants is always entered, since a hoisted out-of-flow descendant can paint outside it.

Boxes that cannot be expressed by the generic paint — replaced elements (`<img>`, `<object>`/`<video>`, inline `<svg>`, `<iframe>`, `<math>`), form fields and `::marker` — are drawn by an `IFragmentContentPainter` chosen from the box's type (`FragmentContentPainters.For`). "Cannot be expressed" is the whole bar: an `<hr>` has no content painter, because its rule is simply its own border and the generic paint already draws borders. The five replaced kinds share one `ReplacedFragmentPainter` base, which owns the sequence they have in common (overflow clip, background, borders, then the replacement content in the content box of the phantom word that carries it) and asks the subclass only where the content comes from and how to draw it. Adding a new replaced element means adding a content painter, not paint code on `CssBox`.

**Where a transformed box was drawn is the painter's to say.** Link annotations and form widgets are written after the pages are painted, from layout rectangles, but an element a `transform` moved is drawn somewhere else. While it paints, the painter tracks the cumulative map of the transforms it has entered — an element's matrix with its `transform-origin` resolved against its own box, a `perspective` element's projective warp, a 3D rendering context's accumulated plane — in the same fragmentainer-local space every fragment rectangle is in, and records it, per box and pagination slot, for the boxes an annotation can sit over (`BoxPlacements`; a box that is not recorded is where layout put it). the host's link and form-field writers (in PeachPDF, `PaintLinks` and `FormFieldBuilder`) carry the layout rectangle through that map (`AnnotationPlacement`) instead of working the transform out again from the box tree, the way they already read what the painter culled (`HtmlContainerInt.NoteNotRendered`), so the click target and the picture cannot disagree. A PDF `/Rect` is axis-aligned, so it is the bounding box of the drawn shape; a link adds the shape as `/QuadPoints`, and a widget's appearance stream gets the map's linear part as its `/Matrix` when the map is affine.

Each box fragment is painted as follows:

1. **Background colour** — fills the box's border area with `background-color`.
2. **Background images, gradients, and list markers** — All CSS image values (whether used as a `background-image` layer or a `list-style-image` marker) are represented as a `CssImage` discriminated union and painted through the single entry point `CssImagePainter.Paint`. The host supplies the destination rectangle and position/repeat settings; `CssImagePainter` dispatches by image type:
   - **URL images** — delegated to `BackgroundImageDrawHandler.DrawBackgroundImage`, which handles all four `background-repeat` modes (`no-repeat`, `repeat-x`, `repeat-y`, `repeat`) and `background-position` placement. The decoded `Image` is exposed by `CssImage.Url` via its embedded `ImageLoadHandler`, but owned by the render's `HtmlContainerInt` (see "Image loading and decoding" below) — a `background-image` sharing a source with a plain `<img>` or an `<object>` draws the same instance. When the URL source is an SVG, `CssImage.Url` instead exposes the parsed `SvgDocument`; `CssImagePainter` gets a document-local cached `Canvas.CreateTile` Form XObject sized to the resolved `background-size` (using the SVG's own intrinsic width/height/ratio, like a raster image's), then hands that tile to `BackgroundImageDrawHandler` so it positions/repeats exactly like a decoded raster image — real vector content, never rasterized. Repainting the same SVG at the same size reuses the form.
   - **Linear gradients** — `GetLinearGradientBrush` with arbitrary colour stops and angles, including `repeating-linear-gradient`.
   - **Radial gradients** — `GetRadialGradientBrush` with elliptical shape, size keywords (`closest-side`, `farthest-corner`, etc.), and repeating variants.
   - **Conic gradients** — `GetConicGradientBrush` with per-stop angle positions.

   List marker images are loaded during word measurement (the same `EnsureLoadedAsync` call used by background layers) and painted by the `::marker` box's own content painter immediately after the child-box paint, using a font-height-sized square positioned to the left of the list item.
3. **Borders** — `BordersDrawHandler.DrawBoxBorders` draws each side independently, respecting `border-style` (solid, dashed, dotted, double, groove, ridge, inset, outset), `border-width`, and `border-color`. Rounded corners are rendered as `GraphicsPath` arcs. For inline elements that span multiple line boxes, left and right borders are only drawn on the first and last fragment respectively.
4. **Inline content** — for each `CssLineBox` the box participates in, its `CssRect` words are drawn in order. `CssRectWord` instances emit a `DrawString` call; `CssRectImage` instances emit a `DrawImage` call. Text decoration (underline, overline, line-through) is drawn as lines immediately after the text.

##### Stacking order (CSS2.1 Appendix E) and Acid2

The four steps above order one box's own layers; ordering sibling and descendant boxes against each other is the stacking-context problem, which `StackingOrder` owns. Within a single stacking context, the painter's stacking loop paints in [CSS2.1 Appendix E](https://www.w3.org/TR/CSS21/zindex.html) order: in-flow block-level descendants first, then non-positioned floats, then in-flow inline-level content (text and inline replaced content, e.g. an inline `<img>`/`<object>`), then positioned descendants. A plain block-level wrapper whose entire content is inline (a `<div>` around nothing but an inline image) is treated as part of the inline pass — `StackingOrder.ActsAsInline` — since painting it is what paints that inline content.

Floats complicate this because a float can need to paint against siblings at a different nesting level than where it's declared. `DomUtils.NeedsStackingHoist`/`StackingOrder.Flatten` hoist such a float, and `IsLocalOrderingScope` decides how far: a float hoisted past its immediate container still preserves correct **local** order against its non-hoisted siblings as long as that container is itself positioned (`position: relative`/`absolute`/`fixed`/`sticky`), even without an explicit `z-index`; a genuine stacking-context descendant (real `z-index`, `opacity < 1`, or a transform) instead escapes all the way to the nearest true stacking context, where z-order competition actually has meaning. One narrower case is a known residual: a float whose immediate container is a *plain, non-positioned* wrapper hoists to the nearest stacking-context ancestor rather than preserving local order against that wrapper's siblings.

Outlines are the one layer not drawn where their box paints. [CSS Positioned Layout 4's painting order](https://www.w3.org/TR/css-position-4/#painting-order) recommends drawing outlines last in the stacking context, so a ring that spills over a following sibling is not painted over by it. The painter collects each outline into the nearest enclosing *outline scope*. It draws the collected outlines just before the scope's first positive-`z-index` layer, or when the scope finishes if it has none. So a raised overlay still covers an outline, while `z-index: auto`/`0` positioned content does not. Scopes are the boxes that paint as one unit: the root, stacking contexts, positioned boxes, floats and atomic inlines (Appendix E), plus flex and grid items, which Flexbox and Grid Layout paint like inline blocks. A box that `PaintFragment` wraps in its own transform or `clip-path` is also a scope, so its outlines are drawn inside that graphics state. Every box that `StackingOrder.Flatten` hoists is a scope, so a deferred outline never crosses a hoisting boundary. Each outline records the `overflow` clips between itself and its scope, then replays them when it is drawn. In tagged output, a scope's outlines form one artifact, drawn after the structure element that owns them has closed. Outlines keep their relative order; deferral only changes where they fall relative to other content.

The paint-order, blank-page-skipping and unforced-break margin-truncation behavior above is exercised by the **Acid2** browser conformance test, which PeachPDF renders substantially correctly. Acid2's assumption that a `position: fixed` element can cover content on one continuous canvas remains inherently different in a paginated renderer. Its interlocking 2×2-pixel checkerboard backgrounds are geometrically phase-correct in the PDF, but can show rasterizer- and resolution-dependent stippling because each tiny transparent image tile is filtered independently. The stacking-context model is documented from the authoring side in [Stacking Context](html-css-support.md#stacking-context).

#### Rasterized regions

PDF cannot express every effect as vector content (a blur needs per-pixel convolution), so the painter has a second route: `Canvas.BeginRasterSurface(bounds)` returns a child canvas — for the PDF canvas a [`RasterCanvas`](#peachdrawing-cpu-raster) — whose coordinate system is the requester's own, so the ordinary paint code draws into it with no translation; `DrawRaster(surface)` hands the finished bitmap back, and the PDF canvas encodes it as a PNG and embeds it through the same pass-through path any PNG takes. Both members are virtual with inert defaults, so a canvas that cannot rasterize (a measure-only pass, a test double) returns null and the caller falls back to what it did before. `FragmentPainter.PaintRasterized` is the first caller: it paints an element's fragment subtree into a surface, runs the element's `filter` list over the pixels, and draws the result.

**What asks for pixels.** The seam is used by six features, each a few lines in the painter over the same machinery:

- **CSS effects that PDF has no operator for** — `filter` functions with a blur or a cross-channel colour matrix, `drop-shadow()`, blurred `box-shadow` and `text-shadow`. `FragmentPainter.PaintRasterized` paints the subtree into a tight bitmap, runs the filter list over it (`RasterFilterChain`), and draws it; shadows draw only the shadow layer as a bitmap under the vector content.
- **SVG filters with pixel primitives.** `SvgRasterFilterEvaluator` evaluates the whole primitive graph over premultiplied bitmaps (over the kernels in `Svg/SvgRasterKernels.cs` and `PeachDrawing`'s `Filters/`: blur, morphology, convolution, turbulence per the specification's reference algorithm, displacement, lighting, arithmetic compositing, component transfer, colour matrices), converting between sRGB and linearRGB per `color-interpolation-filters`. A filter of only PDF-expressible primitives keeps the vector tile evaluator.
- **`backdrop-filter`.** Paint is forward-only, so the backdrop is rebuilt on demand (`FragmentPainter.Backdrop.cs`): a second painter walks the same fragment tree into a bitmap from the element's *backdrop root* and stops the moment it reaches the element, which is by construction what was painted before it.
- **Perspective and 3D transforms.** A transform that is not affine once projected onto the element's plane (`perspective()`, or a 3D transform under a parent's `perspective`) is painted untransformed into a bitmap and warped through a 3×3 homography (`Homography`, `Warp`); the affine and plane-parallel cases keep the vector path. A `transform-style: preserve-3d` subtree is one *3D rendering context*: each member plane's transform is accumulated down from the context's root (`FragmentPainter.Preserve3D.cs`), every plane is painted into its own bitmap and `Warp.Composite` draws it into one shared bitmap through a per-pixel depth buffer (`DepthBuffer`), so planes that intersect are resolved exactly. The per-pixel work (`PixelKernels.Warp.cs`: the bilinear texel blend and the depth test) has a scalar reference and a `Vector128` implementation that agree bit for bit.
- **Transparency flattening** for PDF/A-1 and PDF/X-1a/X-3 (`TransparencyPolicy.Flatten`). What needs transparency is decided by the PDF writer's own guard: a box's own paint is tried on a scratch page in which the guard records instead of throwing (`TransparencyProbe`), so there is one list of transparent constructs. A box that needs it is replaced by an opaque bitmap of the page up to and including it, using the same repaint as the backdrop filter.
- **Bitmap colour glyphs** (`CBDT`/`CBLC`, `sbix`): each glyph is the font's picture, drawn by `ColorGlyphPainter` in PDF and by `RasterCanvas.DrawString` in bitmaps.

Text inside a rasterized region stays selectable: after the bitmap is drawn, a second painter in text-only mode draws the same text again as invisible PDF text (render mode 3), at the same positions, and the bitmap itself is marked an artifact in tagged output.

#### Image loading and decoding

Images are loaded on demand by `ImageLoadHandler`. Supported sources include file paths, HTTP URLs (via the render context's `GetResourceStream`), `data:` URIs, and MHTML-embedded resources. Decoding is handled by **PeachImage** (JPEG, PNG, BMP, GIF, WebP, AVIF, TIFF, and JPEG XL — TGA, PSD, and HDR aren't implemented there and so aren't decodable). Decoded images are cached for the lifetime of a single render so that the same image referenced multiple times in a document is only decoded once.

`PeachImageSource` (the PDF engine's image source, in `PeachDrawing.PDF`'s `Engine/Drawing/`) routes a CMYK/YCCK JPEG around the usual forced-RGBA32 decode entirely — that conversion has no color management at all, so applying it to a CMYK source would destroy its print separations. A CMYK JPEG (and an RGB/grayscale JPEG carrying a usable embedded ICC profile) is instead embedded via byte-for-byte pass-through in `PdfImage` (see [PeachDrawing.PDF](#peachdrawingpdf)) — see [Color and ICC profiles](usage-examples.md#color-and-icc-profiles) for the embedding behavior itself.

Orientation is reported, never applied, by PeachImage's decoders: `PeachImageSource` copies `ImageInfo.Orientation` onto the source it returns, and `Image.Width`/`Height` stay the stored raster's size so downscale decisions and every pass-through embed keep working on the real pixels. The CSS `image-orientation` property (default `from-image`) resolves to a rotation and flip, intrinsic sizes are read through the oriented size, and painting pushes a matrix around the draw call, which is why a passed-through JPEG is never re-encoded to rotate it.

A JPEG XL file is routed first: one made by losslessly recompressing a JPEG carries the data to rebuild that exact JPEG, so `PeachImageSource` reconstructs it (no pixel decode) and hands the bytes back through the JPEG routing above — the same CMYK/ICC pass-through rules apply, with no second implementation. Any other JPEG XL file decodes to RGBA like the other raster formats; a CMYK one is color-managed to sRGB first, and an animation renders its first frame.

`ImageLoadHandler` is an implementation detail of `CssImage.Url`: each URL image owns its handler and exposes `EnsureLoadedAsync(HtmlContainerInt)` for lazy loading and `Dispose()` for cleanup. Callers (background layer loops, list marker painting) interact only with `CssImage` and never touch `ImageLoadHandler` directly.

A successfully resolved image or SVG document is cached on the render's `HtmlContainerInt`, keyed by the resolved absolute source URI, so a source referenced by more than one `<img>`/`<object>`/`background-image`/`list-style-image`/`content: url()` is fetched and decoded only once - this is the cache the paragraph above refers to. The `Image`/`SvgDocument` outlives any single `ImageLoadHandler`; disposing a handler only releases its own fetch stream, never the resolved resource, which the container disposes once at the end of the render.

### Tagged PDF (PDF/UA) structure tree

**Key types:** `IStructureTagSink`/`StructureNode` ([Html/Core/Handlers/IStructureTagSink.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Html/Core/Handlers/IStructureTagSink.cs)), `StructureTagMapper` ([Html/Core/Handlers/StructureTagMapper.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Html/Core/Handlers/StructureTagMapper.cs)); in PeachPDF `StructureTagBuilder` ([PdfFeatures/StructureTagBuilder.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/PeachPDF/PdfFeatures/StructureTagBuilder.cs)); in the PDF writer `PdfTagRecorder` ([PdfTagRecorder.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/drawing/PeachDrawing.PDF/PdfTagRecorder.cs))

Tagged output (`PdfGenerateConfig.EnableTaggedPdf`, off by default — see [Tagged PDF (PDF/UA) Support](html-css-support.md#tagged-pdf-pdfua-support) for the user-facing CSS property and default mapping) is built live from the same fragment paint walk that produces the visible page content, rather than from a separate pass or an event-interception layer around drawing calls. The engine fully controls its own paint walk, so it is the natural place to hook in, and it stays format-neutral: it reports structure to an `IStructureTagSink` a host supplies, and a host that writes no tagged format supplies none.

- `FragmentPainter.PaintTagged` — the single choke point every fragment's paint goes through — is where tagging attaches. When the document has a sink (`HtmlDocument.StructureTagSink`), it calls `StructureTagMapper.Classify(box)` before delegating to `PaintContent`, which dispatches to the box type's own content painter. With no sink, `PaintTagged` reduces to a direct `PaintContent` call behind a single null check.
- `StructureTagMapper.Classify` resolves a box to one of four outcomes: transparent pass-through, an `/Artifact` marked-content sequence (e.g. a decorative `<hr>`), a pure grouping structure element (no MCID of its own — most real HTML elements, since their text always lives on an anonymous child box), or a content structure element (gets an MCID wrapping its own `DrawString`/`DrawImage` calls). The common-case HTML-tag → structure-type mapping is entirely CSS-driven via `-peachpdf-pdf-tag-type` (see the linked docs page); this mapper only implements the narrower fallback for anonymous boxes (no source element for any selector to match — e.g. CSS-generated table structure) and the `auto` block/inline default.
- PeachPDF's `StructureTagBuilder` implements the sink as a thin adapter that hands each element, identified by its box, to the PDF writer's `PdfTagRecorder`, which owns the actual PDF object graph (see [PeachDrawing.PDF](#peachdrawingpdf): the structure tree root and synthetic `/Document`, the box → structure element cache, per-page MCIDs, the `/ParentTree`). A `<li>` gets a dedicated path (`FragmentPainter.PaintListItem`) splitting its marker and body content into sibling `/Lbl` and `/LBody` structure elements under `/LI`, driven by the `::marker` pseudo-element's own cascaded `-peachpdf-pdf-tag-type` (see [Pseudo-elements](html-css-support.md#pseudo-elements)).
- Links are tied to the tree the same way: `HtmlDocument.PaintLinks` reports each link it places on a canvas to the sink (`LinkAdded`), and `StructureTagBuilder` looks up the originating box's `/Link` structure element and appends an `/OBJR` to its `/K`, giving the annotation its own `/StructParent`-keyed `/ParentTree` entry — both directions of the PDF/UA-preferred bidirectional link/structure linkage, drawn from the same `ParentTreeNextKey` counter the page/MCID entries already advanced past (the builder's `Finish()` runs before links are written).

### SVG rendering

**Key types:** `SvgTreeBuilder` ([Svg/SvgTreeBuilder.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Svg/SvgTreeBuilder.cs)), `SvgRenderer` ([Svg/SvgRenderer.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Svg/SvgRenderer.cs)), `SvgDocument` ([Svg/SvgDocument.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Svg/SvgDocument.cs)), `ISvgSourceNode` ([Svg/ISvgSourceNode.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Svg/ISvgSourceNode.cs)), `CssBoxSvg` ([Html/Core/Dom/CssBoxSvg.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Html/Core/Dom/CssBoxSvg.cs))

PeachPDF renders SVG — both inline `<svg>` elements in HTML and standalone SVG (`<img src="x.svg">` / `data:image/svg+xml`) — as real vector PDF content: shapes become native PDF path-construction operators, gradients become native PDF shadings, and clips/masks/patterns become native PDF constructs. SVG is never rasterized to a bitmap. This is a cross-cutting subsystem, not a pipeline phase of its own — it plugs into DOM construction, layout and painting through two specialised `CssBox` subtypes. For the full element/property compatibility matrix, see [Supported SVG Features](supported-svg-features.md).

#### Two entry points, one pipeline

SVG content reaches the renderer through one of two `CssBox` subtypes, both converging on the same `SvgTreeBuilder`/`SvgRenderer` pipeline:

| Entry point | Source | How the source is read |
|---|---|---|
| `CssBoxSvg` | An inline `<svg>` element in the HTML document | Its already-parsed descendant `CssBox` tree (built for free by the ordinary HTML parser — see [HTML parsing](#html-parsing)) is read as a plain tag/attribute data source, never laid out or painted through the generic box pipeline |
| `CssBoxImage` | `<img src="x.svg">` or `<img src="data:image/svg+xml,...">` | `ImageLoadHandler` sniffs the `.svg` extension or `data:image/svg+xml`/`Content-Type: image/svg+xml` and, instead of decoding a raster bitmap, parses the fetched bytes as standalone XML (`XDocument.Load`) |

Both paths build an `SvgDocument` scene graph once. Inline `<svg>` caches it for the box's lifetime (`CssBoxSvg.EnsureDocument`), since the graph can depend on host-document context such as `currentColor`. A standalone source instead resolves through `HtmlContainerInt`'s render-scoped resolved-resource cache (keyed by the source's fully resolved absolute URI, base URI included — see [Image loading and decoding](#image-loading-and-decoding) above): every `<img>`, `background-image`, `border-image`, list marker, or `<object>` reference that resolves to the same absolute source, anywhere in the document, shares one `SvgDocument` instance rather than each `ImageLoadHandler` re-fetching and re-parsing it. On its first paint, the scene graph is drawn into a PDF Form XObject; later paints of the same instance at the same viewport size invoke that form, including from a different element and on other pages. The form cache belongs to the PDF document, so it never carries an XObject into a different output file. Position is applied when the form is invoked, while size and `PixelsPerPoint` are part of the cache key because they affect the rendered artwork. Separate `SvgDocument` instances do not share a form: inline `currentColor` and other resolved styles are already baked into each graph, and a standalone source only becomes one instance when its resolved source URI is identical. A replaced SVG smaller than the backend's one-point form minimum is painted directly.

#### Source abstraction — `ISvgSourceNode`

`SvgTreeBuilder` never touches `CssBox` or `XElement` directly. Both entry points instead wrap their underlying tree behind a minimal, source-agnostic interface — `Name`, `GetAttribute(name)`, `Children`, `GetTextContent()` — so the exact same tree-building code produces an identical `SvgDocument` regardless of which source it came from:

- `CssBoxSvgSourceNode` ([Svg/CssBoxSvgSourceNode.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Svg/CssBoxSvgSourceNode.cs)) wraps a live `CssBox` tree (inline `<svg>`).
- `XElementSvgSourceNode` ([Svg/XElementSvgSourceNode.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Svg/XElementSvgSourceNode.cs)) wraps a `System.Xml.Linq.XElement` (standalone SVG, parsed as real XML rather than through the HTML tokenizer).

`GetTextContent()` returns only a node's own direct text-node children, not descendant elements' text — this matters for `<text>Hello <tspan>World</tspan></text>`, where "Hello" (the `<text>`'s own run) must stay separate from "World" (the `<tspan>`'s own run) for both source kinds identically.

#### Build phase — `SvgTreeBuilder`

`SvgTreeBuilder.Build(ISvgSourceNode root, RenderContext adapter, PaintColor? contextColor)` runs synchronously in two passes:

1. **`CollectDefinitions`** — a single walk of the whole tree that registers every id-bearing node (`Dictionary<string, ISvgSourceNode>`) and fully resolves self-contained definitions (gradients, markers, patterns, masks, `<style>` text) up front. This exists because SVG allows forward references — a `<use>` or `fill="url(#id)"` can reference an id defined later in document order.
2. **Recursive tree build** — `BuildElement`/`BuildGroup`/`BuildPath`/etc. walk the tree again, this time constructing the immutable `SvgElement` scene graph, resolving `url(#id)` references against the now-complete registry from pass 1.

Presentation properties (fill, stroke, opacity, font, etc.) are threaded down the recursion as small immutable record structs — `InheritedPaint` for paint/stroke properties, `FontContext` for `<text>`'s font-family/size/weight/style — so a property left unspecified on a child correctly resolves to its nearest ancestor's value, matching CSS-style inheritance without needing `CssBox`'s own cascade machinery.

The result is an `SvgDocument`: a `ViewBox`/`Width`/`Height`/`PreserveAspectRatio` plus a `List<SvgElement>` scene graph (`SvgPathElement`, `SvgCircleElement`, `SvgRectElement`, `SvgGroupElement`, `SvgUseElement`, `SvgImageElement`, `SvgTextElement`, and others — see [Svg/SvgElement.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Svg/SvgElement.cs)) plus dictionaries of gradient/clip-path/marker/pattern/mask definitions keyed by id.

#### Paint phase — `SvgRenderer`

`SvgRenderer.RenderInto(Canvas g, SvgDocument document, Rect viewportRect)` is the single paint entry point shared by the inline-SVG and `<img src="x.svg">` content painters. It clips to the target rectangle, computes the viewBox→viewport transform (`ComputeViewportTransform`, supporting all 9 `preserveAspectRatio` alignment keywords plus `meet`/`slice`/`none`), pushes that transform, and recursively paints every scene-graph element.

Critically, `SvgRenderer` issues nothing but ordinary `Canvas` calls (`GetGraphicsPath`, `DrawPath`, `GetSolidBrush`, `GetLinearGradientBrush`, `PushClip`, `PushTransform`, `DrawString`, …) — the same abstraction the HTML/CSS [painting](#painting) uses. There is no SVG-specific graphics API; an SVG `<path>` becomes a `GraphicsPath` built from bezier/arc/line segments exactly the way a CSS `border-radius` corner does, and an SVG gradient becomes a `Brush` from the same `GetLinearGradientBrush`/`GetRadialGradientBrush` calls background-image gradients use. This is what keeps SVG output genuinely vector: every drawing call flows through the same `Canvas` as the rest of the document, whichever backend that is.

PDF's native shading types have no tiling/repeat concept of their own, so `spreadMethod="repeat"`/`"reflect"` (`SvgRenderer.ExpandLinearSpread`/`ExpandRadialSpread`) pre-tile the gradient's own stop list — projecting the filled shape's bounding box onto the gradient axis (or radius) to find how many cycles are needed to cover it, then replicating the stops per cycle, mirroring alternate cycles for `reflect` — before the brush is ever built. This mirrors, but doesn't share code with, how CSS's `repeating-linear-gradient()`/`repeating-radial-gradient()` (`CssImagePainter.ExpandRepeatingStops`) solve the identical underlying problem: CSS's gradient axis is already sized to the background box before tiling starts, while SVG's `x1`/`y1`/`x2`/`y2` (or `r`) define only one author-chosen cycle, and SVG additionally needs `reflect`, which CSS repeating-gradients don't have.

The viewport-transform helper is reused, not reimplemented, for every SVG construct that establishes its own coordinate system: a nested `<svg>`, a `<symbol>` reached through `<use>`, a `<marker>` instance, and a `<pattern>` tile all call the same `RenderViewport` helper `RenderInto` itself is built on.

#### PDF primitive reuse for pattern/mask

`<pattern>` and `<mask>` needed genuinely new PDF-writing capability, supplied by extending the `Canvas`/`RenderContext` abstraction rather than adding SVG-specific PDF code:

- `Canvas.CreateTile(width, height)` creates a reusable vector sub-canvas — in the PDF backend a real Form XObject — and returns a fresh `Canvas` that paints into it. A pattern's content, a mask's content, and (for a masked element) the element's own content are each rendered once into a tile this way.
- **Pattern fill** clips to the filled shape's geometry, then draws that one tile `Image` repeatedly (`DrawImage`) across the shape's bounding box. Every repeat references the same underlying vector Form XObject content — never a rasterized bitmap — but this is not a native PDF `/PatternType 1` tiling pattern object; it's a simpler, lower-risk approximation that stays fully vector.
- **Mask** (`SvgRenderer.RenderMaskedElementContent`) renders the masked element's own content into one tile and the `<mask>`'s content into a second, identically-sized tile, then composites them via `Canvas.DrawImageMasked(content, mask, destRect)` — a single atomic placement (`PdfGraphicsRenderer.DrawImageMasked`, [PDF/Rendering/PdfGraphicsRenderer.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/drawing/PeachDrawing.PDF/Engine/Rendering/PdfGraphicsRenderer.cs)) that emits the PDF `/SMask` `gs` operator inside the *same* `q ... cm ... Do ... Q` block used to place the content, reusing `DrawImage`'s already-correct destRect placement math rather than relying on whatever CTM happens to be ambient at some earlier point in the content stream. This atomicity is load-bearing, not stylistic: a `CreateTile` form's content is Y-flipped relative to *its own* small height, not the page's, so a mask activated via a bare, separately-timed `gs` (an earlier implementation) silently lands in the wrong part of the page the moment the page has any margin, scroll offset, or layout position — evaluating as fully transparent everywhere. Both the mask's `/G` form and the masked content's own form must additionally carry their own `/Group << /S /Transparency ... >>` transparency-group dictionary for the soft mask to take effect in spec-conformant readers (Chrome/Edge/Acrobat, all PDFium- or Acrobat-derived) — MuPDF is unusually lenient and applies an `/SMask` even to non-group content, which made this gap easy to miss under a single-renderer check. See [Supported SVG Features](supported-svg-features.md) and the `PdfSoftMask`/`PdfExtGState` PDF object types for the rest of the construct.

#### Link annotations

An `<a>` element becomes a real PDF link annotation, reusing the same annotation-registration pipeline plain HTML `<a>` elements already use rather than a parallel SVG-specific one. Because painting runs once per *output page* during pagination, link discovery is a deliberately separate, paint-independent tree walk — `SvgRenderer.CollectLinks` composes transforms and bounding boxes only, issuing no `Canvas` calls — so a link is registered exactly once regardless of how many pages the containing box is painted on. `DomUtils.GetAllSvgLinks` finds every `CssBoxSvg`/`CssBoxImage` in the box tree and calls `CollectLinks` on each; `HtmlContainerInt.GetLinks()` merges the results into the same list ordinary HTML `<a>` links populate.

#### Coverage

See [Supported SVG Features](supported-svg-features.md) for the complete element/attribute compatibility matrix, including the reasoning behind each deliberately-excluded SVG feature (SMIL animation, scripting, `filter`, legacy SVG fonts, `textPath`, and others).

### MathML rendering

**Key types:** `MathTreeBuilder` ([MathML/MathTreeBuilder.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/MathML/MathTreeBuilder.cs)), `MathLayoutEngine` ([MathML/MathLayoutEngine.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/MathML/MathLayoutEngine.cs)), `MathRenderer` ([MathML/MathRenderer.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/MathML/MathRenderer.cs)), `MathNode`/`MathBox` ([MathML/MathNode.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/MathML/MathNode.cs), [MathML/MathBox.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/MathML/MathBox.cs)), `IMathSourceNode` ([MathML/IMathSourceNode.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/MathML/IMathSourceNode.cs)), `CssBoxMath` ([Html/Core/Dom/CssBoxMath.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Html/Core/Dom/CssBoxMath.cs))

PeachPDF renders inline `<math>` elements as real vector PDF content — glyphs through the ordinary text
pipeline, fraction bars/radical rules as native path/fill operators, stretchy operators (parentheses,
radical signs, ...) built from the resolved font's own OpenType `MATH` table glyph data. Like SVG, this
is a cross-cutting subsystem built on the same foreign-content pattern SVG established, not a pipeline
phase of its own; unlike SVG, it needs only one entry point (inline `<math>` — there is no
`<img src="x.mml">`-equivalent standalone-MathML-by-reference case) and no CSS-property-registry
involvement (MathML's own attributes — `mathvariant`, `displaystyle`, `scriptlevel`, `stretchy`, ... —
are plain XML attributes, not CSS properties; genuine CSS properties that do apply to math content —
`color`, `font-family`, `font-size` — reach it for free through the ordinary HTML cascade, the same way
they reach SVG). For the full element/attribute compatibility matrix, see
[Supported MathML Features](supported-mathml-features.md).

#### Foreign-content boundary

`CssBoxMath` mirrors `CssBoxSvg` exactly: `HtmlConstants.Math`/`CssBox.CreateBox`'s tag-name switch
constructs one for every `<math>` element, its descendant `CssBox` tree (built for free by the ordinary
HTML parser) is read once as a plain tag/attribute/already-cascaded-style data source, and
`DomParser`'s structural-normalization passes (`Correct*`) are guarded to never descend into one — the
same `if (box is CssBoxSvg or CssBoxMath) return;` checks, extended from the single-purpose SVG guards a
extended from the single-purpose SVG guards that already existed. `CascadeApplyStyles` is deliberately *not*
guarded, so `color`/`font-family`/`font-size` cascade into `<math>` descendants normally.

#### Source abstraction — `IMathSourceNode`

Mirrors `ISvgSourceNode`'s shape (`Name`, `GetAttribute(name)`, `Children`, `GetTextContent()`), plus
`Color`/`FontSizePt`/`GetFontAtSize(sizePt)` accessors that read a node's already-cascaded CSS state
directly (SVG needs no such accessors on its own source-node interface, since it re-derives paint
properties itself via `SvgPropertyRegistry`). Only one implementation exists today —
`CssBoxMathSourceNode`, wrapping the live `CssBox` subtree — but the interface seam is kept for symmetry
with the SVG precedent and so unit tests can build a tree from a lightweight fake.

#### Build phase — `MathTreeBuilder`

`MathTreeBuilder.Build(IMathSourceNode mathRoot, RenderContext adapter)` is a single recursive pass — unlike
`SvgTreeBuilder`, no id-collection pre-pass is needed, since MathML has no forward-reference id system
like SVG's `url(#id)`. It produces a `MathDocument`: a `MathNode` presentation tree (`MathRowNode`,
`MathTokenNode`, `MathFractionNode`, `MathRadicalNode`, `MathScriptNode`, `MathUnderOverNode`,
`MathMultiscriptsNode`, `MathTableNode`/`MathTableRowNode`/`MathTableCellNode`, and others — see
[MathML/MathNode.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/MathML/MathNode.cs))
plus a `ResolveFont(sizePt)` delegate the layout engine uses for every distinct scriptlevel-scaled size
a formula needs. `displaystyle`/`scriptlevel` are resolved to concrete values at build time, following
MathML Core §3.3.4's per-schema inheritance rules (e.g. a fraction's numerator/denominator are always
laid out one scriptlevel deeper and never in displaystyle, regardless of the fraction's own). MathML
3-only elements MathML Core dropped get best-effort handling here: `mfenced` desugars into a plain row
of synthesized fence/separator `mo` tokens, `maction` builds only its selected (or first) child, and
`<semantics>` builds only its first (presentation-markup) child.

#### Layout phase — `MathLayoutEngine`

`MathLayoutEngine.Layout(MathDocument document, Canvas g)` implements
[MathML Core](https://w3c.github.io/mathml-core/)'s layout algorithm — the W3C/browser-vendor
specification that gives MathML 3's presentation markup a concrete box model, since MathML 3 itself
leaves layout implementation-defined. Every structural measurement (fraction rule thickness, radical
gaps, script shift/scale, stack spacing) comes from the resolved math font's OpenType `MATH` table
constants (`MathTable.cs`'s `MathConstantsTable`), scaled by `sizePt / unitsPerEm * g.PixelsPerPoint` —
the same formula `PdfCanvasFont.ScaleDesignUnits` already uses for vertical metrics, so `MathBox` geometry
lands in the same working unit space as everything else `Canvas` measures/draws in. A font with no
`MATH` table (`MathMetrics`'s fallback path) uses fixed, TeX-book-derived approximate ratios instead of
refusing to lay the formula out — MathML Core's own documented strategy for this case. Inter-element
spacing/stretchiness/large-operator defaults for an `mo` with no explicit attribute come from
`MathOperatorDictionary` — MathML Core Appendix B's own category-based operator dictionary, transcribed
from the spec's compact (Content, Form) → category classification rather than a flat approximation.

The result is a `MathBox` tree: computed inline size/ascent/descent per node, with positioned children
(`MathPositionedBox`) and, for a leaf, the paint data needed to draw it (`MathPaintKind.Text` for
ordinary token glyphs via `DrawString`, `.Rule` for a fraction bar/radical vinculum via a filled
rectangle, `.Glyphs` for a stretchy operator's glyph(s) via the raw-glyph-index `Canvas.DrawGlyphs`
primitive below — either a single `MathVariants` pre-sized variant, or, when none is tall/wide enough, a
full MathML Core §5.3.2 `GlyphAssembly` construction: `MathGlyphAssemblyShaper` repeats the construction's
extender part(s) and distributes connector overlap to reach the target size, the same algorithm real
browsers use). Like SVG's `SvgDocument` scene graph, `MathBox` is an entirely separate model, opaque to
`FragmentTree`/`BoxFragment` — `CssBoxMath` is one ordinary, monolithic (never-fragmented) box in the
fragment tree (see §6), but everything inside it is painted as a single opaque blob from the fragment
tree's point of view. Unlike an actual replaced element (`<img>`/`<svg>`), an explicit CSS `width`/
`height` resizes only `CssBoxMath`'s own outer box (`MeasureWordsSize`) — MathML Core doesn't
characterize `<math>` as replaced, so its content is never rescaled to fit, and can overflow a
smaller-than-natural box or leave extra space in a larger one.

#### Paint phase — `MathRenderer`

`MathRenderer.RenderInto(Canvas g, MathBox root, Rect destination)` walks the positioned `MathBox`
tree issuing ordinary `Canvas` calls — the same abstraction the HTML/CSS painting and SVG's
`SvgRenderer` both use. A stretchy operator's assembled/variant glyphs are addressed directly by font
glyph index via `Canvas.DrawGlyphs` (`GlyphPlacement`), a new primitive alongside the existing
character-based `DrawString`: unlike ordinary text, a `MATH`-table size-variant glyph frequently has no
Unicode codepoint of its own to shape through cmap, so it has to be drawn by raw glyph id instead.
`PdfGraphicsRenderer.DrawGlyphsAtPositions` implements it by generalizing the per-glyph `Td`+`Tj` emission `DrawString` already uses internally, and — since
nothing else registers these glyphs for font subsetting the way `DrawString`'s own shaping path does —
explicitly calls `PdfFont.AddShapedGlyph` for each one before drawing, or the embedded font's subset
would omit them entirely.

#### PDF 2.0: tagged `Formula` + Associated Files

A `<math>` element defaults to the `Formula` tagged-PDF structure type (`-peachpdf-pdf-tag-type`, see
[Tagged PDF (PDF/UA) structure tree](#tagged-pdf-pdfua-structure-tree)). When tagging is enabled,
`StructureTagBuilder.AttachMathMlSource` re-serializes the element's original MathML markup
(`MathMlSerializer`, reading the `IMathSourceNode` tree directly — the author's own markup, not this
engine's internal, already-desugared `MathNode` interpretation of it) and attaches it as a PDF 2.0
([ISO 32000-2](https://www.iso.org/standard/75839.html) §14.13) Associated File
(`/AF`, `/AFRelationship /Supplement`) on the structure element, using the PDF writer's `PdfFileSpecification`/
`PdfEmbeddedFile` objects.
The same two objects back the general [`PdfGenerateConfig.Attachments`](#embedded-files-attachments) API.
See [MathML Associated Files](html-css-support.md#mathml-associated-files) for the user-facing mechanism
and [PDF 2.0 output](usage-examples.md#pdf-20-output) for `PdfGenerateConfig.PdfVersion`.

#### Coverage

See [Supported MathML Features](supported-mathml-features.md) for the complete element/attribute
compatibility matrix, including the reasoning behind each deliberately-scoped-out MathML capability
(elementary math, Content MathML, and others — all confirmed absent from MathML Core itself, not just
unimplemented here).

### Named pages and margin boxes

**Key types:** `PageRule`/`MarginStyleRule` ([CSS/Rules/PageRule.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/CSS/Rules/PageRule.cs), [CSS/Rules/MarginStyleRule.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/CSS/Rules/MarginStyleRule.cs)), `CssNamedStringEngine` ([Html/Core/Dom/CssNamedStringEngine.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Html/Core/Dom/CssNamedStringEngine.cs)), `MarginBoxRenderer` ([Html/Core/Dom/MarginBoxRenderer.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Html/Core/Dom/MarginBoxRenderer.cs))

CSS Paged Media's named `@page` rules and margin boxes (`@top-center`, `@bottom-right`, …) — the mechanism behind running headers/footers — are a cross-cutting subsystem spanning CSS parsing, layout and page painting, similar in shape to SVG above.

#### Parsing

`@page name:pseudo { … }` selectors are parsed into a `PageSelector` holding one or more `PageSelectorEntry(Name, Pseudo)` pairs (e.g. `@page dictionary:first` → `Name="dictionary"`, `Pseudo="first"`). Each `@page` rule becomes a `PageRule` exposing `Selector`, `Style` (page-level declarations like `margin`/`size`), and `Margins` — the nested `@top-center` etc. blocks, each a `MarginStyleRule` (selector name + its own declaration block).

#### Assigning content to a named page

The `page` CSS longhand (`PropertyNames.PageName`, surfaced as `CssBox.PageName`) assigns a box to a named page type. Once a box's `Location.Y` is finalized during layout, `CssBox.PerformLayoutImp`/`CssLayoutEngine` register it — `HtmlContainerInt.RegisterNamedPageElement(name, y)` appends a `NamedPageElement(Name, Y)` to `HtmlContainerInt`'s tracked list. This registration has to happen strictly after `Location` is final (and, if a later pass like multi-column re-banding moves the box, `OffsetTop`/`OffsetLeft` must keep the recorded `Y` in sync — see the note at the end of [Multi-column layout](#multi-column-layout) above) — registering against a stale Y silently points running headers at content on the wrong page.

#### Resolving which `@page` rule applies, per output page

`PageRuleResolver.GetOrderedApplicableRules` computes, for each output page, the "active named page" — the most recent `NamedPageElement` whose Y precedes that page's end, since a `page` assignment propagates forward through the flow rather than applying to a single page only (`HtmlContainerInt.PageBoundaryEpsilon`, 0.5, absorbs floating-point boundary noise). It then scores every `@page` selector entry (name+pseudo highest, name-alone or pseudo-alone lower, `:first` always wins when it matches) into an ascending-precedence list. Three callers consume that shared list: `SelectPageRule` picks the single winning rule for page-level properties (`margin`/`size`, via `ResolvePageMargins`); `SelectApplicableMarginRules` *merges* margin-box declarations by box name across every matching rule (a low-specificity base rule's `@top-left` and a higher-specificity named rule's `@bottom-right` both need to render on the same page); `SelectApplicablePageStyle` merges page-level declarations as a font-property inheritance fallback for margin boxes that don't set their own font.

#### Rendering margin boxes

`MarginBoxRenderer.Render` runs once per output page (called by `PagePainter` as it paints the page), given that page's merged margin rules and page style. For each `MarginStyleRule` it resolves `content` (string literals, `counter(page|pages)`, `string(name[, keyword])`), computes box geometry via `GetMarginBoxRect`/`ComputeThreeBoxSizes` (explicit width/min/max honored first, remaining space split evenly among `auto` boxes within each three-box row/column), and resolves font/color/alignment — falling back to the page style for font properties per the CSS Paged Media inheritance model.

#### Running headers/footers — `string-set` and `string()`

`CssNamedStringEngine.ApplyStringSet` runs during layout for any box with a `string-set` declaration, parsing the `name content-list, name2 content-list2, …` grammar (`counter()`, `counters()`, `attr()`, `content()`, `string()` all supported inside the content list) and producing `NamedString(Name, Value, Y)` records — stored both on the box itself and centrally via `HtmlContainerInt.RegisterNamedString`. `MarginBoxRenderer.ResolveNamedString` later implements the GCPM `first`/`start`/`last`/`first-except` selection keywords by filtering that document-ordered list down to the current page's `[pageY, pageY + pageHeight)` window (again widened by `PageBoundaryEpsilon`) — this is what makes a `@top-center { content: string(chaptertitle) }` margin box show the *correct* running header for whatever content actually landed on that specific page, not just the first or last `string-set` in the whole document.

#### Running elements — `position: running()` and `element()`

**Key types:** `RunningElement`/`RunningSelectionEngine` ([Html/Core/Entities/RunningElement.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Html/Core/Entities/RunningElement.cs), [Html/Core/Dom/RunningSelectionEngine.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Html/Core/Dom/RunningSelectionEngine.cs)), `RunningElementLayout` ([Html/Core/Dom/RunningElementLayout.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Html/Core/Dom/RunningElementLayout.cs)), `MarginBoxContentFragmentBuilder` ([Html/Core/Fragmentation/MarginBoxContentFragmentBuilder.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Html/Core/Fragmentation/MarginBoxContentFragmentBuilder.cs)), `MarginBoxFragment`/`FragmentainerFragment.MarginBoxes` ([Html/Core/Fragments/Fragment.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/engine/PeachBrackets/Html/Core/Fragments/Fragment.cs))

css-gcpm-3's `running()`/`element()` go further than `string-set`/`string()`: instead of capturing plain text, a `position: running(name)` element is removed from flow entirely and `content: element(name)` shows it *complete with formatting and descendant elements* — genuine layout, not a captured string. That requires more than a document-ordered value list:

- **Detach from flow.** `CssBox.IsRunningPositioned` is checked everywhere flow membership already is (`CssBox.LayoutBlockChildren`'s per-child loop, and the equivalent item-collection filters in `CssLayoutEngineFlex`/`CssLayoutEngineGrid`/`CssLayoutEngineColumns` — a table row or cell is a known gap: `position: running()` there is not excluded from the table's own layout, see [HTML & CSS support](html-css-support.md)). A running child is never placed or laid out during the main pass; instead the child-walk that would have placed it registers it — `CssBox.RegisterAsRunningElement` withdraws any stale registration (mirroring `NamedPageElement`'s own discipline) and adds a `RunningElement(Name, Box, Y)` to `HtmlContainerInt`'s document-order list, where `Y` is the position the box *would* have occupied (its nearest in-flow sibling's `ActualBottom`, or the parent's content-top).
- **Select, per page.** `RunningSelectionEngine.SelectByKeyword` implements the identical `first`/`start`/`last`/`first-except` rules `MarginBoxRenderer.ResolveNamedString` already established for `string()`, factored into one generic method both call — `MarginBoxRenderer.ResolveRunningElement` is the `RunningElement`-typed instantiation.
- **Lay out for real, per page.** The same shared `CssBox` may be selected onto several pages, each with a different margin-box width — not `CssProxyBox`'s rigid-translation trick (repeating table headers), since that assumes identical constraints at every repeat. `RunningElementLayout.LayoutRunningElementFor` instead reparents the box to a throwaway synthetic containing block sized to the margin box's rect, resets its (and every descendant's) line boxes, and calls the same `CssLayoutEngineFlex.PerformLayoutBlockified` pattern (`DetachFragmentainer`/`SuppressWordPageBreaks`/`box.PerformLayout(g)`) flex-item measurement already established for "lay this out again, off to the side, within one layout generation" — genuine re-layout, not translation, so a narrower page's margin box gets a genuinely different line-wrap.
- **Capture into the fragment tree.** `HtmlContainerInt.LayoutMarginBoxes` runs once, after `FragmentEmitter.Finish()` (the final page list has to be known first, since selection is page-index-based), resolving each page's `content: element()` margin boxes and capturing each selected box's freshly laid-out subtree via `MarginBoxContentFragmentBuilder` — a small, one-shot `BoxFragment` builder, deliberately not a reuse of `FragmentEmitter`'s pagination-aware machinery (inseparably coupled to multi-pass span/slice bookkeeping meaningless for content that never fragments). The result lands on `FragmentainerFragment.MarginBoxes`, so paint stays fragment-driven for this content too — `PagePainter` paints it via the same, unmodified `FragmentPainter.PaintFragment` every other `BoxFragment` goes through, which is also why tagged-PDF structure and real formatting/descendant elements come for free. Plain string/counter/`string()`/image margin-box content is untouched, staying on `MarginBoxRenderer`'s existing pipeline — there's no formatting fidelity to gain by moving already-plain-text content through real layout.

---

## PeachBrackets views

Three packages put a `PeachBrackets.HtmlDocument` on a screen. Each is a thin shell: it owns the platform's scrolling, input and accessibility objects, lays the document out for the view's width at one CSS pixel per device-independent unit, asks the document what is under the pointer (`GetCursorAt`, `GetInteractionAt`, `HitTest`), feeds it pointer state (`SetPointerStateAsync`, which drives `:hover`/`:active`, `pointer-events` and `inert`), paints only the invalidated or visible region through its backend, and maps the document's accessibility tree onto the platform's. None of them re-implements layout or paint. Each guide lists what it wires up and its limits.

### PeachBrackets.WinUI

**Guide:** [PeachBrackets.WinUI](peachbrackets-winui.md) · **Source:** [src/engine/PeachBrackets.WinUI/](https://github.com/jhaygood86/PeachPDF/tree/main/src/engine/PeachBrackets.WinUI/) · depends on `PeachBrackets` and `PeachDrawing.Win2D` · Windows only

Code-only (no XAML). `HtmlView` hosts a `CanvasVirtualControl` in a `ScrollViewer`, paints only invalidated regions, and drives the document from pointer events. The partials split the view by concern (input, scrolling, text input, forms, pickers, cursors, images, animation, focus, UI Automation in `HtmlViewAutomationPeer`). `HtmlViewLogic`-style window-free pieces (cursor mapping, device capabilities, page hit mapping, launchable schemes) are separated so they can be unit tested; the control itself is exercised in a real window by the UI tests.

### PeachBrackets.Android

**Guide:** [PeachBrackets.Android](peachbrackets-android.md) · **Source:** [src/engine/PeachBrackets.Android/](https://github.com/jhaygood86/PeachPDF/tree/main/src/engine/PeachBrackets.Android/) · depends on `PeachBrackets`, `PeachDrawing.Skia` and `SkiaSharp.Views` · .NET for Android (API 24+)

`HtmlView : FrameLayout` hosts an `SKGLSurfaceView` (or, with `useGpu: false`, an `SKCanvasView`). The visible region is painted by `PaintPageRegionAsync`; on the GPU it is painted as 1024-pixel tiles of the `DocumentPaintLayer.Content` layer kept as textures (a `TileGrid` with an LRU), with the `ViewportAttached` layer painted over them each frame. Touch is handled directly (`GestureDetector`, `ScaleGestureDetector`, `OverScroller`) with an event per touch stage so the host can take a gesture away from the document's `touch-action`. `Logic/` holds the Android-free pieces (accessibility mapping, tile grid, scroll and zoom math, caret and IME edits, picker values), compiled into a plain-.NET test project. The UI thread must never block on an async call.

### PeachBrackets.Apple

**Guide:** [PeachBrackets.Apple](peachbrackets-apple.md) · **Source:** [src/engine/PeachBrackets.Apple/](https://github.com/jhaygood86/PeachPDF/tree/main/src/engine/PeachBrackets.Apple/) · depends on `PeachBrackets` and `PeachDrawing.CoreGraphics` · macOS and iOS

`HtmlViewEngine` is the shared part: off-main-thread load and layout, a 1×1 composer as the measuring canvas, the tile cache where **each 512-pixel tile is a `CALayer`** (painted a few milliseconds at a time, and only the tiles a change's dirty regions touch) so Core Animation composites scroll and zoom on the GPU, the viewport-attached overlay layer, and the serialized pointer-state pump. `AppKit/HtmlView.cs` (an `NSScrollView` over a flipped page view) and `UIKit/HtmlView.cs` (a `UIScrollView` with tap, long-press and hover recognizers, a pointer interaction, an edit menu and a touch gate that applies `touch-action`) are the two views, each compiled for its platform only; there is one public-surface snapshot per platform because they differ.

---

## PeachPDF (the product)

**Guide:** [Getting started](getting-started.md), [Usage examples](usage-examples.md), [Declarative API](declarative-api.md) · **Source:** [src/PeachPDF/](https://github.com/jhaygood86/PeachPDF/tree/main/src/PeachPDF/)

PeachPDF is the package most applications reference. It does not parse HTML, lay anything out or write a PDF object: it is the glue that configures `PeachBrackets` and `PeachDrawing.PDF` from one public configuration object, supplies the resource loaders, and offers the declarative page builder. Its job is to keep that mapping in one place.

| Folder / file | What it holds |
|---|---|
| `PdfGenerator.cs`, `PdfGenerateConfig.cs` | The public entry point and the one configuration object (page size and orientation, margins, PDF version, conformance, encryption, tagged PDF, metadata, fonts, loader, rasterization DPI, animation progress, attachments...) |
| `Rendering/` | `HtmlRenderContext` — the `PdfRenderContext` HTML and CSS render with: the CSS generic font families and default font, the syntax of `font-variation-settings`, SVG colour glyphs (it supplies `SvgGlyphPainter`), and resource loading through the configured `RNetworkLoader`. `EngineOptions` maps the public option types onto `PeachDrawing.PDF`'s |
| `Network/` | The resource loaders (below) and `MimeTypeResolver` |
| `PdfFeatures/` | `StructureTagBuilder` (the engine's tagging seam onto the PDF tag recorder), `FormFieldBuilder` and `AnnotationBuilder` (the engine's annotation model onto AcroForm fields and PDF annotations) |
| `Layout/` | The declarative builder: `IDocumentBuilder`, `IPageDescriptor`, containers, rows and columns, tables, lists, text spans, file layers, `DocumentMerge` |
| value types | `PdfColor`, `PdfLength`, `PdfSize`, `PageSize`, `PdfDocumentMetadata`, `PdfAttachment`, `PeachPdfCssContent`, `PeachPdfDocument`, `TransparencyPolicy`, `RuneRange`, `FacturXOptions` |

Public exception types (`HtmlRenderException`, `HtmlRenderErrorType`) are forwarded to the `PeachBrackets` assembly (`RenderErrorTypeForwards.cs`), and the types PeachPDF exposes that the engine also needs (`PdfColorScheme`, `ClipReport`, `PdfSize`, the declarative stylesheet) are mapped at this boundary onto engine-owned equivalents (`ColorSchemePreference`, the clip log, `Size`, `CssData`).

### Generating a PDF

`PdfGenerator` ([PdfGenerator.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/PeachPDF/PdfGenerator.cs)) drives the pipeline for one document:

1. It creates a `PdfComposer` over its `HtmlRenderContext` and wraps it in a `PeachPdfDocument`.
2. It builds `HtmlDocumentOptions` from `PdfGenerateConfig` (`ToDocumentOptions`), then `HtmlDocument.LoadAsync(html, renderContext, options)` parses and cascades.
3. It lays the document out against a measuring `PdfCanvas` (`PdfCanvas.CreateMeasuring`), which gives layout the real PDF text metrics. `@page` margin overrides resolved from the stylesheet take part in this step.
4. For each page it adds a `PdfComposerPage` and calls `HtmlDocument.PaintPageAsync(pageIndex, page.Canvas)`.
5. After all pages are painted it calls `HtmlDocument.PaintLinks` (the annotation model's links become `Canvas.AddLink` calls and PDF link annotations) and writes the form fields and CSS-defined annotations from `DocumentAnnotations` through `FormFieldBuilder` and `AnnotationBuilder`, carrying each layout rectangle through the painter's recorded transform map so a click target never disagrees with the picture.
6. `ConfigureDocument` applies metadata, language, conformance, embedded files and outline, and the caller saves the returned `PeachPdfDocument` to any `Stream`.

`AddPdfPages` can be called repeatedly on the same document, and the declarative builder renders one `Page(...)` at a time through the same path, so document-wide state (conformance level, embedded-file plan) is remembered on the composer's options and compared against on later calls — a different value throws instead of silently producing a mixed file. A `PdfGenerator` and everything it owns (font and brush caches, the render context) is instance-scoped and not thread-safe; use one per thread, request or batch item.

### Resource loading

**Key types:** `RNetworkLoader` ([Network/RNetworkLoader.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/PeachPDF/Network/RNetworkLoader.cs)), `RNetworkResponse` ([PeachDrawing.Core/RNetworkResponse.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/drawing/PeachDrawing.Core/RNetworkResponse.cs), a Core type)

Resource loading is where PeachPDF answers the engine's requests. It is a cross-cutting concern that spans multiple pipeline phases: the HTML document itself must be fetched before parsing begins, external stylesheets are fetched during stylesheet collection, and images are fetched during painting. All three use the same `RNetworkLoader` abstraction, which means the caller controls how every byte of external content enters the rendering pipeline.

#### The abstraction

```csharp
public abstract class RNetworkLoader
{
    public abstract Task<string> GetPrimaryContents();
    public abstract Task<RNetworkResponse?> GetResourceStream(Uri uri);
    public abstract Uri? BaseUri { get; }
}
```

| Member | Purpose |
|---|---|
| `GetPrimaryContents()` | Returns the root HTML string. Called once at the start of the pipeline when `null` is passed to `PdfGenerator.GeneratePdf`. |
| `GetResourceStream(Uri)` | Returns a `RNetworkResponse` (stream + HTTP-style headers) for any external resource URI. Called for stylesheets and images. |
| `BaseUri` | The document's base URL. Used to resolve relative URIs in `href`, `src`, and `url()` values. If `null`, relative references resolve against the current working directory as a `file:` URI (the default working-directory base is supplied by `PdfRenderContext`) — unless [local file access is disabled](usage-examples.md#disabling-local-file-access), in which case there is no base to resolve against and the reference goes unresolved. |

`RNetworkResponse` is a simple record: `(Stream? ResourceStream, Dictionary<string, string[]>? ResponseHeaders)`. The headers are inspected by `StylesheetLoadHandler` to validate the `Content-Type` is `text/css` before accepting the body as a stylesheet.

Resources are addressed with `System.Uri`, which has no length limit on .NET 10 and later, so a large `data:` URI is held as an ordinary `Uri`. A relative reference is resolved against the base with `new Uri(baseUri, relative)`, which is how the `<base href>` element and `BaseUri` are applied to relative resource references.

#### How the pipeline uses it

**HTML document** — when `GeneratePdf` is called with a `null` HTML string, `GetPrimaryContents()` is called to obtain the document. The `HttpClientNetworkLoader` fetches the URL passed to its constructor; the `FileUriNetworkLoader` reads the file passed to its constructor; the `MimeKitNetworkLoader` extracts `MimeMessage.HtmlBody` from the MHTML archive.

**Stylesheets and images** — `StylesheetLoadHandler.LoadStylesheet` and `ImageLoadHandler.SetImageFromPath` both resolve their `href`/`src` to an absolute URI against the document base (a `<base href>` element, else `BaseUri`, else the working-directory `file:` default) via the shared `CommonUtils.ResolveAgainstDocumentBase`, then call `adapter.GetResourceStream(uri)`. There is a single resolution path — local files, `data:` URIs, and remote resources all flow through `GetResourceStream` uniformly, with no separate file-system branch. Stylesheet responses are only accepted when the `Content-Type` is `text/css`; a resolved SVG image is detected from its `Content-Type: image/svg+xml` (or `.svg` extension).

**`data:` and `file:` URIs** — regardless of which `RNetworkLoader` is configured, `HtmlRenderContext.GetResourceStream` handles `data:` URIs internally via `DataUriNetworkLoader` and `file:` URIs internally via `FileUriNetworkLoader`. This ensures inline base64 resources and local files always work without the configured loader needing to implement either scheme. Setting [`AllowLocalFileAccess`](usage-examples.md#disabling-local-file-access) to `false` removes the `file:` half of that: every `file:` request then resolves to "not found", and the working-directory base above is withdrawn, so a document cannot reach the file system by either route.

#### Built-in implementations

PeachPDF ships four concrete loaders:

##### `DataUriNetworkLoader` ([Network/DataUriNetworkLoader.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/PeachPDF/Network/DataUriNetworkLoader.cs))

The default loader when no `NetworkLoader` is set in `PdfGenerateConfig`. It handles only `data:` URIs, decoding the base64 payload into a `MemoryStream`. Any other remote URI scheme returns `null`, meaning remote resources (remote stylesheets, remote images) are silently skipped — the safest default for server-side environments where network access must be controlled explicitly. Local `file:` resources still resolve, because `PdfRenderContext` routes them through `FileUriNetworkLoader` and supplies a working-directory base URI.

##### `FileUriNetworkLoader` ([Network/FileUriNetworkLoader.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/PeachPDF/Network/FileUriNetworkLoader.cs))

Serves the root document and referenced resources from the local file system via `file:` URIs. Constructed with no arguments, its `BaseUri` is the current working directory; constructed with a file path, its `BaseUri` is that file's own `file:` URI and `GetPrimaryContents()` reads the file — the way a browser bases a locally-opened document. `GetResourceStream` reads the file and synthesizes a `Content-Type` header from `MimeTypeResolver`.

`MimeTypeResolver` uses the **operating system's own MIME mechanism by default** — the Windows shell file-association API on Windows, the Uniform Type Identifiers API on macOS/iOS, and the `/etc/mime.types` database on Linux — validating the result is a real `type/subtype` value (so a shell "no content type registered" property descriptor is rejected rather than used). When the OS provides nothing usable, it falls back to a **built-in map** covering HTML, CSS, SVG, common raster image extensions (PNG, JPEG, BMP, GIF, TGA, PSD, HDR — this map only resolves an extension to a MIME type string; it is unrelated to which of those formats PeachPDF can actually decode, see below), and the TTF/OTF/WOFF/WOFF2 font formats, and finally `application/octet-stream`. Results are memoized per extension. If a local file uses an extension outside the built-in set, register its MIME type with the OS (e.g. the shell `Content Type` association on Windows, or an `/etc/mime.types` / `~/.local/share/mime` entry on Linux) so the OS lookup can resolve it. (TGA/PSD/HDR resolve a MIME type either way but aren't decodable — see below.)

Consistent with browsers, PeachPDF gates only *stylesheets* on MIME type — a linked stylesheet body is accepted only with a `text/css` `Content-Type` — while images and fonts are identified by their bytes (raster decoding via PeachImage; `FontFormatConverter` sniffing), not their declared type.

##### `MimeKitNetworkLoader` ([Network/MimeKitNetworkLoader.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/PeachPDF/Network/MimeKitNetworkLoader.cs))

Wraps a MimeKit-parsed MHTML archive. `GetPrimaryContents()` returns `MimeMessage.HtmlBody`. `GetResourceStream(uri)` walks the MIME body parts and finds the part whose `Content-Location` matches the requested URI. This allows a fully self-contained `.mhtml` file to be rendered without any network access: all resources are read from the archive. `BaseUri` is `null` because the base URL is embedded in the document itself.

##### `HttpClientNetworkLoader` ([Network/HttpClientNetworkLoader.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/PeachPDF/Network/HttpClientNetworkLoader.cs))

Fetches resources over HTTP using a caller-supplied `HttpClient`. The `primaryContentsUri` passed to the constructor sets `BaseUri` and is used by `GetPrimaryContents()` to download the root HTML. All subsequent resource requests (stylesheets, images) resolve against that base. The caller controls the `HttpClient` lifetime, so custom headers, authentication, proxies, timeouts, and `HttpMessageHandler` chains are all supported without any PeachPDF-specific API.

#### Implementing a custom loader

To integrate PeachPDF into an environment with custom resource-resolution logic — for example, reading assets from a cloud blob store, a bundler manifest, or an in-memory dictionary — subclass `RNetworkLoader` and set the instance on `PdfGenerateConfig.NetworkLoader`:

```csharp
public class MyLoader : RNetworkLoader
{
    public override Uri? BaseUri => null;

    public override Task<string> GetPrimaryContents() =>
        Task.FromResult("<html>…</html>");

    public override Task<RNetworkResponse?> GetResourceStream(Uri uri)
    {
        var bytes = MyAssetStore.Load(uri.OriginalString);
        if (bytes is null) return Task.FromResult<RNetworkResponse?>(null);
        return Task.FromResult<RNetworkResponse?>(
            new RNetworkResponse(new MemoryStream(bytes), null));
    }
}
```

### Registering fonts at runtime

`PdfGenerator.AddFontFromStream(stream)` registers a font (TrueType, CFF, WOFF, or WOFF2) for the current generator instance. The `AddFontFromStream(stream, IReadOnlyList<RuneRange>)` overload ([RuneRange.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/PeachPDF/RuneRange.cs)) additionally scopes that font to specific code-point ranges — the programmatic equivalent of an `@font-face unicode-range` descriptor — so characters outside the declared ranges fall back to another registered font via the [per-character matching](#per-character-font-matching). `AddFontFamilyMapping` maps one family name onto another. See [Fonts](usage-examples.md#fonts) for usage.

### The declarative builder

`PdfGenerator.CreateDocument` (and `DocumentBuilder`/`IPageDescriptor` under `Layout/`) lets an application describe pages in C# instead of HTML. The descriptors do not have their own layout: they fill an engine `DeclarativePage` through `PeachBrackets.Building` (`DocumentNode`, `StyleSheet`, `DocumentBuildContext`), which the engine lays out exactly as it would a parsed document, so declarative and HTML pages share every layout and paint behavior. See [Declarative API](declarative-api.md).

### Tagged PDF, forms and annotations

PeachPDF adapts the engine's neutral seams to the PDF writer in `PdfFeatures/`. These are described with the engine side in [Tagged PDF (PDF/UA) structure tree](#tagged-pdf-pdfua-structure-tree); the summary is that `StructureTagBuilder` implements the engine's `IStructureTagSink` over `PdfTagRecorder`, `FormFieldBuilder` turns `PageFormField` into `PdfFormBuilder` fields, and `AnnotationBuilder` turns the engine's `PageAnnotation` into `PdfAnnotationBuilder` definitions.

### Embedded files (attachments)

`PdfGenerateConfig.Attachments` embeds arbitrary files document-wide. `PdfEmbeddingPlan` validates the request
(PDF/A-3 or no PDF/A level, unique Latin-1 names, a MIME type per file) and freezes a copy of it;
`PdfEmbeddingPlan.Apply` builds a `PdfEmbeddedFile` (the stream, with `/Params /Size` and `/ModDate`, optionally
Flate-compressed as it is written) and a `PdfFileSpecification` (`/F`, `/UF`, `/Desc`, `/AFRelationship`, and an
`/EF` dictionary naming the stream under both `/F` and `/UF`) per file, then `PdfCatalog.AddAttachment` indexes the
specification twice: in the catalog's `/AF` array and in the `/Names /EmbeddedFiles` name tree.

Two constraints shape it. Rendering re-enters `RenderPagesCore` once per `AddPdfPages` call *and* once per
declarative `Page(...)`, so the plan is remembered on `PdfDocumentOptions` (`EmbeddingPlan`), compared against on
every later call (a different set throws, exactly like a changed `PdfAConformance`), and written once
(`EmbeddingApplied`). And the writer renumbers every object at save time, so the name tree stores real
`PdfReference` items rather than literals carrying object numbers. The tree is a single flat leaf (`/Names`
only), which is valid at any size and needs no `/Limits` bookkeeping. See
[Embedding files](usage-examples.md#embedding-files-pdfa-3-attachments) for the user-facing behavior.

`Apply` also remembers each file's `PdfFileSpecification` by name, so a `FileAttachment` annotation
(`-peachpdf-pdf-annotation-type: file-attachment`, see [PDF Annotations](html-css-support.md#pdf-annotations-support))
can point its `/FS` at the specification that already holds the stream - one embedded file reachable from `/AF`, from
the name tree and from every icon that names it.

### Annotations from CSS

`AnnotationPropertyCollector` (in `PeachBrackets`) walks the fragment tree page by page and turns every box whose
`-peachpdf-pdf-annotation-type` is set into a `PageAnnotation`: the page-local rectangles of the fragment (the lines of
an inline, the box of a block) are placed on the page in points the way links are, with transforms and fixed/sticky
displacement followed. An empty inline has no fragment, so it is placed where the content before it ends. PeachPDF's
`AnnotationBuilder` turns each `PageAnnotation` into a `PdfAnnotationDefinition` for the writer's `PdfAnnotationBuilder`, with
the appearance stream drawn by `PdfAnnotationAppearance` (icons, text marks, shapes, free text, stamps, ink), which the writer
package exposes so a caller with no HTML can write the same annotations.

### PeachPDF.Cli

**Guide:** [Command-line tool](cli.md) · **Source:** [src/PeachPDF.Cli/](https://github.com/jhaygood86/PeachPDF/tree/main/src/PeachPDF.Cli/)

The `peachpdf` tool: a thin front end (`ArgumentParser` with a conventional command-line grammar, `CliOptions`, `CliRunner`, `CliNetworkLoader`, `CliLogger`) over `PdfGenerator`, published as per-platform Native AOT binaries (`net10.0` only). It doubles as the library's AOT smoke test: a successful `dotnet publish -p:PublishAot=true` proves the whole pipeline — HTML parsing, CSS cascade, layout and PDF writing — compiles ahead of time and runs. The published binary's assembly name stays `PeachPDF.Cli` (a name differing only in case from `PeachPDF` collides in project-reference restore); the release workflow renames it to `peachpdf` when packaging.

## Testing

Every phase described above is exercised by an automated test suite and a continuous-integration pipeline that verifies output correctness across platforms. Beyond the automated gate, graphics-state output is verified during development by rasterizing PDFs with two independent renderers (PDFium and MuPDF) to confirm they actually *look* right, not merely that they contain the expected operators — a manual practice, not a CI step. See [How PeachPDF Is Tested](testing.md) for the full picture.

Each package is tested by a project of its own that references only that package's public surface (plus the internals it is explicitly granted):

| Package | Test project(s) |
|---|---|
| `PeachDrawing.Text` | `PeachDrawing.Text.Tests`; `PeachDrawing.Text.Brotli.Tests` for the managed codec |
| `PeachDrawing.Core` | `PeachDrawing.Core.Tests` |
| `PeachDrawing` | `PeachDrawing.Tests` |
| `PeachDrawing.PDF` | `PeachDrawing.PDF.Tests` |
| `PeachDrawing.Svg`, `.Skia`, `.Win2D`, `.CoreGraphics` | `PeachDrawing.Svg.Tests`, `PeachDrawing.Skia.Tests`, `PeachDrawing.Win2D.Tests`, `PeachDrawing.CoreGraphics.Tests` |
| `PeachBrackets` | `PeachBrackets.Tests` (public API only) and `PeachBrackets.Engine.Tests` (box tree, layout and paint internals) |
| `PeachBrackets.WinUI`, `.Android`, `.Apple` | `PeachBrackets.WinUI.Tests` and `.UITests` (a real window), `PeachBrackets.Android.Tests` (logic, no emulator), `PeachBrackets.Apple.Tests` and `.AppTests` (an offscreen window) |
| `PeachPDF` | `PeachPDF.Tests` (what ends up in the saved PDF, written back through a PDF reader) |
| `PeachPDF.Cli` | `PeachPDF.Cli.Tests` |
| `PeachPDF.SourceGenerators` | `PeachPDF.SourceGenerators.Tests` (diagnostics, generated shape, incrementality) |

Rules that follow from the layering: a test that asserts on the box tree belongs with the engine; a test that asserts on what is written into a PDF belongs with `PeachPDF`; none of the lower-package test projects references PeachPDF.
