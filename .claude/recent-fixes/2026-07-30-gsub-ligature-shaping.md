# GSUB ligature substitution (issue #176 slice 1)

## The load-bearing idea

PeachPDF mapped every character 1:1 from codepoint to glyph via `cmap`, with `GlyphSubstitutionTable.Read`
(the GSUB table reader) an empty stub - so `f`+`i` never became a font's `ﬁ` ligature even when the font
defined one, and `font-variant-ligatures` wasn't recognized at all. This closes the ligature portion of
that gap (issue #176), narrowed and re-filed as [issue #533](https://github.com/jhaygood86/PeachPDF/issues/533)
for what's still out of scope (GPOS, GDEF mark filtering, chaining-context lookups, bidi, complex scripts).

New GSUB table reader (`PeachPDF/Fonts/OpenType/GsubTable.cs`, `CoverageTable.cs`): ScriptList/FeatureList/
LookupList common tables, Coverage formats 1/2, and Lookup Type 4 (Ligature Substitution) - Type 9
(Extension Substitution) is unwrapped down to the Type 4 subtable it wraps. `GlyphSubstitutionTable` (the
existing PDFsharp-era stub in `OpenTypeFontTables.cs`) now just locates the table and owns the parsed
`GsubTable`, keeping the stub's PDFsharp header intact and following the COLR/CPAL convention (explicit
offset reseeks) rather than the legacy tables' sequential-read style, since GSUB is an offset graph.

New shaping engine (`PeachPDF/Text/GsubShaper.cs`, parallel to `HyphenationEngine`): does the existing
per-Rune cmap walk (including symbol-font remapping), then - only when the font has GSUB and
`LigatureFeatures` requests it - a single left-to-right pass per active lookup, greedily matching the
font's own `LigatureSet` order (first match wins, not sorted by length) and merging matched spans into one
`ShapedGlyph` with a UTF-16 cluster range back into the source text. `OpenTypeDescriptor.Shape(text, features)`
exposes this as the one glyph-walk `FontHelper.MeasureString`, `XGraphicsPdfRenderer.DrawString`,
`ColorGlyphPainter.Paint`, `GraphicsAdapter.GetTextOutline`, and `CMapInfo.AddShapedText` all now share -
previously each of the first three independently re-derived "codepoint to glyph" via its own `EnumerateRunes`
loop (the exact "two independent parsers for the same thing" pattern CLAUDE.md warns against, here applied
to glyph walks rather than CSS value grammars).

`CMapInfo` gained `AddShapedText`/`LigatureGlyphToText` alongside the existing (untouched) `AddChars`/
`CharacterToGlyphIndex`, since a ligature glyph has no single Unicode scalar to key the latter on;
`PdfToUnicodeMap.PrepareForSave` merges both when building `bfrange` entries, so a rendered ligature glyph
still extracts back to its multi-character source text (verified end-to-end, not just at the glyph-mapping
layer - see Evidence).

New CSS property `font-variant-ligatures` parses the full CSS Fonts Level 3 grammar (`normal | none |
[common-ligatures|no-common-ligatures] || [discretionary-ligatures|no-discretionary-ligatures] ||
[historical-ligatures|no-historical-ligatures] || [contextual|no-contextual]`, each axis at most once) via
a bespoke `FontVariantLigaturesValueConverter` (`Map.FontVariantLigatureTokens.ToConverter().Many()` plus a
same-axis-conflict check) rather than the single-keyword `Converters.From<TEnum>()` shape `font-variant`
itself uses, since this grammar allows an unordered combination of independent toggles. Only the
common-ligatures axis (and `none`) changes `DerivedStyle.ActualFontVariantLigatures`'s resolved
`LigatureFeatures` - the other three axes parse and cascade but are documented (in code and in
`docs/html-css-support.md`) as not yet applied, not silently dropped.

## Evidence

