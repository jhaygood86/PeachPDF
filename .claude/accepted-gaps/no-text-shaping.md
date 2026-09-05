# No text shaping (remaining OpenType Layout gaps)

PeachPDF now implements essentially all of real OpenType Layout a font can exercise, including
Arabic-family complex-script joining: GSUB Lookup Types 1 (single), 2 (multiple), 3 (alternate), 4
(ligature), 5/6 (contextual/chaining context substitution, all three formats - glyph, class,
coverage - including a rule whose own `SequenceLookupRecord` targets another contextual/chaining
lookup, recursed into to an arbitrary depth rather than skipped, since real fonts genuinely chain
these to narrow a glyph's presentation form by successively more specific context - see
`GsubShaper.ApplyNestedLookup`'s own remarks and this feature's own recent-fixes entry for a real
Gujarati font that needs exactly this), and 8 (reverse chaining context single substitution,
end-to-start), unwrapping Type 7 (Extension Substitution) transparently (see
`PeachPDF.Text.GsubShaper`, `PeachPDF.Fonts.OpenType.GsubTable`/`CoverageTable`/`ClassDefTable`);
GDEF-based mark filtering of a lookup's `lookupFlag`, consulted by ligature (Type 4) component
matching, GSUB contextual/chaining (Types 5/6/7/8) backtrack/input/lookahead matching, and GPOS
mark-attachment/cursive base search alike (see
`PeachPDF.Text.GlyphSequenceFilter`/`PeachPDF.Fonts.OpenType.GdefTable`); GPOS Lookup Types 1/2
(single/pair adjustment - kerning), 3 (cursive attachment, a direct port of real HarfBuzz's own
main-direction formula - see `GposPositioner.TryApplyCursivePair`), 4/5/6 (mark-to-base/mark-to-ligature/
mark-to-mark attachment), and 7/8 (context/chained context positioning), unwrapping Type 9 (Extension
Positioning) (see `PeachPDF.Text.GposPositioner`, `PeachPDF.Fonts.OpenType.GposTable`); per-language
GSUB feature selection (a script's language-specific `LangSys`, chosen via the element's
nearest-ancestor `lang`/`xml:lang` - see `CssBox.Language` - resolved to an OpenType language tag via
a curated `PeachPDF.Text.OpenTypeLanguageTags` table); Arabic/Syriac-family joining-form resolution -
Unicode `Joining_Type`/`Joining_Group` driving a ported HarfBuzz state machine
(`PeachPDF.Text.Shaping.Arabic.ArabicJoiningShaper`) that requests a font's `init`/`medi`/`fina`/`isol`/
`curs` features, with shaping always run in true logical order and only the resulting glyph list
reversed for RTL display (`OpenTypeDescriptor.Shape`'s `ReverseForDisplay`, `CssRectWord.DisplayOrderReversed`)
so a font's own contextual `rlig` rules (e.g. Arabic lam-alef) still match; the Universal Shaping
Engine (USE) syllable reordering for Devanagari, Bengali, Gujarati, and Tamil - Unicode
`Indic_Syllabic_Category`/`Indic_Positional_Category` driving a ported HarfBuzz category
classifier/syllable grammar/two-pass glyph reorder
(`PeachPDF.Text.Shaping.Use.UseCategoryClassifier`/`UseSyllableScanner`/`UseReorderer`) that requests a
font's `nukt`/`ccmp`/`locl`/`akhn`/`rphf`/`rkrf`/`abvf`/`blwf`/`half`/`pstf`/`vatu`/`cjct`/`abvs`/`blws`/
`haln`/`pres`/`psts` features and repositions reph/pre-base matras in the resulting glyph list (verified
byte-for-byte against real HarfBuzz's own output for a real font per script, `uharfbuzz`, and against
two PDF renderers - see this feature's own recent-fixes entries); and a real UAX#9 Unicode Bidi
Algorithm (see `PeachPDF.Text.Bidi.BidiResolver`).

## Remaining gaps

- **`GsubShaper.TryApplyRphf`'s ligature match (Lookup Type 4) has no upper bound confining it to the
  syllable it was invoked for.** It mirrors HarfBuzz's own `setup_rphf_mask` by starting the match
  exactly at a syllable's own start (see that method's own remarks on why that alone reproduces the
  masked behavior for the common case), but a real font's `rphf` ligature rule needing more components
  than remain in an unusually short/malformed (`BrokenCluster`) syllable could still consume glyph(s)
  belonging to the next syllable - real `rphf` rules are essentially always exactly RA+VIRAMA (2
  components, occasionally +ZWJ for 3), so this needs both an unusual font *and* a pathologically short
  leading syllable to reach; not implemented since no real font or test case has been found that hits
  it.
- **`GsubShaper.TryApplyRphf`'s Type 1 (single) branch infers "did a substitution fire" from whether the
  resulting `GlyphIndex` differs from the input**, unlike HarfBuzz's own `record_rphf_use` (which checks
  a dedicated "was substituted" bit, independent of whether the output glyph ID happens to differ). A
  font whose `rphf` coverage table maps a glyph to itself (a legal, if pointless, identity substitution)
  would not be recognized as having formed a reph. Not implemented since no real font has been found
  authored this way, and detecting it properly would mean duplicating `GsubSingleSubstitutionSubtable`'s
  own coverage-matching logic outside `ApplySingleSubstitutionAt` rather than reusing it.
