# Porting notes: Khmer shaping in PeachDrawing.Text

Everything in this directory is derived from HarfBuzz and is used under HarfBuzz's own "Old MIT"
license (the permissive, attribution-only license used throughout `src/` in the HarfBuzz
repository - see each file's own header, reproduced verbatim, and `THIRD-PARTY-LICENSES.md` for how
this fits into PeachPDF's own BSD 3-Clause licensing). This mirrors `PeachDrawing.Text/Internal/Text/Shaping/Use/`'s
own convention (the Universal Shaping Engine port) for the identical class of source - HarfBuzz's
own shaping logic - just for a different HarfBuzz shaper.

## What was ported, from where

| | |
|---|---|
| Upstream | HarfBuzz, <https://harfbuzz.github.io> (mirror <https://github.com/harfbuzz/harfbuzz>) |
| Commit | `409c467b8259ad5fcc3fdcc477a1796fec256853` (`main`, retrieved 2026-09-27) |
| Latest tagged release at that date | `14.5.0` |

Only three source files were read for this port, all from `src/` at that commit:

| HarfBuzz file | What it is | C# file(s) it fed |
|---|---|---|
| `hb-ot-shaper-khmer.cc` | The Khmer shaper itself: feature list, category derivation entry point, `reorder_consonant_syllable`/`reorder_syllable_khmer`, `decompose_khmer`/`compose_khmer` | `KhmerReorderer.cs` (the reorder function only - see "Not ported" below) |
| `hb-ot-shaper-khmer-machine.rl` | The Ragel grammar for Khmer's own syllable machine (`find_syllables_khmer`) | `KhmerSyllableScanner.cs` (hand-written scanner, no Ragel toolchain in this repo - see `Use/UseSyllableScanner.cs`'s own identical precedent) |
| `gen-indic-table.py` | The build-time generator that derives HarfBuzz's shared Indic-family category table (`category_map`/`category_overrides`/`position_map`, and the Khmer/Myanmar-specific matra-to-position-category rewrite) from the raw Unicode `IndicSyllabicCategory.txt`/`IndicPositionalCategory.txt` | `KhmerCategoryClassifier.cs` |

`hb-ot-shaper-indic.hh`/`hb-ot-shaper-indic-table.cc` (the *compiled output* of `gen-indic-table.py`,
shared with HarfBuzz's Myanmar and legacy Indic shapers) were read only to confirm
`gen-indic-table.py`'s own derivation rules against real generated data - nothing was copied from
either file, since `KhmerCategoryClassifier.cs` re-derives the same table from this repo's own
already-checked-in UCD data (`assets/unicode/IndicSyllabicCategory.txt`/`IndicPositionalCategory.txt`)
rather than embedding a generated table, exactly as `Use/UseCategoryClassifier.cs` already does for
the Universal Shaping Engine's own category data.

## Not ported

- **`decompose_khmer`/`compose_khmer`** (Unicode normalization for five Khmer vowel signs with no
  canonical decomposition) - this codebase has no general shaping-time normalization pass at all
  (Arabic-family and USE shaping don't decompose/recompose either), so adding one for Khmer alone
  was out of scope. See `.claude/accepted-gaps/no-text-shaping.md`'s own Khmer remaining-gaps bullet.
- **`cfar` feature request and its mask assignment** - a narrow, single-font-family disambiguation
  HarfBuzz's own comment describes as needed only for some Microsoft Khmer fonts; no bundled test
  font defines it. See the same accepted-gap entry.
- **Per-syllable OpenType feature masking** (HarfBuzz's own `khmer_shape_plan_t::mask_array`,
  assigned per-glyph inside `reorder_consonant_syllable`) - `GsubShaper`'s own Khmer stage applies
  `locl`/`ccmp`/`pref`/`blwf`/`abvf`/`pstf` globally instead, the same documented v1 simplification
  already established for the Universal Shaping Engine's own basic features (see `GsubShaper.ApplyUseShaping`'s
  own remarks) - a font's coverage/context tables only match the sequences they're authored for, so
  this produces the same result in practice.
- **Dotted-circle insertion** (`hb_syllabic_insert_dotted_circles`, from `hb-ot-shaper-syllabic.cc`) -
  not ported for Khmer, matching this port's own pre-existing scope for the Universal Shaping Engine
  (neither inserts one for a broken cluster).
- **`hb-ot-shaper-khmer-machine.hh`'s compiled Ragel tables** - `KhmerSyllableScanner.cs` is a
  hand-written recursive-descent scanner over the *grammar* (the `.rl` file's own regular
  expression, before Ragel compiles it into a state machine), not a port of the generated C tables
  themselves - exactly `Use/UseSyllableScanner.cs`'s own approach.

## Verification

Every category/syllable/reorder decision this port makes was cross-checked against real HarfBuzz
(`uharfbuzz` 14.4.0, a real "Noto Sans Khmer" font - see `assets/fonts/NotoSansKhmerSubset.LICENSE.txt`)
during development - not merely reverse-engineered from this implementation - covering a bare
consonant, a coeng+RO pair (`pref`, reordered before the base), a coeng+other-consonant pair (`blwf`,
in place), one dependent vowel sign per reorder-relevant position, a coeng+RO pair *and* a pre-base
vowel sign together (proving the single-loop reorder's own ordering, not two independent passes), a
Robat sign, and two adjacent bare consonants (proving syllable segmentation). See
`KhmerUseShapingCharacterizationTests` for the exact glyph IDs/orders this produced.
