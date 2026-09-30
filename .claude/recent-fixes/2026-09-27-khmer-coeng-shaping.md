# Khmer coeng/subjoined-consonant shaping (issue #1493)

## A silent-corruption bug found and fixed during post-change review

`CssBox.AppendWordsFromText` has no dedicated script-boundary word split (its own remarks give exactly
this shape of example - a script change with no adjacent whitespace stays inside one `CssRectWord`), so
a word gluing a USE-shaped script (Devanagari/Bengali/Gujarati/Tamil) directly against Khmer with no
separating boundary could resolve **both** `CssRectWord.EffectiveUseCategories` and
`EffectiveKhmerCategories` non-null on the very same word - each is computed independently over the
whole word span and returns non-null as soon as any character in it is non-inert.
`CssBox.ResolveWordShapingFeatures` forwarded both into one `ShapeSettings` unconditionally, so
`GsubShaper.Shape` would run `ApplyUseShaping` then `ApplyKhmerShaping` back-to-back over the same
glyph list - and `ApplyUseShaping`'s own conjunct-formation stage can shrink `glyphs.Count` first,
silently misaligning `ApplyKhmerShaping`'s category-to-glyph mapping and corrupting the Khmer half's
own reorder/classification with no exception at all. `Paragraph.ShapePiece` already enforced
"at most one of `JoiningForms`/`UseCategories`/`KhmerCategories`" via its own `if`/`else if` chain, so
ordinary paragraph-driven engine text was never at risk - `ResolveWordShapingFeatures` was the one call
site that forwarded whatever a `CssRectWord` already carried instead of deriving one-of-three itself.
Fixed with the same precedence order `ShapePiece` already uses (`JoiningForms` > `UseCategories` >
`KhmerCategories`), plus `ApplyKhmerShaping`'s own category snapshot now keys by `PlacedGlyph.ClusterStart`
rather than raw position (matching `ApplyUseShaping`'s own established technique) as defense in depth,
so a future caller that ever broke the one-of-three invariant again would degrade a misaligned glyph to
`KhmerCategory.Other` instead of silently misclassifying the wrong one. Regression test:
`MixedUseShapedScriptsCharacterizationTests.EndToEndLayout_DevanagariGluedDirectlyToKhmerWithNoBoundary_NeverForwardsBothCategoriesAtOnce`.

## Load-bearing idea

Khmer is **not** a fifth Universal Shaping Engine script. Confirmed by reading current HarfBuzz
source (commit `409c467b8259ad5fcc3fdcc477a1796fec256853`, retrieved 2026-09-27) before writing any
shaping logic, per the task's own instruction not to guess: Khmer still has its own dedicated shaper
(`hb-ot-shaper-khmer.cc`/`hb-ot-shaper-khmer-machine.rl`), HarfBuzz's older, pre-USE "Indic" shaper
family (shared with Myanmar and the legacy Indic shaper), completely separate from
`hb-ot-shaper-use.cc`. The two share only their raw `Indic_Syllabic_Category`/`Indic_Positional_Category`
UCD inputs - the category alphabet, syllable grammar, and reorder algorithm are all genuinely
different. This is why the port added a parallel `KhmerCategory`/`KhmerCategoryClassifier`/
`KhmerSyllableScanner`/`KhmerReorderer` set under `Internal/Text/Shaping/Khmer/` rather than widening
`UseCategory`/`UseCategoryClassifier`/`UseSyllableScanner`/`UseReorderer`, exactly as the plan's own
"this is plausible" caveat anticipated.

A second, non-obvious divergence: **`GsubShaper`'s Khmer stage runs its reorder pass *before* the
font's `locl`/`ccmp`/basic (`pref`/`blwf`/`abvf`/`pstf`) features, the opposite order from the
Universal Shaping Engine's own `ApplyUseShaping`** (which runs its pre-processing/basic features
first, then reorders). Confirmed two ways: reading HarfBuzz's own `collect_features_khmer` (which
registers `reorder_khmer` as a GSUB pause before any feature is even enabled), and empirically -
shaping KA+COENG+RO+a pre-base vowel sign through real HarfBuzz (`uharfbuzz`) for a real font gives
`[vowel, pref-substituted coeng+Ro glyph, base]`; the vowel could only land ahead of the
already-front-moved coeng+Ro pair if the reorder ran before the substitution, not after. Getting this
stage order backwards would have shipped output that happened to look plausible but silently
diverged from every real Khmer renderer for exactly this (extremely common - a subjoined RO plus any
leading vowel) sequence.