- **A hyphenation split (`CssLayoutEngine.TryHyphenateWord`) does not carry a word's own
  `ScriptTag`/`EffectiveJoiningForms`/`EffectiveUseCategories` onto the prefix/suffix `CssRectWord`s it
  creates** - a hyphenated Arabic-family-joining or USE-shaped (Devanagari/Bengali/Gujarati/Tamil) word
  loses real shaping across the split point. Pre-existing for `EffectiveJoiningForms` since Arabic
  joining landed; now also covers `EffectiveUseCategories`. Low practical impact today (this repo's
  `hyphens: auto` pattern data has no Arabic/Devanagari/Bengali/Gujarati/Tamil-language entries - see
  [`hyphens: auto` language coverage](docs/html-css-support.md#hyphens-auto-language-coverage) - so only
  an explicit, author-placed soft hyphen in text in one of these scripts can reach this path at all) but
  not fixed here, since doing so means widening `TryHyphenateWord`'s own word-splitting logic to slice
  all three fields the same way `CssBox.AppendWordsFromText` already does, a change to shared
  hyphenation code rather than this feature's own files.
- **Per-language selection is a curated BCP-47 → OpenType-tag subset**, not the full ~7000-row
  OpenType Language System Tags registry (not mechanically derivable from BCP-47). A language absent
  from the table simply falls back to the script's `DefaultLangSys`, same as before this existed.
- **Indic script reordering covers Devanagari, Bengali, Gujarati, and Tamil, not the full USE-driven
  Brahmic family.** `UseCategoryClassifier` only ports the subset of HarfBuzz's own category predicates
  a codepoint in one of these four scripts' own blocks can ever satisfy (verified by enumerating every
  codepoint in all four blocks against the real UCD data, not assumed) - a script needing medial
  consonants (Javanese/Sundanese/Batak), a static Sakot/Reordering_Killer/Consonant_With_Stacker
  codepoint (Tibetan, others), or a *statically* UCD-assigned repha (`Consonant_Preceding_Repha`/
  `Consonant_Prefixed` - none of these four scripts' own repha is statically assigned; each is entirely
  font-GSUB-driven, see `GsubShaper.TryApplyRphf`'s own remarks) falls back to the classifier's `O`
  ("other", non-participating) category rather than its true one, so such text shapes with each
  codepoint's nominal glyph only, same as before this feature existed. Of the three scripts added
  alongside Devanagari, only Bengali needed categories beyond Devanagari's own reachable set (`GB` for
  its Consonant Placeholder, `FMAbv` for its one Syllable Modifier) - Gujarati and Tamil needed zero new
  classifier/scanner code. Extending to a script family beyond these four (Gurmukhi, Kannada, Malayalam,
  Oriya, Telugu, and the rest) means widening `UseCategoryClassifier`/`UseCategory`/
  `UseSyllableScanner`'s grammar further, not a new mechanism.
- **`UseSyllableScanner`'s CGJ/ZWNJ handling is narrower than HarfBuzz's own.** A Combining Grapheme
  Joiner/variation-selector codepoint that isn't already given its own dedicated `Indic_Syllabic_Category`
  value (unlike ZWJ, which is) isn't recognized as transparent at all (this classifier omits the
  `Default_Ignorable_Code_Point` check HarfBuzz's own `is_CGJ` also consults, to avoid a third
  UCD-derived table for an edge case that's rare directly adjacent to real text in these scripts); and a
  CGJ appearing in the *middle* of a syllable's tail (rather than only between two already-scanned
  syllables) ends that syllable early instead of being skipped transparently mid-scan. Both degrade to
  two adjacent syllables instead of one rather than crashing or mis-rendering catastrophically.
