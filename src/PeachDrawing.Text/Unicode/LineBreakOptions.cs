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
        /// The least restrictive rules: a line may also start with a small kana, an iteration mark or a hyphen after an ideograph, and may
        /// end between two ellipses (<c>line-break: loose</c>), whatever the language of the text. Where
        /// <see cref="LineBreakOptions.Language"/> is Chinese or Japanese a line may also start with a middle dot, a colon, a semicolon or a
        /// question or exclamation mark of CJK text, end before a suffix such as a fullwidth percent sign and after a prefix such as a
        /// fullwidth yen sign, and start with the wave dash and the katakana double hyphen as in <see cref="Normal"/>.
        /// </summary>
        Loose = 1,

        /// <summary>
        /// The usual rules: as strict, but the wave dash and the katakana double hyphen may start a line when
        /// <see cref="LineBreakOptions.Language"/> is Chinese or Japanese (<c>line-break: normal</c>).
        /// </summary>
        Normal = 2,

        /// <summary>The most restrictive rules: a small kana or a wave dash may not start a line, as in the algorithm's own default (<c>line-break: strict</c>).</summary>
        Strict = 3,

        /// <summary>A line may end after every grapheme cluster, whatever the character rules say; only hard line breaks are kept (<c>line-break: anywhere</c>).</summary>
        Anywhere = 4,
    }

    /// <summary>
    /// How the characters of the Complex_Context line breaking class (<c>SA</c>) are resolved. UAX #14 rule LB1 leaves this to criteria
    /// outside the algorithm, and for the scripts written without spaces between words the criterion that matters is a word list.
    /// </summary>
    public enum ComplexContextBreaking
    {
        /// <summary>
        /// A line may end between the words of Thai, Lao, Khmer and Burmese text, found in a word list the library carries and never
        /// inside a syllable, as browsers do. The lists load the first time text of the script is analysed. Text in other
        /// Complex_Context scripts, such as Tai Tham, is resolved by <see cref="GeneralCategory"/>, as is text in one of these four when
        /// its word list cannot be read.
        /// </summary>
        Dictionary = 0,

        /// <summary>
        /// The fallback rule LB1 itself gives: a nonspacing or spacing mark is a combining mark and every other Complex_Context
        /// character a letter, so a run of one script has no opportunity inside it. This is what the Unicode conformance file for the
        /// algorithm expects, and what a caller that segments the text with its own dictionary wants.
        /// </summary>
        GeneralCategory = 1,
    }

    /// <summary>
    /// The tailorings CSS applies to the line breaking algorithm. The default value is <see cref="LineBreakStrictness.Auto"/> and
    /// <see cref="WordBreakMode.Normal"/>, which is <see cref="LineBreakStrictness.Normal"/>, with <see cref="ComplexContextBreaking.Dictionary"/>:
    /// set <see cref="Strictness"/> to <see cref="LineBreakStrictness.Strict"/> and <see cref="ComplexContext"/> to
    /// <see cref="ComplexContextBreaking.GeneralCategory"/> for the algorithm's own default.
    /// </summary>
    public readonly struct LineBreakOptions
    {
        /// <summary>How the letters of a word break, CSS <c>word-break</c>.</summary>
        public WordBreakMode WordBreak { get; init; }

        /// <summary>How strictly the characters that should not start a line are kept off it, CSS <c>line-break</c>.</summary>
        public LineBreakStrictness Strictness { get; init; }

        /// <summary>
        /// The language of the text as a BCP 47 tag such as <c>ja</c> or <c>zh-Hant-TW</c>, or <see langword="null"/> when it is not
        /// known. Only the primary language subtag is read, without regard to case. CSS Text 3 allows some of the breaks of
        /// <see cref="LineBreakStrictness.Normal"/> and <see cref="LineBreakStrictness.Loose"/> only where the writing system is
        /// Chinese or Japanese: with any other language, or none, those breaks stay forbidden.
        /// </summary>
        public string? Language { get; init; }

        /// <summary>
        /// How text in the Complex_Context class (Thai, Lao, Khmer and Burmese, which write no spaces between words) is broken. The
        /// default finds the words in a dictionary and allows a line to end between them; the language of the text is not read, the
        /// script is. <see cref="ComplexContextBreaking.GeneralCategory"/> gives no opportunity inside a run of one script.
        /// <see cref="WordBreak"/> and <see cref="Strictness"/> apply on top of either choice.
        /// </summary>
        public ComplexContextBreaking ComplexContext { get; init; }
    }
}
