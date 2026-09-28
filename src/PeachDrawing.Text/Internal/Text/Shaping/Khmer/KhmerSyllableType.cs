namespace PeachDrawing.Text.Internal.Text.Shaping.Khmer
{
    /// <summary>
    /// The kind of orthographic unit <see cref="KhmerSyllableScanner"/> found - HarfBuzz's own
    /// <c>khmer_syllable_type_t</c> (<c>hb-ot-shaper-khmer-machine.rl</c>), unreduced: Khmer's grammar
    /// only ever produces these three.
    /// </summary>
    internal enum KhmerSyllableType : byte
    {
        /// <summary>A well-formed syllable: a base consonant/independent-vowel/placeholder/dotted-circle,
        /// optionally extended by coeng-joined subjoined consonants, followed by any dependent vowel
        /// signs and trailing Khmer signs. The only type <see cref="KhmerReorderer"/> ever moves a
        /// glyph within (HarfBuzz's own <c>reorder_syllable_khmer</c> also reorders
        /// <see cref="BrokenCluster"/> identically - see that method's own remarks).</summary>
        ConsonantSyllable,

        /// <summary>Coeng/vowel/trailing-sign content with no leading base consonant at all (e.g. text
        /// starting with a bare coeng or dependent vowel) - malformed, but handled gracefully (and still
        /// reordered - see <see cref="ConsonantSyllable"/>'s own remarks) rather than rejected.</summary>
        BrokenCluster,

        /// <summary>A non-Khmer-structural character (punctuation, another script's letter) - its own
        /// single-glyph syllable, never reordered.</summary>
        NonKhmerCluster,
    }
}