- **`nukt`/`ccmp`/`locl`/`akhn` and the 7 "orthographic unit shaping" features
  (`rkrf`/`abvf`/`blwf`/`half`/`pstf`/`vatu`/`cjct`) apply globally, not masked to each syllable's own
  span the way real HarfBuzz masks them.** For well-formed text a font's own coverage/context tables
  only match the sequences they're authored for, so this produces the same result in practice - real
  HarfBuzz's own per-syllable OpenType feature masking exists mainly to keep one syllable's substitution
  from ever reaching into a neighboring syllable's glyphs, a general mechanism this codebase has no
  other use for yet (`GsubShaper.ApplyUseShaping`'s own remarks).
- **`pref` (pre-base-reordering consonants) and the topographical `isol`/`init`/`medi`/`fina` features
  are not requested at all** - none of Devanagari/Bengali/Gujarati/Tamil has a codepoint that classifies
  as a pre-base-reordering consonant, and the topographical features exist for scripts that share
  Arabic's own joining-form model, which none of these four scripts use. Both are real HarfBuzz USE
  stages a future USE script outside this four-script family could need.
- **SVG `<text>`/`<tspan>`/`<textPath>` does not resolve script, joining forms, or USE categories at
  all** - Arabic-family and USE-shaped (Devanagari/Bengali/Gujarati/Tamil) text in SVG both still use
  isolated nominal glyphs (in logical, unreordered order for the USE-shaped scripts), unlike HTML text.
  SVG's own bidi reordering is a separate implementation from HTML's, and unifying them for this wasn't
  in scope alongside HTML support landing.
- **GPOS cursive attachment's `RIGHT_TO_LEFT` lookup-flag cascade is validated**, but its main-direction
  (X) formula is hardcoded to HarfBuzz's own RTL buffer-direction branch, since the only caller
  (Arabic-family joining) is always RTL-treated - an LTR-buffer-direction cursive script would need that
  branch ported too, untested since nothing in this codebase can currently reach it.
- **`OpenTypeDescriptor.SupportsFeatureTags`/`RFont.SupportsFontVariantCaps` are not script-tag-aware.**
  They always check tag support under the same no-script-tag fallback chain (`"latn"`/`"DFLT"`)
  `GsubShaper.Shape` itself falls back to for a run with no `TextShapingFeatures.ScriptTag` - never a
  specific script tag - because every caller resolves caps once per `CssBox`/SVG element
  (`DerivedStyle.ActualFontVariantCaps`/`SvgTreeBuilder.ComputeFontContext`), before per-word script-run
  splitting exists to know one. For a font that defines the requested tags only under a specific script's
  own `LangSys` (not under `"latn"`/`"DFLT"`), this can under-report support for script-tagged text
  (currently: Arabic-family joining) even though `Shape` would actually find and apply them via its own
  per-run script preference. Narrow in practice - most fonts author caps/ligature features under
  `DFLT`/`latn` regardless of what else they support - and fixing it properly would mean resolving caps
  per word-run instead of once per box, a larger change than this narrow query justifies on its own.
Originally filed as [issue #176](https://github.com/jhaygood86/PeachPDF/issues/176); the narrowed
remainder above is still tracked under [issue #533](https://github.com/jhaygood86/PeachPDF/issues/533).
GSUB/GDEF/GPOS/per-language behavior is covered by `GdefTableSyntheticTests`,
`GsubMultipleAndContextualSyntheticTests` (including a synthetic nested-contextual-lookup-within-
contextual-lookup case pinning the recursion `GsubShaper.ApplyNestedLookup` performs for lookup types
5/6), `GsubReverseChainSyntheticTests`, `GposTableSyntheticTests`,
`GposCursiveMarkLigatureSyntheticTests`, `GposApplyDispatchSyntheticTests`,
`GposNestedCursiveAndMarkToLigatureSyntheticTests`, `ShapingCharacterizationTests` (real-font
characterization), `ArabicJoiningShaperTests`, `GsubArabicJoiningSyntheticTests`,
`ArabicJoiningCharacterizationTests`, and `ArabicCursiveAttachmentCharacterizationTests` (real-font,
including a real cursive-attachment font); USE behavior is covered by `UseCategoryClassifierTests`,
`UseSyllableScannerTests`, `UseReordererTests` (each testing its own pure logic in isolation, across
all four scripts), and per-script real-font characterization test pairs -
`DevanagariUseShapingCharacterizationTests`/`DevanagariUseCharacterizationTests`,
`BengaliUseShapingCharacterizationTests`/`BengaliUseCharacterizationTests`,
`GujaratiUseShapingCharacterizationTests`/`GujaratiUseCharacterizationTests`,
`TamilUseShapingCharacterizationTests`/`TamilUseCharacterizationTests` (each cross-checked
glyph-for-glyph against real HarfBuzz's own output via `uharfbuzz` for that script's own bundled
font), plus `MixedUseShapedScriptsCharacterizationTests` (a paragraph mixing two different USE-shaped
scripts, and one mixing a USE-shaped script with plain Latin text); the remaining gaps above are
pinned by their own doc comments in those files rather than by a dedicated negative test per gap. See
[Text shaping](docs/html-css-support.md#text-shaping).
