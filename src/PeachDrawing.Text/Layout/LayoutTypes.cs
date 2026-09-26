using PeachDrawing.Text.Shaping;
using PeachDrawing.Text.Unicode;
using System;

namespace PeachDrawing.Text.Layout
{
    /// <summary>
    /// Which side of a boundary between two lines a position belongs to, when the text offset is both the end of one line and the
    /// start of the next.
    /// </summary>
    public enum TextAffinity
    {
        /// <summary>The position belongs to the text before the offset: the end of the earlier line.</summary>
        Upstream = 0,

        /// <summary>The position belongs to the text after the offset: the start of the later line.</summary>
        Downstream = 1,
    }

    /// <summary>A place in the text of a paragraph.</summary>
    /// <param name="Index">The UTF-16 offset of the boundary between two characters, from 0 to the length of the text.</param>
    /// <param name="Affinity">Which line the position is on when <paramref name="Index"/> is where a soft line break falls.</param>
    public readonly record struct TextPosition(int Index, TextAffinity Affinity = TextAffinity.Downstream);

    /// <summary>A stretch of the text of a paragraph, by UTF-16 offsets.</summary>
    /// <param name="Start">The offset of the first character.</param>
    /// <param name="End">The offset just past the last character.</param>
    public readonly record struct TextRange(int Start, int End)
    {
        /// <summary>The number of UTF-16 code units in the range.</summary>
        public int Length => End - Start;

        /// <summary>Whether the range holds no text.</summary>
        public bool IsEmpty => End <= Start;

        /// <summary>Whether <paramref name="index"/> is at or after the start and before the end.</summary>
        /// <param name="index">The offset to test.</param>
        public bool Contains(int index) => index >= Start && index < End;
    }

    /// <summary>Where the text of a line is aligned within the available width (CSS <c>text-align</c>).</summary>
    public enum TextAlign
    {
        /// <summary>The side the paragraph's text starts on: left for a left-to-right paragraph, right for a right-to-left one.</summary>
        Start = 0,

        /// <summary>The side the paragraph's text ends on.</summary>
        End = 1,

        /// <summary>The left edge.</summary>
        Left = 2,

        /// <summary>The right edge.</summary>
        Right = 3,

        /// <summary>The middle.</summary>
        Center = 4,

        /// <summary>
        /// Both edges: the room a line has left is shared out to fill it, at the spaces and, as <see cref="ParagraphStyle.TextJustify"/> says, between characters.
        /// The last line of the paragraph and a line that ends in a forced break are aligned as <see cref="ParagraphStyle.AlignLast"/> says (the start, by default),
        /// and a line with nowhere to add room is not changed.
        /// </summary>
        Justify = 5,
    }

    /// <summary>Whether and how words are broken with a hyphen at the end of a line (CSS <c>hyphens</c>).</summary>
    public enum Hyphens
    {
        /// <summary>A line breaks inside a word only where the text has a soft hyphen (U+00AD), and ends with a hyphen there.</summary>
        Manual = 0,

        /// <summary>Soft hyphens are not break opportunities, and no word is hyphenated.</summary>
        None = 1,

        /// <summary>
        /// Soft hyphens are used, and words are also hyphenated where the patterns of the language allow (see <see cref="Hyphenator"/>). The language is the
        /// <see cref="ShapeSettings.Language"/> of the run the word is in, or <see cref="LineBreakOptions.Language"/> of the paragraph's line breaking; a
        /// word with neither is not hyphenated.
        /// </summary>
        Auto = 2,
    }

    /// <summary>The smallest a hyphenated word and the pieces of it may be (CSS <c>hyphenate-limit-chars</c>).</summary>
    /// <param name="WordLength">The fewest characters a word must have to be hyphenated, or <see langword="null"/> for 5.</param>
    /// <param name="BeforeBreak">The fewest characters that must stay before the hyphen, or <see langword="null"/> for 2.</param>
    /// <param name="AfterBreak">The fewest characters that must go after the hyphen, or <see langword="null"/> for 2.</param>
    public readonly record struct HyphenateLimitChars(int? WordLength = null, int? BeforeBreak = null, int? AfterBreak = null);

