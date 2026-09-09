namespace PeachPDF.Text
{
    /// <summary>
    /// Unicode's <c>Default_Ignorable_Code_Point</c> property (UAX #44 / <c>DerivedCoreProperties.txt</c>):
    /// codepoints that carry meaning for text processing but have <b>no visible rendering of their own</b> -
    /// variation selectors, ZWJ/ZWNJ, the bidi controls, word joiner, the Hangul fillers, the language tag
    /// characters. A conforming renderer must never draw a missing-glyph box for one of these: the whole
    /// point of the property is that a font is <i>expected</i> not to have a glyph for them, so falling
    /// through to <c>.notdef</c> turns an invisible character into visible garbage.
    ///
    /// Small and static enough (17 ranges) to answer from an inline comparison chain rather than the
    /// Brotli-compressed generated-table pattern <see cref="VerticalOrientationTable"/> and
    /// <see cref="Bidi.BidiClassTable"/> use for their far larger UAX properties - there is no per-version
    /// churn here worth a generator, and this sits on the per-codepoint shaping path.
    /// </summary>
    /// <remarks>
    /// The ranges deliberately include the reserved-for-future-ignorable blocks the UCD itself lists
    /// (U+2065, U+FFF0..U+FFF8, and the unassigned remainder of U+E0000..U+E0FFF) - they are
    /// <c>Default_Ignorable_Code_Point</c> today, so a document containing one should render nothing
    /// rather than a box, exactly as if it were assigned.
    /// </remarks>
    internal static class UnicodeDefaultIgnorables
    {
        /// <summary>
        /// Whether <paramref name="codepoint"/> has Unicode's <c>Default_Ignorable_Code_Point</c> property.
        /// </summary>
        public static bool IsDefaultIgnorable(int codepoint) => codepoint switch
        {
            0x00AD => true,                          // SOFT HYPHEN
            0x034F => true,                          // COMBINING GRAPHEME JOINER
            0x061C => true,                          // ARABIC LETTER MARK
            >= 0x115F and <= 0x1160 => true,         // HANGUL CHOSEONG/JUNGSEONG FILLER
            >= 0x17B4 and <= 0x17B5 => true,         // KHMER VOWEL INHERENT AQ/AA
            >= 0x180B and <= 0x180F => true,         // MONGOLIAN FREE VARIATION SELECTORs + VOWEL SEPARATOR
            >= 0x200B and <= 0x200F => true,         // ZWSP, ZWNJ, ZWJ, LRM, RLM
            >= 0x202A and <= 0x202E => true,         // LRE, RLE, PDF, LRO, RLO
            >= 0x2060 and <= 0x206F => true,         // WORD JOINER, invisible operators, the bidi isolates, deprecated format chars
            0x3164 => true,                          // HANGUL FILLER
            >= 0xFE00 and <= 0xFE0F => true,         // VARIATION SELECTOR-1..16
            0xFEFF => true,                          // ZERO WIDTH NO-BREAK SPACE (byte order mark)
            0xFFA0 => true,                          // HALFWIDTH HANGUL FILLER
            >= 0xFFF0 and <= 0xFFF8 => true,         // reserved
            >= 0x1BCA0 and <= 0x1BCA3 => true,       // SHORTHAND FORMAT controls
            >= 0x1D173 and <= 0x1D17A => true,       // MUSICAL SYMBOL BEGIN/END BEAM..PHRASE
            >= 0xE0000 and <= 0xE0FFF => true,       // LANGUAGE TAG, the TAG characters, VARIATION SELECTOR-17..256, reserved
            _ => false
        };
    }
}
