// Ported from HarfBuzz's src/gen-use-table.py (the `is_*`/`map_to_use` category-derivation
// predicates), retrieved 2026-09-05 from
// https://github.com/harfbuzz/harfbuzz/blob/main/src/gen-use-table.py - "Old MIT" license. This
// script carries no individual per-file header (true of most build-time/tooling scripts under
// src/), so it falls under HarfBuzz's own project-wide notice from COPYING
// (https://github.com/harfbuzz/harfbuzz/blob/main/COPYING) rather than one file's own narrower
// header - see THIRD-PARTY-LICENSES.md for the full notice text and how this fits into PeachPDF's
// own licensing.

using System.Globalization;

namespace PeachPDF.Text.Shaping.Use
{
    /// <summary>
    /// Derives a codepoint's <see cref="UseCategory"/> - a pure function, ported from HarfBuzz's own
    /// category-derivation script (`gen-use-table.py`'s `is_*`/`map_to_use` predicates, retrieved
    /// 2026-09-05 from https://github.com/harfbuzz/harfbuzz/blob/main/src/gen-use-table.py - "Old MIT"
    /// license; this script carries no individual per-file header, so it falls under HarfBuzz's own
    /// project-wide notice from `COPYING` - see THIRD-PARTY-LICENSES.md for the full text), against
    /// <see cref="IndicSyllabicCategoryTable"/>/
    /// <see cref="IndicPositionalCategoryTable"/> and .NET's own built-in General_Category
    /// (<see cref="CharUnicodeInfo.GetUnicodeCategory(int)"/> - no separate UCD-derived
    /// General_Category table is needed, since the BCL already ships one).
    ///
    /// <b>Scope: only the predicate subset a Devanagari (U+0900-U+097F), Bengali (U+0980-U+09FF),
    /// Gujarati (U+0A80-U+0AFF), or Tamil (U+0B80-U+0BFF) codepoint can ever satisfy is ported</b> -
    /// HarfBuzz's real classifier has ~20 `is_*` predicates covering every USE-driven script
    /// (Hieroglyphs, Tibetan's Sakot, Javanese/Sundanese/Batak's medial consonants, scripts with a
    /// static Repha codepoint, etc.); none of those extra categories are reachable from any codepoint
    /// these four blocks' own `Indic_Syllabic_Category` data actually assigns (verified by enumerating
    /// every codepoint in all four blocks against the real UCD data, not assumed - see
    /// `.claude/recent-fixes`'s own entry for the research this was verified against). Of the three
    /// scripts added alongside Devanagari, only Bengali needs a category Devanagari's own reachable set
    /// didn't already carry (<see cref="UseCategory.GB"/> for its Consonant Placeholder, U+0980;
    /// <see cref="UseCategory.FMAbv"/> for its one Syllable Modifier, U+09FE) - Gujarati and Tamil
    /// produce no codepoint needing a category beyond what Devanagari's own classifier already
    /// resolves. A codepoint whose real UISC value would need one of the omitted predicates (e.g. a
    /// Tibetan Sakot, U+0F19 - <see cref="IndicSyllabicCategory"/>'s own <c>InvisibleStacker</c>
    /// without also being U+1A60) falls through to <see cref="UseCategory.O"/> here rather than its
    /// true category - a deliberate, documented v1 simplification (see
    /// <c>.claude/accepted-gaps/no-text-shaping.md</c>): this classifier is only ever invoked for a
    /// word already resolved to one of the four scripts' own OpenType script tags (see
    /// <c>GsubShaper</c>'s own USE-stage), so a genuinely foreign codepoint reaching it at all is
    /// already an edge case, and treating it as an inert "other" character (its own single-glyph
    /// syllable, never reordered) degrades safely rather than guessing wrong.
    ///
    /// Also omitted: the `AJT` (Arabic Joining Type) term in HarfBuzz's own `is_BASE` - none of these
    /// four scripts' own codepoints have an `ArabicShaping.txt`/`DerivedJoiningType.txt` entry at all,
    /// so that clause never fires for anything this classifier is ever asked about; and the `UDI`
    /// (Default_Ignorable_Code_Point) term in `is_CGJ`'s second clause - ZWJ/ZWNJ (the only
    /// default-ignorable codepoints that actually matter for text in these scripts) are each already
    /// given their own explicit `Indic_Syllabic_Category` value (`Joiner`/`Non_Joiner`), so UDI is only
    /// needed to additionally catch a bare Combining Grapheme Joiner (U+034F) or a variation selector
    /// immediately after such text - both real but rare enough in practice to accept as a narrow,
    /// documented gap rather than adding a third UCD-derived (`DerivedCoreProperties.txt`) table for
    /// it.
    /// </summary>
    internal static class UseCategoryClassifier
    {
        public static UseCategory Classify(int codepoint)
        {
            IndicSyllabicCategory uisc = IndicSyllabicCategoryTable.Of(codepoint);
            IndicPositionalCategory uipc = IndicPositionalCategoryTable.Of(codepoint);
            UnicodeCategory ugc = CharUnicodeInfo.GetUnicodeCategory(codepoint);

            // is_BASE (gen-use-table.py), minus its AJT clause (see this class's own remarks). Bindu
            // is listed here (rather than only in is_VOWEL_MOD's own Bindu clause below) because
            // HarfBuzz's real predicate includes it in this Lo-gated list too - unreachable for
            // Devanagari (no Devanagari Bindu codepoint is General_Category=Lo) but real for Bengali's
            // U+09FC BENGALI LETTER VEDIC ANUSVARA, a full letter rather than a combining mark.
            if (uisc is IndicSyllabicCategory.Number or IndicSyllabicCategory.Consonant
                    or IndicSyllabicCategory.ConsonantHeadLetter or IndicSyllabicCategory.ToneLetter
                    or IndicSyllabicCategory.VowelIndependent)
                return UseCategory.B;
            if (ugc == UnicodeCategory.OtherLetter &&
                uisc is IndicSyllabicCategory.Avagraha or IndicSyllabicCategory.Bindu
                    or IndicSyllabicCategory.ConsonantFinal or IndicSyllabicCategory.ConsonantMedial
                    or IndicSyllabicCategory.ConsonantSubjoined or IndicSyllabicCategory.Vowel
                    or IndicSyllabicCategory.VowelDependent)
                return UseCategory.B;

            // is_BASE_OTHER (Consonant_Placeholder) - Bengali's own U+0980 BENGALI ANJI. Real
            // HarfBuzz's own predicate also covers a handful of unrelated punctuation codepoints
            // (U+2015, U+2022, U+25FB-U+25FE) that fall outside all four in-scope scripts' own blocks -
            // left resolving to UseCategory.O here (this classifier's existing catch-all for them, same
            // as before Bengali support existed) rather than widened to cover codepoints no in-scope
            // script actually needs, matching this classifier's own documented scope limitation.
            if (uisc == IndicSyllabicCategory.ConsonantPlaceholder)
                return UseCategory.GB;

            // is_CGJ, minus its UDI clause (see this class's own remarks) - covers ZWJ via its own
            // dedicated Joiner syllabic-category value.
            if (uisc == IndicSyllabicCategory.Joiner)
                return UseCategory.CGJ;

            // is_CONS_MOD - Nukta/Gemination_Mark/Consonant_Killer.
            if (uisc is IndicSyllabicCategory.Nukta or IndicSyllabicCategory.GeminationMark
                    or IndicSyllabicCategory.ConsonantKiller)
                return ResolveModifierPosition(uipc, UseCategory.CMAbv, UseCategory.CMBlw);

            // is_CONS_FINAL_MOD (Syllable_Modifier) - reachable only via Bengali's own Sandhi Mark
            // (U+09FE), whose Indic_Positional_Category is Top, resolving (per HarfBuzz's own
            // use_positions['FM'] = {'Abv': [Top], 'Blw': [Bottom], 'Pst': [Not_Applicable]} mapping)
            // to FMAbv - the only FM-family member this classifier's scope needs (see
            // UseCategory.FMAbv's own remarks on why FMBlw/FMPst are omitted).
            if (uisc == IndicSyllabicCategory.SyllableModifier)
                return UseCategory.FMAbv;

            // is_HALANT - Virama, except U+0DCA (Sinhala's HALANT_OR_VOWEL_MODIFIER split-off,
            // unreachable for a Devanagari codepoint but kept for fidelity to the ported predicate).
            if (uisc == IndicSyllabicCategory.Virama && codepoint != 0x0DCA)
                return UseCategory.H;

            // is_ZWNJ.
            if (uisc == IndicSyllabicCategory.NonJoiner)
                return UseCategory.ZWNJ;

            // is_VOWEL - Pure_Killer, or a non-Lo Vowel/Vowel_Dependent (a dependent vowel sign is
            // always Mn/Mc, never Lo - the Lo case is an independent vowel LETTER, already claimed by
            // is_BASE above).
            if (uisc == IndicSyllabicCategory.PureKiller ||
                (ugc != UnicodeCategory.OtherLetter && uisc is IndicSyllabicCategory.Vowel or IndicSyllabicCategory.VowelDependent))
                return ResolveVowelPosition(uipc);

            // is_VOWEL_MOD - Tone_Mark/Cantillation_Mark/Register_Shifter/Visarga, or a non-Lo Bindu.
            if (uisc is IndicSyllabicCategory.ToneMark or IndicSyllabicCategory.CantillationMark
                    or IndicSyllabicCategory.RegisterShifter or IndicSyllabicCategory.Visarga ||
                (ugc != UnicodeCategory.OtherLetter && uisc == IndicSyllabicCategory.Bindu))
                return ResolveModifierPosition(uipc, UseCategory.VMAbv, UseCategory.VMBlw, UseCategory.VMPst, UseCategory.VMPre);

            // is_OTHER (the catch-all - punctuation, OM, and every other UISC value this classifier
            // doesn't assign a dedicated category to).
            return UseCategory.O;
        }