    /// <summary>Whether the last full line of a paragraph may be hyphenated (CSS <c>hyphenate-limit-last</c>).</summary>
    public enum HyphenateLimitLast
    {
        /// <summary>There is no restriction.</summary>
        None = 0,

        /// <summary>
        /// The last full line, the one that ends where the rest of the text before the paragraph's end or the next forced break fits on a line of its own,
        /// does not end with a hyphenation.
        /// </summary>
        Always = 1,
    }

    /// <summary>Where a justified line gets its extra room (CSS <c>text-justify</c>).</summary>
    public enum TextJustify
    {
        /// <summary>
        /// The room is shared between the spaces of the line and the boundaries next to a letter of a script written without spaces (Han, Hiragana, Katakana,
        /// Bopomofo and Yi), so a line of Chinese or Japanese is justified too. A line with neither is left as it is.
        /// </summary>
        Auto = 0,

        /// <summary>Lines are not justified: <see cref="TextAlign.Justify"/> aligns them as <see cref="TextAlign.Start"/> does.</summary>
        None = 1,

        /// <summary>The room is shared between the spaces of the line only.</summary>
        InterWord = 2,

        /// <summary>
        /// The room is shared between every pair of adjacent characters, spaces included, except where the joined letters of a cursive script would be pulled
        /// apart. A line of any script is justified, if it has two characters.
        /// </summary>
        InterCharacter = 3,
    }

    /// <summary>What may be done to a word that is too long for a line on its own (CSS <c>overflow-wrap</c>).</summary>
    public enum OverflowWrap
    {
        /// <summary>The word overflows the line.</summary>
        Normal = 0,

        /// <summary>The word is broken between characters, but only when it does not fit a line by itself.</summary>
        BreakWord = 1,

        /// <summary>
        /// The word is broken between characters when it does not fit a line by itself, and the possibility of doing so also counts when
        /// the narrowest the paragraph can be is worked out.
        /// </summary>
        Anywhere = 2,
    }

    /// <summary>Why a line ends where it does.</summary>
    public enum LineEnd
    {
        /// <summary>At a break opportunity the line breaking algorithm allows, because the next word did not fit.</summary>
        Soft = 0,

        /// <summary>At a forced break: a newline in the text.</summary>
        Forced = 1,

        /// <summary>In the middle of a word, because the word is wider than a line and the paragraph allows breaking it.</summary>
        Emergency = 2,

        /// <summary>It is the last line, ending at the end of the text.</summary>
        Last = 3,

        /// <summary>
        /// Inside a word, at a soft hyphen or a place automatic hyphenation chose, and the line ends with a hyphen: a generated run (see
        /// <see cref="PlacedRun.IsGenerated"/>) that is not part of the text.
        /// </summary>
        Hyphenated = 4,
    }

    /// <summary>The narrowest and widest a paragraph can be laid out.</summary>
    /// <param name="MinContent">The width of the widest unbreakable piece: laying the paragraph out narrower makes something overflow.</param>
    /// <param name="MaxContent">The width of the widest line when nothing but forced breaks end a line: laying it out wider changes nothing.</param>
    public readonly record struct ContentWidths(double MinContent, double MaxContent);

