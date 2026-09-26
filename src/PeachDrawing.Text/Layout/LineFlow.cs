using System;
using System.Diagnostics.CodeAnalysis;

namespace PeachDrawing.Text.Layout
{
    /// <summary>
    /// The room a line of a paragraph has, as its caller worked it out: where the space starts and ends across the layout, how far the text is indented into it, and where
    /// the line's top is.
    /// </summary>
    /// <param name="Left">The left edge of the space, in layout units. It is where a left-to-right line starts (after the indent), and the far edge of a right-to-left one. It must be finite.</param>
    /// <param name="Right">The right edge of the space, or <see cref="double.PositiveInfinity"/> for a line that breaks only where it is forced to. A space that ends before it starts has no width.</param>
    /// <param name="Indent">How far the text is moved in from the start edge of the space (<see cref="LineFlow.GetIndent"/> gives the <see cref="ParagraphStyle.TextIndent"/> of the line); it must be finite.</param>
    /// <param name="Top">The distance from the top of the layout to the top of the line, in layout units; it must be finite.</param>
    public readonly record struct LineSpace(double Left, double Right, double Indent = 0, double Top = 0);

    /// <summary>
    /// Where the next line of a paragraph starts. A cursor is an immutable value: laying a line out never changes the cursor it is given, one saved earlier can be laid out
    /// from again (a caller that undoes a line, or lays it out in another space, keeps the cursor from before it), and <c>default</c> is the start of the paragraph.
    /// </summary>
    public readonly struct FlowCursor : IEquatable<FlowCursor>
    {
        private readonly int _offset;
        private readonly int _hyphenatedLines;
        private readonly bool _isEnd;

        internal FlowCursor(int offset, int hyphenatedLines, bool isEnd)
        {
            _offset = offset;
            _hyphenatedLines = hyphenatedLines;
            _isEnd = isEnd;
        }

        /// <summary>The UTF-16 offset in the text of the first character of the line that starts here.</summary>
        public int Offset => _offset;

        /// <summary>Whether the last line of the paragraph has been laid out, so that there is no line after this cursor.</summary>
        public bool IsEnd => _isEnd;

        internal int HyphenatedLines => _hyphenatedLines;

        internal static FlowCursor Finished(int offset) => new(offset, 0, true);

        /// <inheritdoc/>
        public bool Equals(FlowCursor other) => _offset == other._offset && _hyphenatedLines == other._hyphenatedLines && _isEnd == other._isEnd;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is FlowCursor other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(_offset, _hyphenatedLines, _isEnd);

        /// <summary>Whether two cursors are at the same place.</summary>
        /// <param name="left">A cursor.</param>
        /// <param name="right">Another.</param>
        public static bool operator ==(FlowCursor left, FlowCursor right) => left.Equals(right);

        /// <summary>Whether two cursors are at different places.</summary>
        /// <param name="left">A cursor.</param>
        /// <param name="right">Another.</param>
        public static bool operator !=(FlowCursor left, FlowCursor right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString() => _isEnd ? "end" : $"{_offset}";
    }

    /// <summary>
    /// A paragraph laid out one line at a time in the room its caller gives each line: the tier for a caller that owns what the paragraph flows around (floats, columns,
    /// pages, a retry when a line's height grows), where <see cref="Paragraph.Layout"/> is for one that has a width and nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="TryNext"/> is a function of the paragraph, the cursor and the space and nothing else: it never changes its inputs, keeps no state, and gives the same line for
    /// the same arguments however the calls are ordered, so a caller replaces an undo of its own by calling it again with an earlier cursor. It can be called from several threads.
    /// </para>
    /// <para>
    /// What the flow leaves to its caller: <see cref="ParagraphStyle.MaxLines"/> (the caller decides how many lines there are; <see cref="ParagraphStyle.TextOverflow"/> still cuts a line
    /// that overflows its space), where the next line's top is (the height of the line laid out is <see cref="LineBox.Height"/>), and the indent (see <see cref="GetIndent"/>). The limit on
    /// hyphenating the last full line is worked out against the space of the line being laid out, since the next line's is not known, and tab stops are measured from the start
    /// edge of the space plus the indent, not from the edge of a block that a float narrowed the space of. A line's runs are placed in layout coordinates, at the space's left edge.
    /// </para>
    /// </remarks>
    public sealed class LineFlow
    {
        internal LineFlow(Paragraph paragraph)
        {
            Paragraph = paragraph;
        }

        /// <summary>The paragraph that flows.</summary>
        public Paragraph Paragraph { get; }

        /// <summary>The cursor at the start of the paragraph, which is also <c>default(FlowCursor)</c>.</summary>
        public FlowCursor Start => default;

        /// <summary>
        /// How far the line that starts at <paramref name="cursor"/> is indented by the paragraph's <see cref="ParagraphStyle.TextIndent"/>: the first line, the lines after a forced break, and
        /// the other lines, as the style says. A caller that follows the style passes this as the <see cref="LineSpace.Indent"/> of the line's space.
        /// </summary>
        /// <param name="cursor">A cursor of this paragraph's flow.</param>
        /// <returns>The indent, in layout units.</returns>
        /// <exception cref="ArgumentException">The cursor is beyond the end of the text.</exception>
        public double GetIndent(in FlowCursor cursor)
        {
            CheckCursor(cursor);
            return Paragraph.IndentAt(cursor.Offset);
        }

        /// <summary>
        /// Lays out the line that starts at <paramref name="cursor"/> in <paramref name="space"/>, and finds where the line after it starts.
        /// </summary>
        /// <param name="cursor">Where the line starts: <see cref="Start"/>, or the cursor an earlier call gave back.</param>
        /// <param name="space">The room the line has.</param>
        /// <param name="line">The line, placed in layout coordinates with its top at <see cref="LineSpace.Top"/>; <see langword="null"/> when there is no line.</param>
        /// <param name="next">Where the next line starts; the cursor itself when there is no line.</param>
        /// <returns><see langword="false"/> when the last line has already been laid out (<see cref="FlowCursor.IsEnd"/>), and <see langword="true"/> with the line otherwise.</returns>
        /// <exception cref="ArgumentException">The cursor is beyond the end of the text, or a number in <paramref name="space"/> that must be finite is not, or an edge is not a number.</exception>
        public bool TryNext(in FlowCursor cursor, in LineSpace space, [NotNullWhen(true)] out LineBox? line, out FlowCursor next)
        {
            CheckCursor(cursor);
            if (!double.IsFinite(space.Left) || double.IsNaN(space.Right) || !double.IsFinite(space.Indent) || !double.IsFinite(space.Top))
            {
                throw new ArgumentException("The space's left edge, indent and top must be finite, and its right edge a number.", nameof(space));
            }

            if (cursor.IsEnd)
            {
                line = null;
                next = cursor;
                return false;
            }

            line = ParagraphLayoutBuilder.LayFlowLine(Paragraph, cursor, space, out next);
            return true;
        }

        private void CheckCursor(in FlowCursor cursor)
        {
            if (cursor.Offset < 0 || cursor.Offset > Paragraph.Text.Length)
            {
                throw new ArgumentException("The cursor is not from this paragraph's flow.", nameof(cursor));
            }
        }
    }
}
