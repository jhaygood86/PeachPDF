namespace PeachDrawing.Text.Unicode
{
    /// <summary>
    /// What <see cref="Bidi.Analyze"/> found out about one paragraph: an embedding level for every UTF-16 code
    /// unit of the text, and the paragraph's own level.
    /// </summary>
    /// <remarks>
    /// A low surrogate always repeats the level of the high surrogate before it, so a character outside the
    /// Basic Multilingual Plane never splits a level run. The paragraph level seeds the line-level resets of
    /// UAX #9 rule L1, and is how a caller that asked for <see cref="BaseDirection.Auto"/> learns which
    /// direction the text turned out to have.
    /// </remarks>
    public sealed class BidiAnalysis
    {
        internal BidiAnalysis(byte[] levels, byte paragraphLevel)
        {
            Levels = levels;
            ParagraphLevel = paragraphLevel;
        }

        /// <summary>The resolved embedding level of each UTF-16 code unit of the analysed text.</summary>
        /// <remarks>
        /// This is the analysis's own array, not a copy, so that laying out a long paragraph does not copy it again for
        /// every line. Treat it as read-only.
        /// </remarks>
        public byte[] Levels { get; }

        /// <summary>The paragraph's embedding level: 0 for a left-to-right paragraph, 1 for a right-to-left one.</summary>
        public byte ParagraphLevel { get; }

        /// <summary>Whether the paragraph as a whole reads right to left.</summary>
        public bool IsParagraphRtl => (ParagraphLevel & 1) == 1;
    }
}