Also non-obvious: `KhmerReorderer.ReorderSyllable` is **one single left-to-right loop**, not two
independent passes the way `UseReorderer` (Devanagari-family) is. A coeng+RO move and a pre-base
vowel move share one loop variable over the same mutated glyph array, so whichever the loop reaches
second lands closer to the syllable's own start - verified against the same real-font trace above.

## What was found by running it, not by reading it

- Real HarfBuzz decomposes five Khmer vowel signs (`U+17BE`, `U+17BF`, `U+17C0`, `U+17C4`, `U+17C5`)
  into two glyphs at shaping-normalization time (`decompose_khmer`) and **never recomposes them**
  (`compose_khmer` unconditionally refuses whenever the first decomposed part is a combining mark,
  which it always is here) - confirmed by shaping KA+`U+17BE` through the real, unsubsetted upstream
  variable font: 3 glyphs out, not the 1 a font's own direct `cmap` coverage of `U+17BE` would
  suggest. This was not obvious from reading `decompose_khmer`'s own source alone (a "the font
  probably has this covered directly and recomposition is idempotent" reading would have been wrong).
  Not ported - this codebase has no general shaping-time Unicode normalization pass at all (not
  Khmer-specific) - so these five codepoints were deliberately kept out of both the bundled test
  font's codepoint set and the showcase's own sentence (originally drafted using a real word,
  ច្រើន "many", that turned out to need exactly this decomposition - swapped for a different,
  equally real sentence that avoids it). Recorded as a remaining gap in
  `.claude/accepted-gaps/no-text-shaping.md`, not silently shipped.