        /// <summary>Position-suffix resolution for a category whose <c>use_positions</c> mapping
        /// (gen-use-table.py) is <c>{'Abv': [Top], 'Blw': [Bottom, Overstruck]}</c> - covers
        /// Consonant-modifier categories (<see cref="UseCategory.CMAbv"/>/<see cref="UseCategory.CMBlw"/>)
        /// when <paramref name="pst"/>/<paramref name="pre"/> are omitted, and Vowel-modifier
        /// categories' fuller <c>{Abv,Blw,Pst,Pre}</c> mapping when supplied. Falls back to
        /// <paramref name="abv"/> for any positional value the mapping doesn't explicitly list
        /// (matches no real Devanagari codepoint - HarfBuzz's own generator would instead raise on an
        /// unmapped position, but a fallback here is a safer failure mode for hand-fed input than a
        /// thrown exception mid-layout).</summary>
        private static UseCategory ResolveModifierPosition(IndicPositionalCategory uipc,
            UseCategory abv, UseCategory blw, UseCategory? pst = null, UseCategory? pre = null) => uipc switch
        {
            IndicPositionalCategory.Top => abv,
            IndicPositionalCategory.Bottom or IndicPositionalCategory.Overstruck => blw,
            IndicPositionalCategory.Right when pst is { } p => p,
            IndicPositionalCategory.Left when pre is { } p => p,
            _ => abv,
        };