    /// <summary>The look of a run of text in a paragraph.</summary>
    /// <param name="Typeface">The face to set the text in.</param>
    /// <param name="Size">The size of the em, in the units the layout is measured in.</param>
    /// <param name="Shape">What to ask of the shaper, or <see langword="null"/> for the defaults. Its script tag, joining forms and direction are filled in from the text.</param>
    /// <param name="LetterSpacing">Extra distance after every character, in layout units (CSS <c>letter-spacing</c>); zero for none. It is added once for each cluster of glyphs that stand for one character (a base and its marks, or a ligature), after the last of them.</param>
    /// <param name="WordSpacing">Extra distance after every space and no-break space, in layout units (CSS <c>word-spacing</c>); zero for none. Both spacings must be finite.</param>
    /// <param name="Fallback">
    /// What to ask for a typeface when <paramref name="Typeface"/> has no glyph for a character (see <see cref="FontSet.CreateFallback"/>), or
    /// <see langword="null"/> to draw the missing-glyph shape. It is asked once for each user-perceived character the face cannot draw, with
    /// that character's first code point, and answers <see langword="null"/> when it has nothing better; the character and the marks that follow
    /// it are then set in the typeface it returns, at the run's size. Runs are equal only when their fallbacks are the same delegate, and text in
    /// runs that differ is shaped and broken separately, so a caller that sets a fallback on many runs should make it once and share it.
    /// </param>
    public readonly record struct RunStyle(Typeface Typeface, double Size, ShapeSettings? Shape = null, Func<System.Text.Rune, Typeface?>? Fallback = null,
        double LetterSpacing = 0, double WordSpacing = 0);

    /// <summary>
    /// How far the start of a line is moved in from the edge of the paragraph (CSS <c>text-indent</c>), and which lines it moves. The distance is a length
    /// in layout units, so a caller that has a percentage works it out against its own width.
    /// </summary>
    /// <param name="Length">
    /// The distance from the start edge (the left for a left-to-right paragraph, the right for a right-to-left one); it may be negative, which moves the
    /// text out of the paragraph. It must be finite.
    /// </param>
    /// <param name="Hanging">
    /// Whether the lines it applies to are swapped for the ones it does not apply to: the indent is on every line except the first (and, with
    /// <paramref name="EachLine"/>, except the lines that follow a forced break).
    /// </param>
    /// <param name="EachLine">Whether the lines that follow a forced break get the indent too, and not only the first line of the paragraph.</param>
    public readonly record struct TextIndent(double Length, bool Hanging = false, bool EachLine = false);

    /// <summary>
    /// The distance between tab stops (CSS <c>tab-size</c>): a multiple of the width of a space in the face the tab is set in, or a length. The default value,
    /// what <c>default(TabSize)</c> is, is 8 spaces.
    /// </summary>
    public readonly record struct TabSize
    {
        private const byte SpacesKind = 1;
        private const byte LengthKind = 2;
        private readonly byte _kind;
        private readonly double _value;

        private TabSize(byte kind, double value)
        {
            _kind = kind;
            _value = value;
        }

        /// <summary>
        /// A tab stop for every <paramref name="count"/> spaces, each as wide as a space is in the face of the tab, including the letter and word spacing
        /// that go with it.
        /// </summary>
        /// <param name="count">The number of spaces, zero or more; zero makes a tab take no room.</param>
        /// <returns>The size.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative or not finite.</exception>
        public static TabSize FromSpaces(double count)
        {
            if (!double.IsFinite(count) || count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), count, "The number of spaces must be zero or more, and finite.");
            }

