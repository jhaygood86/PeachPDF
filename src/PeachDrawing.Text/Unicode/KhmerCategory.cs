namespace PeachDrawing.Text.Unicode
{
    /// <summary>
    /// The category a character has in HarfBuzz's own Khmer shaper: the alphabet its syllable grammar
    /// (<c>KhmerSyllableScanner</c>) is written over and its glyph reorder (<c>KhmerReorderer</c>) acts on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Khmer categories are not a Unicode property. They are worked out from Indic syllabic/positional
    /// category data plus a small table of Khmer-specific overrides, by <see cref="KhmerShaping.Classify"/>
    /// (a direct port of HarfBuzz's own <c>hb-ot-shaper-khmer.cc</c>/<c>gen-indic-table.py</c> category
    /// derivation - see <c>KhmerCategoryClassifier</c>'s own remarks). Despite sharing
    /// member names with a few of <see cref="UseCategory"/>'s own (<see cref="VAbv"/>/<see cref="VBlw"/>/
    /// <see cref="VPre"/>/<see cref="VPst"/>/<see cref="H"/>), this is a genuinely different, older shaping
    /// model (HarfBuzz's pre-USE "Indic" shaper, which Khmer and Myanmar still use rather than the
    /// Devanagari-family USE engine) with its own syllable grammar and reorder rules - not an extension of
    /// <see cref="UseCategory"/>.
    /// </para>
    /// <para>
    /// Only the categories a Khmer-block (U+1780-U+17FF) codepoint, a dotted circle (U+25CC) or a joiner
    /// (ZWJ/ZWNJ) can ever produce are here - see <c>KhmerCategoryClassifier</c>'s own scope remarks.
    /// </para>
    /// </remarks>
    public enum KhmerCategory : byte
    {
        /// <summary>A character that takes no part in Khmer syllable structure: punctuation, another
        /// script's letters, a stray mark this classifier does not assign a dedicated category to. It
        /// forms a syllable on its own and is never reordered.</summary>
        Other,

        /// <summary>An ordinary base consonant.</summary>
        C,

        /// <summary>An independent vowel letter.</summary>
        V,

        /// <summary>The coeng (<c>KHMER SIGN COENG</c>, U+17D2) - HarfBuzz's own comment notes "we use
        /// category H for spec category Coeng". Marks the consonant after it as subjoined/stacked
        /// below (or, for <see cref="Ra"/>, reordered before the base and given the pre-base-form
        /// <c>pref</c> feature).</summary>
        H,

        /// <summary>A zero-width non-joiner.</summary>
        ZWNJ,

        /// <summary>A zero-width joiner.</summary>
        ZWJ,

        /// <summary>A placeholder base: a Khmer digit, or <c>KHMER SIGN PHNAEK MUAN</c> (U+17D9).</summary>
        Placeholder,

        /// <summary>A dotted circle (U+25CC), the standard stand-in glyph for a mark with no base.</summary>
        DottedCircle,

        /// <summary><c>KHMER LETTER RO</c> (U+179A) - the one consonant whose coeng-formed subscript is
        /// reordered before the syllable's base and takes the pre-base-form <c>pref</c> feature, rather
        /// than staying in place with <c>blwf</c> like every other subjoined consonant.</summary>
        Ra,

        /// <summary>A dependent vowel sign written above the base.</summary>
        VAbv,

        /// <summary>A dependent vowel sign written below the base.</summary>
        VBlw,

        /// <summary>A dependent vowel sign written before the base: the one category
        /// <c>KhmerReorderer</c> moves, to the syllable's own start.</summary>
        VPre,

        /// <summary>A dependent vowel sign written after the base.</summary>
        VPst,

        /// <summary>A robat (<c>KHMER SIGN ROBAT</c>, U+17CC) or register shifter (Muusikatoan/Triisap,
        /// U+17C9-U+17CA) - absorbed into the base consonant's own leading unit (HarfBuzz's own
        /// <c>cn</c>), never reordered.</summary>
        Robatic,

        /// <summary>A Khmer sign that groups with the vowel signs ahead of a trailing post-vowel
        /// consonant in the syllable grammar (Nikahit, Bantoc, Toandakhiat, Kakabat..Samyok Sannya,
        /// Viriam - U+17C6, U+17CB, U+17CD-U+17D1) - HarfBuzz's own <c>Xgroup</c>.</summary>
        Xgroup,

        /// <summary>A Khmer sign that trails a syllable's own tail (Reahmuk, Yuukaleapintu, Bathamasat,
        /// Atthacan - U+17C7-U+17C8, U+17D3, U+17DD) - HarfBuzz's own <c>Ygroup</c>.</summary>
        Ygroup,
    }
}