        /// <summary>Position-suffix resolution for <c>is_VOWEL</c>'s fuller
        /// <c>{'Abv': [Top, Top_And_Bottom, Top_And_Bottom_And_Right, Top_And_Right], 'Blw': [Bottom,
        /// Overstruck, Bottom_And_Right], 'Pst': [Right], 'Pre': [Left, Top_And_Left,
        /// Top_And_Left_And_Right, Left_And_Right]}</c> mapping (gen-use-table.py) - none of the
        /// listed compound positions (<c>Top_And_Bottom</c> etc.) occur in the Devanagari block today,
        /// but are still matched here for fidelity to the ported table, since a future Unicode version
        /// could assign one to a new Devanagari-block codepoint.</summary>
        private static UseCategory ResolveVowelPosition(IndicPositionalCategory uipc) => uipc switch
        {
            IndicPositionalCategory.Top or IndicPositionalCategory.TopAndBottom
                or IndicPositionalCategory.TopAndBottomAndRight or IndicPositionalCategory.TopAndRight => UseCategory.VAbv,
            IndicPositionalCategory.Bottom or IndicPositionalCategory.Overstruck
                or IndicPositionalCategory.BottomAndRight => UseCategory.VBlw,
            IndicPositionalCategory.Right => UseCategory.VPst,
            IndicPositionalCategory.Left or IndicPositionalCategory.TopAndLeft
                or IndicPositionalCategory.TopAndLeftAndRight or IndicPositionalCategory.LeftAndRight => UseCategory.VPre,
            _ => UseCategory.VAbv,
        };
    }
}