            return count == 8 ? default : new TabSize(SpacesKind, count);
        }

        /// <summary>A tab stop for every <paramref name="length"/> layout units.</summary>
        /// <param name="length">The distance, zero or more; zero makes a tab take no room.</param>
        /// <returns>The size.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative or not finite.</exception>
        public static TabSize FromLength(double length)
        {
            if (!double.IsFinite(length) || length < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length), length, "The length must be zero or more, and finite.");
            }

            return new TabSize(LengthKind, length);
        }

        /// <summary>Whether the distance is a length, and not a number of spaces.</summary>
        public bool IsLength => _kind == LengthKind;

        /// <summary>The number of spaces, or the length in layout units when <see cref="IsLength"/> is set.</summary>
        public double Value => _kind == 0 ? 8 : _value;
    }

    /// <summary>How a paragraph as a whole is set.</summary>
    public readonly record struct ParagraphStyle
    {
        /// <summary>
        /// Creates the default style: the direction detected from the text, aligned at the start, ordinary line breaking, the line height the faces
        /// ask for. <c>default(ParagraphStyle)</c> differs only in the direction, which is left to right.
        /// </summary>
        public ParagraphStyle()
        {
            Direction = BaseDirection.Auto;
        }

        /// <summary>The direction the paragraph starts in.</summary>
        public BaseDirection Direction { get; init; }

        /// <summary>Where lines are aligned.</summary>
        public TextAlign Align { get; init; }

        /// <summary>
        /// How the last line of the paragraph, and a line that ends in a forced break, are aligned (CSS <c>text-align-last</c>); <see langword="null"/> is
        /// the same as <see cref="Align"/>, except that justified text starts them at the start.
        /// </summary>
        public TextAlign? AlignLast { get; init; }

        /// <summary>
        /// How far lines start from the edge (CSS <c>text-indent</c>). The indent takes room from the line: it breaks earlier, and the line is aligned in what
        /// is left.
        /// </summary>
        public TextIndent TextIndent { get; init; }

        /// <summary>
        /// The distance between the tab stops a tab character (U+0009) advances the pen to (CSS <c>tab-size</c>). Stops are measured along the line from the
        /// start edge of the paragraph, in the order the text is written, so a tab after right-to-left text in a left-to-right line is placed as if that
        /// text were where it is in memory. A tab at the end of a line hangs, like any space there. Justification widens spaces only, so text after a tab in a justified line
        /// moves off its stop by what the spaces before it gained.
        /// </summary>
        public TabSize TabSize { get; init; }

        /// <summary>Where a justified line gets its room from (CSS <c>text-justify</c>); it matters only where the alignment is <see cref="TextAlign.Justify"/>.</summary>
        public TextJustify TextJustify { get; init; }

        /// <summary>Whether words are broken with a hyphen (CSS <c>hyphens</c>).</summary>
        public Hyphens Hyphens { get; init; }

        /// <summary>
        /// The string a hyphenated line ends with (CSS <c>hyphenate-character</c>), from 1 to 32 UTF-16 units, or <see langword="null"/> for the hyphen (U+2010)
        /// where the face has it and the hyphen-minus otherwise.
        /// </summary>
        public string? HyphenateCharacter { get; init; }

        /// <summary>
        /// The smallest a hyphenated word and its pieces may be (CSS <c>hyphenate-limit-chars</c>). Counts are of UTF-16 units and must not be negative. The patterns of a
        /// language have minimums of their own, which these cannot go below, and a word of more than 128 UTF-16 units is not hyphenated.
        /// </summary>
        public HyphenateLimitChars HyphenateLimitChars { get; init; }

        /// <summary>The most lines in a row that may end with a hyphenation (CSS <c>hyphenate-limit-lines</c>), or <see langword="null"/> for no limit; it must not be negative.</summary>
        public int? HyphenateLimitLines { get; init; }

        /// <summary>
        /// How much room a line may have left at its end before its last word is hyphenated (CSS <c>hyphenate-limit-zone</c>), in layout units; a word is
        /// hyphenated to fill a line only when the unfilled space would be at least this. It is not negative or infinite, and a percentage is the caller's to resolve.
        /// </summary>
        public double HyphenateLimitZone { get; init; }

        /// <summary>Whether the last full line may end with a hyphenation (CSS <c>hyphenate-limit-last</c>).</summary>
        public HyphenateLimitLast HyphenateLimitLast { get; init; }

        /// <summary>How CSS <c>word-break</c> and <c>line-break</c> tailor where lines may break.</summary>
        public LineBreakOptions LineBreak { get; init; }

        /// <summary>What to do with a word that is wider than a line.</summary>
        public OverflowWrap OverflowWrap { get; init; }

        /// <summary>Whether lines are kept from breaking where they do not fit, so that only forced breaks end a line (CSS <c>text-wrap: nowrap</c>).</summary>
        public bool NoWrap { get; init; }

        /// <summary>
        /// The height of a line as a multiple of the largest font size on it, or <see langword="null"/> for what the faces themselves
        /// ask for (their ascent, descent and line gap). The leading is shared equally above and below.
        /// </summary>
        public double? LineHeight { get; init; }
    }
}
