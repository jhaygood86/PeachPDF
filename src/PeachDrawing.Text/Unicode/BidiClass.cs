namespace PeachDrawing.Text.Unicode
{
    /// <summary>
    /// The bidirectional character types of UAX #9: the value of the <c>Bidi_Class</c> property, which every
    /// code point has exactly one of.
    /// </summary>
    public enum BidiClass : byte
    {
        /// <summary>Left-to-right strong.</summary>
        L,

        /// <summary>Right-to-left strong (Hebrew and similar).</summary>
        R,

        /// <summary>Right-to-left Arabic strong.</summary>
        AL,

        /// <summary>European number.</summary>
        EN,

        /// <summary>European number separator.</summary>
        ES,

        /// <summary>European number terminator.</summary>
        ET,

        /// <summary>Arabic number.</summary>
        AN,

        /// <summary>Common number separator.</summary>
        CS,

        /// <summary>Non-spacing mark.</summary>
        NSM,

        /// <summary>Boundary neutral.</summary>
        BN,

        /// <summary>Paragraph separator.</summary>
        B,

        /// <summary>Segment separator.</summary>
        S,

        /// <summary>Whitespace.</summary>
        WS,

        /// <summary>Other neutral.</summary>
        ON,

        /// <summary>Left-to-right embedding.</summary>
        LRE,

        /// <summary>Left-to-right isolate.</summary>
        LRI,

        /// <summary>Left-to-right override.</summary>
        LRO,

        /// <summary>Right-to-left embedding.</summary>
        RLE,

        /// <summary>Right-to-left isolate.</summary>
        RLI,

        /// <summary>Right-to-left override.</summary>
        RLO,

        /// <summary>Pop directional format.</summary>
        PDF,

        /// <summary>Pop directional isolate.</summary>
        PDI,

        /// <summary>First-strong isolate.</summary>
        FSI
    }
}
