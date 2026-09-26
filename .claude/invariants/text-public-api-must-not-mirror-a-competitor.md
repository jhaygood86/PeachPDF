# The PeachDrawing.Text public API must not mirror a competitor's

A maintainer of a commercial competitor has already objected that another PeachDrawing-family library (PeachImage) looked
"too similar" to theirs. `PeachDrawing.Text` is a font and text library, and SixLabors.Fonts is the obvious neighbour, so
the public surface is designed from PeachPDF's requirements and from the specifications, never from that library.

## The rules

1. **Requirements first.** A public type exists because PeachPDF, or a documented use, needs the capability. Its shape
   follows the specification it implements. Vocabulary comes from the specs (CSS property and keyword names, OpenType table
   and feature names, Unicode algorithm and property names) and from prior art that is not SixLabors.Fonts: HarfBuzz,
   FreeType, DirectWrite and CoreText, and the Skia/Flutter paragraph model.
2. **Structural differences, not renames.** A `Typeface` is size-free (no face-plus-size "font"). Text layout is an immutable
   prepared `Paragraph` laid out at any width into an immutable snapshot. Outlines and colour glyphs are returned as data,
   not through a render callback. Options and enums use CSS's names. Geometry is BCL types and code points are `Rune`.
3. **Naming.** No public type shares both a name and a role with a SixLabors.Fonts type; no method or enum-member set is
   copied; doc-comment prose is written fresh. Everyday typographic terms (`Rune`, `Glyph`, `Metrics`) are fine, never as a
   matching bundle.
4. **Clean-room process.** Implementers do not read SixLabors.Fonts source and do not use its docs as a structural template.
   The published docs carry a factual feature-coverage comparison only, like the one on `why-peachpdf.html`, and no
   migration guide from their calls to ours.
5. **Every PR that adds public API updates the register below** and is reviewed against it with the maintainer before it
   merges. A collision found is renamed, not defended. `src/PeachDrawing.Text/PublicApi.txt` is the reviewed snapshot of the
   whole surface; `PublicSurfaceTests` fails when the surface and the file disagree, so nothing becomes public by accident.

## Register

The origin column is where the name and shape come from. "Ours" means we chose it because of what it does here.

### `PeachDrawing.Text.Unicode`

| Public name | Role | Origin of the name and shape |
|---|---|---|
| `Bidi` (`Analyze`, `ReorderLine`, `ClassOf`, `TryGetMirror`, `Mirror`, `Reverse`) | UAX #9 resolution and line reordering | UAX #9's own split between paragraph-level resolution and line-level reordering (rule L2). The spec calls the property `Bidi_Class` and the mirroring `Bidi_Mirroring_Glyph`. `Analyze` and `Mirror` are ours |
| `BidiAnalysis` | Levels per code unit and the paragraph level | Ours; the spec's "resolved levels" and "paragraph embedding level" |
| `BidiClass` | The 23 `Bidi_Class` values | UAX #9 table 4; member names are the spec's abbreviations |
| `BidiRun` | A maximal run of equal level | UAX #9 "level run" |
| `BaseDirection` | Direction a paragraph starts in, `Auto` for detection | UAX #9 "base direction" (P2, P3) with CSS `direction` values |
| `EmbeddingSpan`, `ExplicitPush` | A directional push over a range with no control character in the text | Ours, from CSS Writing Modes' synthetic pushes; `ExplicitPush` members are the seven control characters' names in UAX #9 |
| `Scripts` (`Of`, `Resolve`, `ResolveLooked`, constants) | Script property and UAX #24 run resolution | UAX #24 "Script" property, `Common`, `Inherited`, `Unknown` are the UCD's value names; `Resolve` is section 5.1 |
| `OpenTypeTags` (`ForScript`, `ForLanguage`) | Script and language to OpenType tags | OpenType "script tag" and "language system tag" |
| `VerticalOrientation`, `VerticalOrientationClass` | UAX #50 property and the CSS `text-orientation: mixed` decision | UAX #50 `Vertical_Orientation` values `U`, `R`, `Tu`, `Tr` |
| `DefaultIgnorables` | The `Default_Ignorable_Code_Point` property | The UCD property name |
| `Hyphenator` (`FindBreakPoints`) | Liang-pattern hyphenation | TeX hyphenation; `FindBreakPoints` is ours |
| `Emoji` (`Resolve`, `ResolveAt`, `IsPresentationParticipant`, `IsPresentationSelector`, `SelectorFor`), `EmojiMode`, `EmojiPresentation` | Choice between text and emoji presentation | UTS #51 and CSS Fonts 4 `font-variant-emoji` (`normal`, `text`, `emoji`, `unicode`) |

Every name above was taken from the specification it implements, and the member sets follow those specifications rather
than any library's. The author did not consult SixLabors.Fonts while choosing them, and so cannot vouch that no name
coincides with one of theirs; that check is the maintainer's review under rule 5, and it is still to be done for this slice.
