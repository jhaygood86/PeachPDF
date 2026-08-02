# font-variant completion: real GSUB caps/numeric/east-asian/ligatures, font-feature-settings, and a Type 3 (Alternate Substitution) reader

Adds real OpenType GSUB glyph substitution behind `font-variant-caps` (all 7 keywords),
`font-variant-numeric` (8 keywords), `font-variant-east-asian` (9 keywords), the previously-inert
`font-feature-settings`, and completes `font-variant-ligatures` (`discretionary-ligatures`/
`historical-ligatures` now really apply `dlig`/`hlig`). `font-variant` becomes a real, combinable
shorthand over all five. Also accepts Prince's proprietary `font-variant: prince-opentype(...)` as an
input-only compatibility shim, decomposed at parse time into the standard longhands above — this is
**deliberately undocumented** anywhere user-facing (see [CLAUDE.md](../../CLAUDE.md)'s instruction that
only genuine CSS standards and PeachPDF extensions get documented) and has no accepted-gap file either,
since it isn't a spec deviation PeachPDF owns — it's an input-compatibility convenience for authors
migrating from Prince, entirely invisible downstream of parsing.

## The caps synthesis fallback only fires when real GSUB genuinely can't

`font-variant-caps: small-caps`/`all-small-caps` has synthesized (uppercase + shrink) since before this
change, because no shaping engine existed. `DerivedStyle.ActualFontVariantCaps` now gates synthesis on
`RFont.SupportsFontVariantCaps` — real substitution is preferred whenever the resolved font actually has
`smcp`/`c2sc`/`titl`/etc, and synthesis is now correctly limited to the two keywords CSS actually allows
an approximation for. The other five keywords never synthesize: unsupported means `normal`.

## Generic GSUB Type 1 and Type 3 readers, one combined shaping pass

`GsubTable` gained a Single Substitution (Type 1, formats 1/2) reader and — added specifically once a
real-font search below required it — an Alternate Substitution (Type 3, format 1) reader, alongside the
pre-existing Type 4 (ligature) reader; Type 9 (Extension) is unwrapped to whichever of the three it
wraps. `GsubShaper.Shape` builds one combined "on" tag set from ligatures/caps/numeric/east-asian/
explicit `font-feature-settings` tags, resolves it to one `SortedDictionary<lookupIndex, alternateIndex>`
via the font's own `LookupList` order, and dispatches by real (unwrapped) type in a single pass — so
lookup ordering across features matches the font's own authored order rather than several
independently-ordered passes. `TextShapingFeatures` (a `record struct`) is the one value threaded through
every measure/paint call site.

**A genuine C# gotcha cost real debugging time here**: for a `record struct` primary constructor with
every parameter defaulted, a bare `new()` invokes the struct's implicit parameterless constructor
(zero-initializing every field), *not* the primary constructor evaluated with its own declared defaults —
unlike a `class`. `TextShapingFeatures.Default = new();` and `TextShapingFeatures features = new()` as a
parameter default both silently resolved `Ligatures` to `None` instead of `Default`, breaking ligatures
and caps for any call site relying on the "optional" default. Fixed by making every optional boundary
parameter `TextShapingFeatures? features = null` (nullable) with `features ?? TextShapingFeatures.Default`
resolved inside the few real boundary implementations, and by giving `Default` an explicit
`new(LigatureFeatures.Default)` (one real argument forces the primary-constructor overload).

## `SupportsAllFeatureTags` only trusted tag presence — a font could lie about "supported"

While searching for a real font to prove `petite-caps`/`all-petite-caps` render via genuine substitution
(see below), the only candidate found — web-platform-tests' `gsubtest-lookup3.otf` conformance font —
implements every feature via GSUB **Alternate Substitution** (Type 3), not the Type 1 real fonts
(Source Sans 3) use for `smcp`/`c2sc`/`titl`. `GsubTable.SupportsAllFeatureTags` previously reported
"supported" from tag presence in the `FeatureList` alone, without checking the referenced lookup was a
type the reader could actually apply. For `font-variant-caps` specifically that's a real correctness bug,
not just an academic one: a font declaring a caps feature through an unsupported lookup type (Type 2
multiple, Type 3 before this fix, or a chaining-context type) would make `ActualFontVariantCaps` resolve
away from `None`, which is exactly the signal the synthesis-fallback gate treats as "real GSUB will
handle it" — so synthesis gets skipped for a feature that then silently substitutes nothing at all,
worse than never claiming support. Fixed by having `SupportsAllFeatureTags` also verify at least one of
a tag's active lookups resolves via `GetSingleSubstitutionLookup` or (now) `GetAlternateSubstitutionLookup`
before reporting true. Regression test: `GsubAlternateSubstitutionSyntheticTests.
SupportsAllFeatureTags_RejectsATagBackedOnlyByAnUnsupportedLookupType` (a synthetic Type 2 lookup).

