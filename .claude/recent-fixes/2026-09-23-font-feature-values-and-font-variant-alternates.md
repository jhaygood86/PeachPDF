# `@font-feature-values` at-rule + `font-variant-alternates` property (#1281)

Implements CSS Fonts Module 4's `@font-feature-values` at-rule (named aliases for a font's numbered
OpenType feature indices, in nested `@styleset`/`@character-variant`/`@swash`/`@ornaments`/
`@annotation`/`@stylistic` blocks) and the `font-variant-alternates` property that references them
(`stylistic()`, `historical-forms`, `styleset()`, `character-variant()`, `swash()`, `ornaments()`,
`annotation()`). Both were previously unimplemented — `RuleType.FontFeatureValues` had no `Rule`
subtype and fell through to a no-op, and `font-variant-alternates` didn't exist anywhere in the
codebase.

## The load-bearing idea: no new rendering machinery, only CSS plumbing

`font-feature-settings` already applies an arbitrary OpenType tag by (tag, integer) pair through
`GsubShaper`'s Type 1/3 substitution readers, including the exact "integer selects the Nth glyph
alternate" convention CSS Fonts needs here (`.claude/recent-fixes/2026-08-01-font-variant-completion.md`).
So this feature is almost entirely new CSS-OM parsing + a resolution registry, modeled closely on
`@font-palette-values`/`font-palette` (`.claude/recent-fixes/2026-07-24-css-font-palette-font-palette-values-palette-mix.md`)
— a named-alias at-rule scoped to a font-family, resolved per-element against a registry built once
per document. No `GsubShaper`/`TextShapingFeatures` struct changes were needed at all; the new
`FontVariantAlternatesResolver` just produces the same `(string Tag, int Value)` pairs
`font-feature-settings` already feeds into `TextShapingFeatures.ExplicitFeatures`.

## New nested at-rule parsing pattern

`@font-feature-values`'s body holds nested at-rules (`@styleset { ident: integer+; }`, etc.) whose
declarations are arbitrary author-chosen names, not real CSS properties. `StylesheetComposer` gained
`CreateFontFeatureValues`/`FillFontFeatureValueBlocks`/`CreateFontFeatureValueSet`, modeled on
`CreateKeyframes`/`FillKeyframeRules` (outer-rule-holds-a-list-of-differently-shaped-nested-rules) for
the outer shape and `@page`'s margin-box special-casing (`FillDeclarations`'s `parentPageRule` check)
for "a nested at-rule dispatched by name, not through the generic `CreateAtRule` table." The nested
declaration factory (`PropertyFactory.CreateFontFeatureValueDescriptor`) accepts *any* ident
unconditionally (`new UnknownProperty(name)`), unlike `@font-palette-values`'s three fixed descriptor
names — there is no fixed descriptor vocabulary here, the names are the author's own feature-value
aliases.

**A real bug found only by testing an adversarial case, not by reading the code**: the first version of
`FillFontFeatureValueBlocks`'s "skip an unrecognized nested at-rule" branch advanced one token at a
time and stopped at the first `CurlyBracketClose` it saw — which is the *unrecognized* block's own
closing `}`, not `@font-feature-values`'s own. `@font-feature-values MyFont { @bogus { foo: 1; }
@styleset { real: 1; } }` silently dropped `@styleset` entirely, because the loop treated `@bogus`'s
closing brace as the end of the whole rule. Fixed by reusing `MoveToRuleEnd` (already used elsewhere for
exactly this "skip a whole scope, tracking brace depth" job) instead of a bare `NextToken()`. Regression
test: `FontFeatureValuesParsingTests.FontFeatureValues_UnknownNestedBlockIsIgnored`.

## Precedence ordering hazard found by reasoning through `GsubShaper`, not by a failing test

CSS Fonts 4 §6.8 says `font-variant-alternates` wins over `font-feature-settings` for a tag both touch.
The obvious implementation — concatenate both properties' `(tag, value)` lists into `ExplicitFeatures`
— has a latent bug: `GsubShaper.GetActiveLookupIndices` processes `defaultTags` (value 1, "on") in one
pass and `customAltIndexByTag` (value ≥ 2) in a second pass that always overwrites the first, regardless
of list order. A `font-feature-settings` entry with value ≥ 2 for a tag would silently outlast a
later-appended `font-variant-alternates` entry with value 1 for that same tag, because "later in the
list" doesn't control "which pass wins" the way it needs to. Fixed by deduplicating in `DerivedStyle`
before the list ever reaches `GsubShaper` (`MergeExplicitFeatures`: a plain `Dictionary<string, int>`
overlay, alternates applied after feature-settings) rather than extending `GsubShaper`'s own
`ReservedTags` — that set is a fixed enum-backed vocabulary for the closed-set `font-variant-*`
longhands (caps/numeric/east-asian/etc.), not a fit for the open-ended `ss01`–`ss20`/`cv01`–`cv99`/
`salt`/`swsh`/`ornm`/`nalt`/`hist` tag set `font-variant-alternates` produces.

## Real-font search succeeded on the first well-known candidate

Needed a real font with genuine numbered-stylistic-set GSUB data for the showcase and an integration
test proving actual glyph substitution (not just tag plumbing) — the prior caps/petite-caps search
(2026-08-01) found *no* real-font candidate anywhere for `pcap`/`c2pc` after checking 8 well-regarded
OFL families, so this was budgeted as similarly uncertain. It wasn't: ArrowType's **Recursive**
(OFL 1.1, Google Fonts) has real `ss01`–`ss12`+`ss20` GSUB Single Substitution data on the first check —
`ss01` swaps a double-story lowercase "a" for a single-story form, `ss02` does the same for "g", both
confirmed via direct `fontTools` GSUB introspection before writing anything against it. Bundled as
`assets/fonts/RecursiveSubset.ttf` (basic Latin/digits/punctuation subset of the "Mono Casual Static"
Regular instance, `generate_recursive_subset.py`, mirroring the Nabla subset script's shape).
`character-variant()`/`swash()`/`ornaments()`/`annotation()`/`stylistic()` have no real-font backing
found yet — covered by unit/resolver tests against a hand-authored `@font-feature-values` registry
instead, not by a second real-font search; `styleset()` alone was judged sufficient real-font proof for
the shared mechanism (the same reasoning `font-palette`'s single Nabla font was judged sufficient
proof, not a font per CPAL feature).

## Deliberately not done

SVG `<text>`/`<tspan>` does not consult `font-variant-alternates`/`@font-feature-values` at all — see
`.claude/accepted-gaps/font-variant-alternates-svg-scope.md` ([issue #1293](https://github.com/jhaygood86/PeachPDF/issues/1293)),
consistent with `font-palette`'s own existing HTML-only scope.

## Evidence

- `net8.0` suite green (including the new `FontFeatureValuesParsingTests`,
  `FontVariantAlternatesParsingTests`, `RegisteredFontFeatureValuesTests`,
  `FontVariantAlternatesResolverTests`, `FontVariantAlternatesIntegrationTests` — the last including an
  actual decoded-glyph-outline comparison between `styleset(simple-a)` on and off against the real
  Recursive font, per CLAUDE.md's "a content-stream-substring/tag-presence check is not proof" testing
  convention).
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings.
- The `font_variant_alternates` showcase was rasterized with both PDFium and MuPDF and visually
  compared: `normal`/`styleset(simple-a)`/`styleset(simple-g)`/`styleset(simple-a, simple-g)` each show
  a visibly different, renderer-agreeing glyph shape for "a"/"g" — not just a passing content-stream
  token.
