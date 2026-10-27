using PeachDrawing.Core;
using System;
using System.Collections.Generic;

namespace PeachBrackets.Text;

/// <summary>
/// One line's worth of drawn text, positioned on its page, with where each character sits across it: what a consumer needs to draw a
/// selection, map a pointer to a character, or copy text out of a laid-out document. See <see cref="HtmlDocument.TextRuns"/>.
/// </summary>
/// <remarks>
/// Everything is in points, from the top-left of the sheet, the same space as <see cref="Annotations.PageLink.Bounds"/>.
/// </remarks>
public sealed class TextRun
{
    internal TextRun(
        int pageIndex, string text, Rect bounds, double baseline, double ascent, double descent,
        double[] characterOffsets, int[] characterBoundaries, bool isRtl, int blockId, string? elementId, string? linkUri,
        int lineId, bool followsForcedBreak, IReadOnlyList<PeachBrackets.Html.TextGeometry.TextWordSlice> slices)
    {
        Slices = slices;
        PageIndex = pageIndex;
        Text = text;
        Bounds = bounds;
        Baseline = baseline;
        Ascent = ascent;
        Descent = descent;
        CharacterOffsets = characterOffsets;
        CharacterBoundaries = characterBoundaries;
        IsRtl = isRtl;
        BlockId = blockId;
        ElementId = elementId;
        LinkUri = linkUri;
        LineId = lineId;
        FollowsForcedBreak = followsForcedBreak;
    }

    /// <summary>The zero-based index of the page the run is on.</summary>
    public int PageIndex { get; }

    /// <summary>The text as it was drawn, in logical (reading) order: whitespace collapsed as the layout collapsed it, bidirectional text not reordered or mirrored.</summary>
    public string Text { get; }

    /// <summary>The rectangle the run's words cover, in points.</summary>
    public Rect Bounds { get; }

    /// <summary>The y coordinate of the baseline, in points.</summary>
    public double Baseline { get; }

    /// <summary>How far the tallest font in the run rises above <see cref="Baseline"/>, in points.</summary>
    public double Ascent { get; }

    /// <summary>How far the deepest font in the run falls below <see cref="Baseline"/>, in points.</summary>
    public double Descent { get; }

    /// <summary>
    /// The x coordinate, in points, of the edge before each UTF-16 index of <see cref="Text"/> and after the last one, so it holds
    /// <c>Text.Length + 1</c> values. The values follow the logical order of the text, so in a right-to-left run they decrease. An index inside
    /// a grapheme cluster (or a surrogate pair) has the offset of the boundary before the cluster; use <see cref="CharacterBoundaries"/> to
    /// know which indices are real positions.
    /// </summary>
    public IReadOnlyList<double> CharacterOffsets { get; }

    /// <summary>The UTF-16 indices of <see cref="Text"/> a caret can sit at - the grapheme cluster boundaries, including 0 and <c>Text.Length</c>.</summary>
    public IReadOnlyList<int> CharacterBoundaries { get; }

    /// <summary>Whether the run is laid out right to left.</summary>
    public bool IsRtl { get; }

    /// <summary>
    /// Identifies the block (paragraph, list item, table cell, <c>pre</c> and so on) the run belongs to, unique within the document. Runs of one
    /// block share it; a change of it between two runs is where copied text takes a line break.
    /// </summary>
    public int BlockId { get; }

    /// <summary>The <c>id</c> of the block element the run belongs to, or null if it has none.</summary>
    public string? ElementId { get; }

    /// <summary>The address of the link the run is inside, resolved against the document's base, or null if it is not in a link.</summary>
    public string? LinkUri { get; }

    internal IReadOnlyList<PeachBrackets.Html.TextGeometry.TextWordSlice> Slices { get; }

    internal int LineId { get; }

    internal bool FollowsForcedBreak { get; }

    internal double Left => Math.Min(CharacterOffsets[0], CharacterOffsets[CharacterOffsets.Count - 1]);
}

/// <summary>
/// A place between two characters of the laid-out text: a character index in one of <see cref="HtmlDocument.TextRuns"/>.
/// </summary>
/// <param name="RunIndex">the index of the run in <see cref="HtmlDocument.TextRuns"/></param>
/// <param name="CharIndex">the UTF-16 index in that run's <see cref="TextRun.Text"/>, from 0 to its length</param>
public readonly record struct TextPosition(int RunIndex, int CharIndex) : IComparable<TextPosition>
{
    /// <inheritdoc/>
    public int CompareTo(TextPosition other) => RunIndex != other.RunIndex ? RunIndex.CompareTo(other.RunIndex) : CharIndex.CompareTo(other.CharIndex);
}

/// <summary>A stretch of the laid-out text, from <see cref="Start"/> up to but not including <see cref="End"/>.</summary>
/// <param name="Start">where the stretch begins</param>
/// <param name="End">where it ends</param>
public readonly record struct TextRange(TextPosition Start, TextPosition End);