## Alternate Substitution needs a per-request alternate index, not just on/off

Unlike Type 1 (a fixed glyph swap), Type 3 offers a *set* of alternates per covered glyph, and CSS Fonts
Level 3 gives `font-feature-settings`'s integer meaning here: a bare boolean activation (any
`font-variant-*` longhand, or a `font-feature-settings` tag with value `1`) selects the *first* alternate
(index 0); an explicit value ≥2 selects the `(value-1)`th. `GsubShaper.GetActiveLookupIndices` now
returns a lookup-index→alternate-index map instead of a bare index set: every boolean-activated tag
resolves through one shared `defaultTags` set (index 0, no behavior change for the common case), and only
a `font-feature-settings` tag with an explicit value ≥2 gets its own single-tag lookup resolution so its
non-default index doesn't leak onto any other feature's lookups.

## Real-font search for petite-caps: EB Garamond and 7 other well-regarded OFL fonts have none

The plan's original assumption — that EB Garamond has real `pcap`/`c2pc` data — didn't survive a byte
check: downloaded and inspected all 14 built variants (including the dedicated `AllSC`/`SC` small-caps
builds) from the upstream `georgd/EB-Garamond` "nightly" release; every one has `smcp`/`c2sc`/`onum` but
zero `pcap`/`c2pc`/`unic`/`titl`. Widened the search to Alegreya, Piazzolla, Newsreader, Fraunces,
Literata, Source Serif 4, and Spectral (Google Fonts' OFL catalog, all known for rich OpenType feature
sets) — same result. The only font found anywhere with real petite-caps data is
web-platform-tests' `gsubtest-lookup3.otf` (`css/css-fonts/support/fonts/`, 3-Clause BSD) — a synthetic
conformance-test font (every glyph a covered feature can substitute to spells the literal word "PASS" or
"FAIL"), not a display typeface, bundled into `PeachPDF.Tests` only (not `PeachPDF.TestHarness` — a
PASS/FAIL test font has no place in the public showcase gallery). Its codepoint scheme (documented in its
own `gsubtest-features.js`) maps `0xE000 + 4*featureIndex` to a "default" control glyph plus one
`altN` glyph per alternate index, each substituting to the literal PASS glyph only at its one designed
index — confirmed via `fontTools` introspection (`FeatureList`/`LookupList`/`AlternateSubst` decoding),
not assumption, before writing a single test against it. `unicase` still has no known real-font
candidate; its "real substitution applied" branch remains synthetic-only, same scope as before.

## Evidence

- Full `net8.0` suite: 7529 total (7520 passed, 9 skipped), before and after every change in this batch,
  including under `--collect:"XPlat Code Coverage"` (coverage instrumentation's different JIT/timing is
  a known flakiness amplifier for the font-family-name cache — see `BundledFonts.cs`'s own doc comment —
  so this was checked deliberately, not assumed from a plain run).
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings.
- `diff-cover` against `HEAD`: 100% (630 of 630 coverable lines) — three lines flagged in early passes
  (`CssBox.ContainsUpperLetter`'s body, `DerivedStyle`'s numeric/east-asian switch arms, two GSUB
  reader defensive-return branches) were genuine gaps, not tooling artifacts, and each got a small
  targeted test rather than being left as accepted shortfall against the 90% gate.
- The `font_variant_caps` TestHarness showcase's output PDF was rasterized with both MuPDF and PDFium
  (`dpi=150`/`scale=2.0`) and visually compared per CLAUDE.md's testing conventions — small-caps/
  all-small-caps show visibly different (real vs. synthesized) glyph shapes between the Source Sans 3
  and Source Code Pro columns, oldstyle numerals show real mixed-height figures, and slashed-zero shows
  a visible slash through the digit — not just a passing content-stream substring check.
