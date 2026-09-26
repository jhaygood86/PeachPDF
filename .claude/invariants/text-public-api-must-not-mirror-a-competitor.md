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
5. **Every PR that adds public API updates the register below.** The maintainer reviewed the first slice's register and found
   the naming fine, and then waived the per-PR review for the rest of the stack (2026-09-26): the author picks the
   spec-derived name and records its origin, and a PR is not held for a naming review. A collision found later is renamed,
   not defended. `src/PeachDrawing.Text/PublicApi.txt` is the reviewed snapshot of the
   whole surface; `PublicSurfaceTests` fails when the surface and the file disagree, so nothing becomes public by accident.

## Register

The origin column is where the name and shape come from. "Ours" means we chose it because of what it does here.

### `PeachDrawing.Text` (fonts and matching)

| Public name | Role | Origin of the name and shape |
|---|---|---|
| `FontSet` (`AddData`, `AddStream`, `AddFile`, `TryFindFamily`, `TryFindCoveringFamily`, `HasExplicitRanges`, `MatchOrFallback`, `TryGetFontData`, `ResolveGeneric`, `InstalledFamilyNames`) | The fonts text can be set in: installed plus added, per instance | CSS Fonts 4 section 5, whose matching runs over "the set of available fonts". `Add*` and `Try*` follow .NET's own naming. `MatchOrFallback` and `TryGetFontData` are ours. `TryFindCoveringFamily` is the "system fallback" step of the CSS algorithm |
| `TypefaceFamily` (`Name`, `TryMatch`) | The faces sharing one family name | The family-versus-face split every platform text API has (DirectWrite, CoreText, fontconfig) |
| `Typeface` (`FamilyName`, `StyleName`, `IsBold`, `IsItalic`, `Metrics`, `HasColorGlyphs`, `TryMapRune`, `HasGlyph`, `GetAdvance`, `HasVerticalMetrics`, `GetVerticalAdvance`, `HasVerticalOrigin`, `GetVerticalOrigin`, `TryGetScriptPosition`, `SupportsFeatures`, `MatchesEmojiPresentation`) | One face, with no size | Typography's own term. Deliberately size-free, unlike a face-plus-size "font" object. Member names follow the OpenType tables they read (`cmap` to `TryMapRune`, `hmtx` to `GetAdvance`, `vmtx` and `VORG` to the vertical members) and .NET's `Try` pattern |
| `TypefaceMetrics` | The face's vertical dimensions in design units | Named for what it holds. The cell (`CellAscent`, `CellDescent`, `LineSpacing`) is the GDI/WPF text-cell vocabulary, kept apart from the `NormalLine*` triple that CSS `line-height: normal` uses (CSS Inline Layout). `UnderlinePosition`/`UnderlineThickness` and `StrikeoutPosition`/`StrikeoutThickness` are the `post` and `OS/2` fields, `CapHeight` and `XHeight` the `OS/2` ones, `XMin`..`YMax` the `head` ones. The split into two line-dimension sets is ours |
| `ScriptPlacement`, `ScriptPosition` | Recommended sub/superscript size and offset | OpenType `OS/2` `ySubscript*`/`ySuperscript*` fields; CSS `vertical-align: sub`/`super` |
| `TypefaceQuery` (`Weight`, `Width`, `IsItalic`, `MustCover`) | What a caller wants: the inputs to CSS face matching | CSS Fonts 4 section 5.2 matching inputs; `Width` is the OpenType `usWidthClass` 1 to 9 that the CSS `font-stretch` keywords map to |
| `TypefaceMatch` (`Typeface`, `Synthesis`) | A match and what is still missing from it | Ours |
| `SyntheticStyle` (`None`, `Bold`, `Italic`, `BoldItalic`) | What has to be faked | CSS `font-synthesis` |
| `AddOptions` (`FamilyName`, `Weight`, `IsItalic`, `Width`, `UnicodeRanges`) | The overrides of an `@font-face` rule | CSS `@font-face` descriptors (`font-weight`, `font-style`, `font-stretch`, `unicode-range`) |
| `RuneInterval` | An inclusive interval of scalar values | CSS `unicode-range`; the name is ours, the members are `Rune`'s |
| `GenericFamily` | A generic font family | CSS Fonts 4 generic family keywords |
| `TypefaceFormatException` | Data that is not a font | .NET exception convention |

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
than any library's. The author did not consult SixLabors.Fonts while choosing them, and so could not vouch that no name
coincides with one of theirs; that check is the maintainer's review under rule 5. The maintainer reviewed this slice's
register on 2026-09-26 and found the naming fine. That review covered the `Unicode` table; the tables added afterwards
(`PeachDrawing.Text` fonts, matching and metrics) were not reviewed name by name, because the maintainer waived the per-PR
review (rule 5).