Discovered while validating the parser against the bundled Source Sans 3 fixture: it does *not* define a
GSUB `fi`/`fl` ligature (the precomposed `ﬁ`/U+FB01 codepoint glyph exists, but no `liga` rule produces it
from "f"+"i" in this font) - it ligates "ff"/"ft"/"fft" instead, confirmed independently via `fontTools`
before trusting the C# parser's own output. All tests use these instead of the `fi`/`fl` example the
pre-GSUB characterization test (and the original issue #176 write-up) assumed.

`GetTextOutlineTests` confirms the "ff" ligature glyph has exactly 1 contour (same as a single "f") and a
narrower hmtx advance (577 design units) than two separate 'f's (2 x 292 = 584) - both independently
verified via `fontTools` - so a subpath-count and pen-advance assertion both distinguish shaped from
unshaped output structurally, not just via a content-stream substring (the exact pitfall CLAUDE.md calls
out: a token can be present while the actual result is unmerged).

`FontVariantLigaturesIntegrationTests.Ligature_RendersEmbedsAndRoundTripsToUnicode` renders "ff" through the
real `PdfGenerator` pipeline (`@font-face` data URI, matching `Format12CmapAstralTests`'s established
pattern) and asserts the saved PDF's ToUnicode stream contains `00660066` - the ligature glyph's bfrange
destination carrying both source characters, not one or neither.

Full `dotnet test --framework net8.0` suite: 7033 passed / 0 failed / 9 skipped (up from 7004 pre-change).
Zero-warning `dotnet build PeachPDF.slnx -t:Rebuild`. Diff coverage (`diff-cover` against `origin/main`): 99%.

A post-change review pass (per CLAUDE.md) against the actual CSS Fonts spec text and the OpenType
GSUB/chapter2 spec found three real defects the first version of this change shipped with, all fixed and
covered by new tests before landing:

- **`font-variant-ligatures: none`/`no-common-ligatures` was disabling `rlig` too.** The spec is explicit
  that required ligatures "are not affected by the settings above, including `none`" - `DerivedStyle
  .ActualFontVariantLigatures` now resolves the disabled case to `LigatureFeatures.Required`, not
  `LigatureFeatures.None`, so a font whose required ligatures don't overlap with its `liga`/`clig` set
  keeps shaping them even under `none`.
- **The `font` shorthand never reset `font-variant-ligatures`.** CSS Fonts 4 §7.7 lists it (along with the
  other `font-variant-*` longhands) under "Reset Implicitly" - set whenever `font` is set, even though none
  of them can be spelled out in `font`'s own grammar. Fixed by adding it to `PropertyFactory`'s `font`
  shorthand longhand list; `ShorthandProperty.Export` already resets any listed longhand the shorthand's
  grammar didn't extract a value for, so no grammar change was needed. (`font-size-adjust`/`font-palette`
  are on the same spec list but aren't wired up either - pre-existing, not introduced by this change, and
  left alone.)
- **`letter-spacing` reserved one Tc gap per source character, not per shaped glyph.** The PDF `Tc`
  operator fires once per glyph actually shown, and GSUB can merge several characters into one glyph, so a
  ligating run with `letter-spacing` set was reserving more width than it painted. Added
  `RGraphics.CountShapedGlyphs` (implemented via the same `descriptor.Shape` call `MeasureString`/
  `DrawString` already use) and switched `CssBox`'s three letter-spacing width calculations from
  `Text.Length` to it.

## Deliberately not done

No `GDEF`-based mark filtering, no chaining-context lookup application (GSUB types 5-8), no per-language
`LangSys` selection (always a script's `DefaultLangSys`), no GPOS/kerning, no bidi, no complex-script
joining - all tracked in the narrowed accepted-gap note and issue #533. SVG `<text>` has no
`font-variant-ligatures` presentation attribute/property of its own yet (always shapes as `normal`).
`discretionary-ligatures`/`historical-ligatures`/`contextual` parse but don't drive `dlig`/`hlig`/`calt`
lookup application - deliberately scoped out to keep this slice to the CSS-standard "common ligatures" axis
that's actually wired to a real shaping effect, rather than half-implementing four feature tags at once.
