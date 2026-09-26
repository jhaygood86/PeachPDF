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
    }

    /// <summary>The narrowest and widest a paragraph can be laid out.</summary>
    /// <param name="MinContent">The width of the widest unbreakable piece: laying the paragraph out narrower makes something overflow.</param>
    /// <param name="MaxContent">The width of the widest line when nothing but forced breaks end a line: laying it out wider changes nothing.</param>
    public readonly record struct ContentWidths(double MinContent, double MaxContent);

    /// <summary>The look of a run of text in a paragraph.</summary>
    /// <param name="Typeface">The face to set the text in.</param>
    /// <param name="Size">The size of the em, in the units the layout is measured in.</param>
    /// <param name="Shape">What to ask of the shaper, or <see langword="null"/> for the defaults. Its script tag, joining forms and direction are filled in from the text.</param>
    public readonly record struct RunStyle(Typeface Typeface, double Size, ShapeSettings? Shape = null);

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
