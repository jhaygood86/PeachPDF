namespace PeachPDF.Text.Shaping.Use
{
    /// <summary>
    /// The Universal Shaping Engine's own per-codepoint category (a HarfBuzz concept, not a Unicode
    /// property itself - derived from <see cref="IndicSyllabicCategory"/>/<see cref="IndicPositionalCategory"/>
    /// plus General_Category by <see cref="UseCategoryClassifier"/>), the alphabet
    /// <see cref="UseSyllableScanner"/>'s grammar is written over and <see cref="UseReorderer"/>'s
    /// two reorder passes dispatch on.
    ///
    /// This enum carries only the subset of HarfBuzz's real ~35-member USE category set that a
    /// Devanagari/Bengali/Tamil/Gujarati-scoped classifier can ever produce (see
    /// <see cref="UseCategoryClassifier"/>'s own remarks on why) - the categories a font's own GSUB
    /// `rphf`/basic-feature substitution can also produce dynamically (<see cref="R"/>) are included
    /// even though nothing in <see cref="UseCategoryClassifier"/> assigns them statically. Bengali is
    /// the only one of the four that needs categories beyond Devanagari's own reachable set
    /// (<see cref="GB"/>, <see cref="FMAbv"/>) - Gujarati and Tamil produce no codepoint whose real
    /// Indic_Syllabic_Category value needs a category this enum didn't already carry for Devanagari
    /// (verified against the real Unicode Character Database, not assumed - see this feature's own
    /// recent-fixes entry). Extending this to a script family beyond these four (a script needing
    /// medial consonants - Javanese/Sundanese/Batak, or a static Sakot/Reordering_Killer/
    /// Consonant_With_Stacker codepoint - Tibetan, others) will need new members for that script's own
    /// reachable categories - see <c>.claude/accepted-gaps/no-text-shaping.md</c>.
    /// </summary>
    internal enum UseCategory : byte
    {
        /// <summary>OTHER - a non-participating character (punctuation, an unrelated script's
        /// letter, OM, a stray combining mark UCD doesn't classify for Indic structure). Forms its
        /// own single-glyph syllable and is never reordered.</summary>
        O,

        /// <summary>BASE - a syllable's head: an ordinary consonant, an independent vowel, a digit,
        /// or Avagraha.</summary>
        B,

        /// <summary>Combining Grapheme Joiner / ZWJ - filtered out of the syllable scanner entirely
        /// (see <see cref="UseSyllableScanner"/>), never assigned to an output syllable.</summary>
        CGJ,

        /// <summary>HALANT (virama) - explicitly suppresses the inherent vowel of the preceding
        /// consonant; also the pivot <see cref="UseReorderer"/>'s pre-base-vowel pass resets its
        /// insertion point against.</summary>
        H,

        /// <summary>Zero Width Non-Joiner - blocks conjunct/ligature formation between its
        /// neighbors; kept visible to the scanner only when followed by a combining mark (see
        /// <see cref="UseSyllableScanner"/>'s own remarks).</summary>
        ZWNJ,

        /// <summary>REPHA - never assigned by <see cref="UseCategoryClassifier"/> for Devanagari
        /// (no UCD codepoint statically carries it); assigned dynamically, after a syllable's
        /// leading glyphs run through the font's own `rphf` GSUB feature and one of them is actually
        /// substituted (see <c>GsubShaper</c>'s own USE-stage remarks on why this is a font-data
        /// question, not a Unicode-data one).</summary>
        R,

        /// <summary>Consonant modifier, above-base position (none occur in Devanagari itself - kept
        /// for grammar symmetry with <see cref="CMBlw"/>).</summary>
        CMAbv,

        /// <summary>Consonant modifier, below-base position - Devanagari's Nukta (U+093C).</summary>
        CMBlw,

        /// <summary>Dependent vowel sign (matra), pre-base position - the one category
        /// <see cref="UseReorderer"/> moves backward, to immediately after the nearest preceding
        /// <see cref="H"/> (or the syllable start). Devanagari: U+093F, U+094E.</summary>
        VPre,

        /// <summary>Dependent vowel sign, above-base position - stays in logical order; the font's
        /// own GPOS mark anchoring (not glyph reordering) places it visually above the base.</summary>
        VAbv,

        /// <summary>Dependent vowel sign, below-base position - stays in logical order.</summary>
        VBlw,

        /// <summary>Dependent vowel sign, post-base (right-side) position - stays in logical
        /// order.</summary>
        VPst,

        /// <summary>Vowel modifier (bindu/visarga/tone mark), pre-base position (none occur in bare
        /// Devanagari - kept for grammar symmetry with <see cref="VPre"/>, and for a future script
        /// that does have one).</summary>
        VMPre,

        /// <summary>Vowel modifier, above-base position - Devanagari's Anusvara/Candrabindu family
        /// (U+0900-0902) and the Udatta stress sign (U+0951).</summary>
        VMAbv,

        /// <summary>Vowel modifier, below-base position - Devanagari's Anudatta stress sign
        /// (U+0952).</summary>
        VMBlw,

        /// <summary>Vowel modifier, post-base position - Devanagari's Visarga (U+0903).</summary>
        VMPst,

        /// <summary>BASE_OTHER (Consonant Placeholder) - Bengali's own base-consonant-substitute
        /// construct, U+0980 BENGALI ANJI (the only Bengali/Devanagari/Gujarati/Tamil codepoint with
        /// Indic_Syllabic_Category=Consonant_Placeholder). Real HarfBuzz's own <c>is_BASE_OTHER</c> also
        /// covers a handful of unrelated punctuation marks (em dash, bullet, etc.) that this classifier
        /// leaves as <see cref="O"/> instead (out of scope - see <see cref="UseCategoryClassifier"/>'s
        /// own remarks). Grouped with <see cref="B"/> as an alternate syllable-start token by
        /// <see cref="UseSyllableScanner"/> (HarfBuzz's own grammar: <c>complex_syllable_start = (R |
        /// CS)? (B | GB)</c>) - a GB-led syllable is reordered exactly like a B-led one.</summary>
        GB,

        /// <summary>CONS_FINAL_MOD (Syllable Modifier), above-base position - Bengali's Sandhi Mark
        /// (U+09FE), the only Bengali/Devanagari/Gujarati/Tamil codepoint with
        /// Indic_Syllabic_Category=Syllable_Modifier (Indic_Positional_Category=Top, HarfBuzz's own
        /// <c>use_positions['FM']</c> mapping: <c>{'Abv': [Top], 'Blw': [Bottom], 'Pst':
        /// [Not_Applicable]}</c>). <c>FMBlw</c>/<c>FMPst</c> are omitted - no codepoint reachable by
        /// this classifier ever needs them (see <see cref="UseCategoryClassifier"/>'s own remarks).
        /// Included in <see cref="UseReorderer"/>'s post-base-glyph set (HarfBuzz's own
        /// <c>POST_BASE_FLAGS64</c>), so a reph's forward move still stops before it correctly.</summary>
        FMAbv,
    }
}
