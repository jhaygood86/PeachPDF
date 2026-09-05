namespace PeachPDF.Text.Shaping.Use
{
    /// <summary>
    /// The kind of orthographic unit <see cref="UseSyllableScanner"/> found - a reduced form of
    /// HarfBuzz's own <c>use_syllable_type_t</c> (which has 9 members; the others -
    /// <c>virama_terminated_cluster</c>, <c>sakot_terminated_cluster</c>,
    /// <c>number_joiner_terminated_cluster</c>, <c>numeral_cluster</c>, <c>hieroglyph_cluster</c> -
    /// each structurally require a <see cref="UseCategory"/> member this classifier never produces
    /// for Devanagari, so they can never actually be scanned - see
    /// <see cref="UseSyllableScanner"/>'s own remarks). Only <see cref="StandardCluster"/> is
    /// reordered by <see cref="UseReorderer"/> in practice for well-formed Devanagari text -
    /// <see cref="BrokenCluster"/>/<see cref="SymbolCluster"/> can still carry a pre-base vowel
    /// (rare, malformed input) and are reordered too, matching HarfBuzz's own eligible-type set.
    /// </summary>
    internal enum UseSyllableType : byte
    {
        /// <summary>A well-formed syllable: a base consonant/independent-vowel/digit, optionally
        /// extended by one or more halant-joined conjunct members, followed by any dependent
        /// vowel signs and vowel modifiers.</summary>
        StandardCluster,

        /// <summary>Dependent-vowel/modifier/halant content with no leading base at all (e.g. a word
        /// starting with a bare matra or bindu) - malformed, but handled gracefully rather than
        /// rejected.</summary>
        BrokenCluster,

        /// <summary>A non-Indic-structural character (punctuation, OM, an unrelated symbol),
        /// optionally followed by attaching modifier content.</summary>
        SymbolCluster,

        /// <summary>A single glyph that doesn't extend a cluster at all - a stray combining-grapheme
        /// joiner (absorbed into the nearest preceding syllable when one directly precedes) or any
        /// other unclassified single glyph.</summary>
        NonCluster,
    }
}
