namespace PeachDrawing.Text.Unicode
{
    /// <summary>What a line may do at a position between two characters.</summary>
    public enum LineBreakOpportunity : byte
    {
        /// <summary>The line must not end here.</summary>
        Prohibited = 0,

        /// <summary>The line may end here.</summary>
        Allowed = 1,

        /// <summary>The line must end here: a hard line break, or the end of the text.</summary>
        Mandatory = 2,
    }

    /// <summary>
    /// How CSS <c>word-break</c> tailors the line breaking of the letters inside a word.
    /// </summary>
    public enum WordBreakMode
    {
        /// <summary>The default rules: words break where the algorithm allows, which for Chinese and Japanese is between characters and for most other scripts is at spaces and hyphens.</summary>
        Normal = 0,

        /// <summary>A line may end between any two letters or digits, Hebrew letters included, as well (<c>word-break: break-all</c>).</summary>
        BreakAll = 1,

        /// <summary>No break between two letters or digits, ideographs, Hangul and the letters of Southeast Asian scripts included (<c>word-break: keep-all</c>). Emoji are not letters.</summary>
        KeepAll = 2,
    }

    /// <summary>
    /// How strict the line breaking is about characters that should not start a line (CSS <c>line-break</c>).
    /// </summary>
    public enum LineBreakStrictness
    {
        /// <summary>The library's choice, which is <see cref="Normal"/> (<c>line-break: auto</c>).</summary>
        Auto = 0,

        /// <summary>
        /// The least restrictive rules: a line may also start with a small kana, an iteration mark, a middle dot, a question or
        /// exclamation mark of Japanese text, or an ellipsis, and with a hyphen after an ideograph (<c>line-break: loose</c>). The
        /// characters are tailored whatever the language of the text.
        /// </summary>
        Loose = 1,

        /// <summary>The usual rules: as strict, but the wave dash and the katakana double hyphen may start a line (<c>line-break: normal</c>).</summary>
        Normal = 2,

        /// <summary>The most restrictive rules: a small kana or a wave dash may not start a line, as in the algorithm's own default (<c>line-break: strict</c>).</summary>
        Strict = 3,

        /// <summary>A line may end after every grapheme cluster, whatever the character rules say; only hard line breaks are kept (<c>line-break: anywhere</c>).</summary>
        Anywhere = 4,
    }

    /// <summary>
    /// The tailorings CSS applies to the line breaking algorithm. The default value is <see cref="LineBreakStrictness.Auto"/> and
    /// <see cref="WordBreakMode.Normal"/>, which is <see cref="LineBreakStrictness.Normal"/>: set <see cref="Strictness"/> to
    /// <see cref="LineBreakStrictness.Strict"/> for the algorithm's own default.
    /// </summary>
    public readonly struct LineBreakOptions
    {
        /// <summary>How the letters of a word break, CSS <c>word-break</c>.</summary>
        public WordBreakMode WordBreak { get; init; }

        /// <summary>How strictly the characters that should not start a line are kept off it, CSS <c>line-break</c>.</summary>
        public LineBreakStrictness Strictness { get; init; }
    }
}
