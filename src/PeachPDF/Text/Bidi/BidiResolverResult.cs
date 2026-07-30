namespace PeachPDF.Text.Bidi
{
    /// <summary>
    /// The result of <see cref="BidiResolver.Resolve"/>: one resolved embedding level per UTF-16 code unit
    /// of the input text (a low surrogate always repeats its high surrogate's level - astral characters
    /// never split a level run), plus the resolved paragraph embedding level (odd = RTL) used to seed
    /// L1's paragraph-level resets and available to a caller that requested <see cref="BidiParagraphDirection.Auto"/>.
    /// </summary>
    internal sealed class BidiResolverResult(byte[] levels, byte paragraphLevel)
    {
        public byte[] Levels { get; } = levels;
        public byte ParagraphLevel { get; } = paragraphLevel;
        public bool IsParagraphRtl => (ParagraphLevel & 1) == 1;

        /// <summary>
        /// The per-character ORIGINAL (pre-resolution) <see cref="BidiClass"/> array
        /// <see cref="BidiResolver.Resolve"/> computed internally - exposed so a caller can later run
        /// <see cref="BidiResolver.ApplyLineTrailingWhitespaceReset"/> (UAX #9 L1 clause 4) once line
        /// boundaries are known, without re-deriving codepoint classes for the whole paragraph again.
        /// </summary>
        public required BidiClass[] OriginalTypesForLineBreaking { get; init; }
    }
}
