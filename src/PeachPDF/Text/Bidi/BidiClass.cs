namespace PeachPDF.Text.Bidi
{
    /// <summary>
    /// The 23 bidirectional character types defined by UAX #9 (Unicode Bidirectional Algorithm),
    /// https://www.unicode.org/reports/tr9/#Bidirectional_Character_Types. Every Unicode codepoint's
    /// <c>Bidi_Class</c> property value resolves to exactly one of these - see <see cref="BidiClassTable"/>.
    /// </summary>
    internal enum BidiClass : byte
    {
        // Strong types
        L,
        R,
        AL,

        // Weak types
        EN,
        ES,
        ET,
        AN,
        CS,
        NSM,
        BN,

        // Neutral types
        B,
        S,
        WS,
        ON,

        // Explicit formatting types
        LRE,
        LRI,
        LRO,
        RLE,
        RLI,
        RLO,
        PDF,
        PDI,
        FSI
    }
}