- Real HarfBuzz's Khmer `pref`/`blwf`/`abvf`/`pstf` features are per-syllable-masked in the reference
  implementation; applying them globally instead (this port's own v1 simplification, already
  established for the Universal Shaping Engine's equivalent features) was verified to produce
  byte-for-byte identical output to real HarfBuzz for every case this port's characterization tests
  cover - the font's own coverage/context tables only ever matched the intended sequences.

## What was deliberately not done, and why

- `cfar` (a narrow Microsoft-Khmer-font disambiguation feature) is not requested - no bundled test
  font defines it, and HarfBuzz's own comment scopes it to one specific rare ambiguity.
- Dotted-circle insertion for a broken cluster is not implemented, matching this port's own
  pre-existing scope for the four USE scripts (neither inserts one either).
- SVG `<text>`/`<tspan>` is not wired - `SvgRenderer.ResolveComplexScriptRuns` still only recognizes
  Arabic-family joining and Devanagari USE, matching the existing (already-documented) gap for
  Bengali/Gujarati/Tamil.

## Evidence

- `KhmerCategoryClassifierTests`, `KhmerSyllableScannerTests`, `KhmerReordererTests` (pure-logic unit
  tests, PeachDrawing.Text.Tests) - full suite: 6470 passed, 0 failed (net8.0, Debug).
- `KhmerUseShapingCharacterizationTests` (PeachDrawing.Text.Tests) - 10 cases, every expected glyph
  ID/order cross-checked against real HarfBuzz's own output (`uharfbuzz` 14.4.0) for the exact
  bundled font file, covering: a bare consonant, coeng+RO (`pref`, reordered), coeng+other-consonant
  (`blwf`, in place), one vowel sign per reorder-relevant position, coeng+RO plus a pre-base vowel
  together (the single-loop ordering proof), a Robat sign, and two adjacent syllables.
- `KhmerUseCharacterizationTests` (PeachPDF.Tests/Html/Core) - 4 end-to-end HTML-pipeline cases
  (script tag/category resolution, a real-shaping narrower-than-unshaped measurement regression
  proof, a plain-Latin no-op proof, and a mixed Khmer+Latin paragraph proof).
- `MixedUseShapedScriptsCharacterizationTests` gained one case mixing Devanagari (a USE-shaped
  script) with Khmer in the same paragraph, proving `CssBox.UseCategories`/`CssBox.KhmerCategories`
  stay two independent, correctly-sliced allocations.
- Full `PeachPDF.Tests` suite (net8.0, Debug): 14491 passed, 0 failed, 9 skipped (pre-existing,
  platform-specific).
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings, 0 errors, across net8.0/net10.0/net11.0.
- Showcase (`dictionary_line_breaking` in `PeachPDF.TestHarness/Program.cs`): the Khmer block-glyph
  placeholder replaced with a real "Noto Sans Khmer" subset font
  (`assets/fonts/NotoSansKhmerSubset.ttf`) and a real sentence exercising both a `pref`-reordered
  coeng+RO pair and two `blwf` coeng+other-consonant pairs; rasterized with both PDFium and MuPDF and
  visually confirmed to render correctly-stacked subjoined consonants, not separate nominal glyphs.

## Known small duplication, and a pattern worth watching

`KhmerCategories` is now a third parallel "shaping category slot" bolted onto `ShapeSettings`/
`GsubShaper`/`CssBox`/`CssRectWord`/`CssBidiParagraphResolver`, after `JoiningForms` and
`UseCategories` - each needs its own copy of the same wiring shape (an `IsEmpty` check, a `ccmp`/`locl`
default-tag gate, a `ToRuneIndexed*` slicer, a null-check chain through `ResolveWordShapingFeatures`).
This is not the same CSS-value-grammar duplication `CssLayoutEngine`'s own "don't write two parsers
for the same grammar" convention warns against (checked explicitly - Khmer's and USE's shaping
grammars are genuinely different, not one grammar reimplemented twice), so it was not generalized
here. If a fourth script-family shaping model is ever added (Myanmar is the next-most-likely
candidate, sharing Khmer's own pre-USE "Indic" shaper family in real HarfBuzz), a shared
`ScriptShapingCategory`-style slot would be worth revisiting instead of a fourth parallel copy.
`CssBox.ToRuneIndexedUseCategories`/`ToRuneIndexedKhmerCategories` themselves were generalized into
one shared `ToRuneIndexedCategories<T>` (mirroring `CssRectWord.SliceRuneData<T>`'s own existing
generic-extraction precedent) during this change's own post-change review pass, since that one part
genuinely was a byte-for-byte structural copy differing only in the field/enum/sentinel value.

`Paragraph.ShapePiece` was also tightened during review: it built a `List<KhmerCategory>` (and,
identically, a `List<UseCategory>`) for every piece of a paragraph containing Khmer/USE-shaped text
*anywhere*, even for a piece whose own atom is a different script entirely (e.g. an embedded Latin
numeral run inside Khmer body text) - the list was only ever read if that piece's own script also
matched. Both lists are now built only when the piece's own `atom.Script` is actually in
`KhmerShapedScripts`/`UseShapedScripts`, mirroring the existing `anyJoining` short-circuit already used
for `JoiningForms`.

**Pre-existing cache-growth pattern, not introduced here**: `GsubShaper`/`GposPositioner`'s
`ConcurrentDictionary<ShapeSettings, ...>` lookup-index caches key on the whole `ShapeSettings` record,
and `List<T>` uses reference equality in a record's generated equality - so every distinct piece of
Khmer (or USE) text builds a fresh `KhmerCategories`/`UseCategories` list that never equality-matches a
prior cache key, meaning these caches never hit for Khmer/USE text at all and grow unboundedly for the
lifetime of the typeface's `GsubTable`. `KhmerCategories` inherits this exact characteristic from
`UseCategories`/`JoiningForms` rather than introducing it - the actual fix (keying the cache on the
structural fields that matter, not the whole settings record) is a separate, pre-existing architectural
concern out of scope here.

Two smaller, left-as-is duplications from the same review pass: `KhmerSyllableScanner.ConsumeYgroup`
re-implements the same "consume while category equals target" loop shape
`UseSyllableScanner.ConsumeWhile` already factored out (not reused directly - the two scanners work
over different enums, `KhmerCategory` vs `UseCategory`, in different namespaces), and
`KhmerReorderer.MoveRangeToStart` duplicates the shift-loop shape of `UseReorderer.MoveForward`,
generalized to move 1-2 glyphs at once instead of exactly 1. Both are small (under 25 lines) and
script-specific enough that sharing them would mean a new cross-namespace generic utility for
marginal benefit - left as known small duplication rather than force-shared.

## Scope note

This closes the shaping half of issue #1493 (real coeng/subjoined-consonant stacking, verified
byte-for-byte against real HarfBuzz) with the remaining gaps above narrower and more precisely scoped
than the original issue - not a full, zero-gap HarfBuzz Khmer shaper reimplementation (which would
additionally mean building a general Unicode normalization pass this codebase has never needed
before, for five codepoints).
