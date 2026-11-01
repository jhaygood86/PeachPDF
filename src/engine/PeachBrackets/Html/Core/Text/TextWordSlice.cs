using PeachBrackets.Html.Dom;

namespace PeachBrackets.Html.TextGeometry
{
    /// <summary>
    /// Where one laid-out word sits in the text of a <see cref="PeachBrackets.Text.TextRun"/>, with its character edges relative to the word's own
    /// left edge, so a painter that knows only the word's rectangle can place a part of it.
    /// </summary>
    /// <param name="Word">the word</param>
    /// <param name="TextStart">the UTF-16 index in the run's text where the word's own text begins</param>
    /// <param name="Length">the word's length in UTF-16 units (the logical text, so it is the span the run's text holds)</param>
    /// <param name="Edges">the x of the edge before each index of the word's text and after its last (<c>Length + 1</c> values), in points from the word's left edge</param>
    /// <param name="GapStart">the left end of the separator space drawn before the word, in points from the word's left edge, when the run's text holds one just before it</param>
    /// <param name="GapEnd">the right end of that space</param>
    internal sealed record TextWordSlice(CssRect Word, int TextStart, int Length, double[] Edges, double GapStart, double GapEnd)
    {
        /// <summary>Whether a separator space sits in the run's text just before the word (at <c>TextStart - 1</c>).</summary>
        public bool HasGap => GapEnd > GapStart;
    }
}
