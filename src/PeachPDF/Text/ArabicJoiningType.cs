namespace PeachPDF.Text
{
    /// <summary>
    /// The six values of Unicode's <c>Joining_Type</c> property
    /// (<see href="https://www.unicode.org/reports/tr44/">UAX #44</see>), which classifies how a
    /// codepoint participates in cursive joining behavior (Arabic, Syriac, N'Ko, Mandaic, Mongolian,
    /// and several other joining scripts share this one property). Every codepoint resolves to exactly
    /// one of these - see <see cref="ArabicShapingTable"/>. Single-letter names match the UCD's own
    /// abbreviations (<c>ArabicShaping.txt</c>/<c>DerivedJoiningType.txt</c>) rather than spelling out
    /// <c>Non_Joining</c> etc., since callers (the joining-form state machine) work directly off these
    /// short codes the same way the UCD source data and reference implementations do.
    /// </summary>
    internal enum ArabicJoiningType : byte
    {
        /// <summary>Non_Joining (U) - does not join with an adjacent character on either side (most
        /// codepoints, including all non-joining-script text, default here).</summary>
        U,

        /// <summary>Right_Joining (R) - joins with a preceding character, but not a following one.</summary>
        R,

        /// <summary>Dual_Joining (D) - joins with both a preceding and a following character (most
        /// Arabic letters).</summary>
        D,

        /// <summary>Join_Causing (C) - like <see cref="D"/> for the purpose of causing adjacent
        /// characters to take a joining form, but has no visible joining form of its own (Tatweel, ZWJ).</summary>
        C,

        /// <summary>Left_Joining (L) - joins with a following character, but not a preceding one (rare -
        /// used only by a handful of Syriac/Manichaean codepoints).</summary>
        L,

        /// <summary>Transparent (T) - has no effect on the joining behavior of surrounding characters;
        /// skipped over when determining whether two letters are adjacent for joining purposes (most
        /// combining marks - diacritics between two joined letters must not break the join).</summary>
        T
    }
}
