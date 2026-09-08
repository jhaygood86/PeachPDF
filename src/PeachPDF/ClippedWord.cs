using System.Collections.Generic;

namespace PeachPDF;

/// <summary>
/// One word that was DRAWN into the content stream and then truncated by a clip.
/// </summary>
/// <remarks>
/// <para>
/// This exists because reading the finished PDF cannot detect this class of loss. A glyph that was
/// emitted and then truncated by the PDF clip operator is still IN the content stream, so a reader
/// that parses that stream finds it and reports the word present, while a renderer that honours the
/// clip shows only part of it. Measured with the two as an oracle pair on a table cell narrower than
/// its content: PdfPig reads every character back and reports the document complete; MuPDF cannot
/// find the text at all. Both are correct — the difference is simply not recorded in the file. So
/// the engine, at the moment it decides to draw, is the only thing that can report it.
/// </para>
/// <para>
/// It is deliberately ADDITIVE to reading the output back, never a replacement. A content-stream
/// reader answers "did the glyph land in the file"; an engine-side collector answers "did the
/// painter intend to emit it", and the two catch different defects — a document declaring one image
/// where the PDF contains zero is caught only by the former.
/// </para>
/// <para>
/// It UNDER-reports, and that is the safe direction but must not be mistaken for completeness.
/// The clip this is measured against is <c>RGraphics</c>'s tracked rect stack, and two clips never
/// reach it: a <c>border-radius</c> or <c>clip-path</c> clip (the path-clip push deliberately
/// leaves the tracked bound over-wide, and the exclude-push is a no-op), and the page-level
/// <c>XGraphics.IntersectClip</c> the PDF generator applies outside the stack. So "nothing
/// reported" is a weaker statement than "nothing was clipped".
/// </para>
/// </remarks>
/// <param name="Text">The word as it was drawn.</param>
/// <param name="DrawnWidth">Width of the word's own rect, before the clip was applied.</param>
/// <param name="VisibleWidth">
/// Width that survived the clip. Less than <paramref name="DrawnWidth"/> when width was a clipped
/// dimension; EQUAL to it for a word clipped only vertically, which is recorded too — a box short
/// enough to cut a line's height but wide enough to keep the whole word is the same silent loss.
/// </param>
/// <param name="DrawnHeight">Height of the word's own rect, before the clip was applied.</param>
/// <param name="VisibleHeight">Height that survived the clip.</param>
public readonly record struct ClippedWord(
    string Text,
    double DrawnWidth,
    double VisibleWidth,
    double DrawnHeight,
    double VisibleHeight)
{
    /// <summary>
    /// The fraction of the word's area that survived the clip, 0 to 1. A calibration handle: the
    /// threshold for what counts as material is a measurement over real documents, not a guess.
    /// </summary>
    public double KeptFraction =>
        DrawnWidth <= 0 || DrawnHeight <= 0
            ? 1d
            : (VisibleWidth / DrawnWidth) * (VisibleHeight / DrawnHeight);
}

/// <summary>
/// Every <see cref="ClippedWord"/> a single render produced, in paint order.
/// </summary>
public sealed class ClipReport
{
    internal readonly List<ClippedWord> Words = [];

    /// <summary>The clipped words, in paint order.</summary>
    public IReadOnlyList<ClippedWord> ClippedWords => Words;

    /// <summary>
    /// Appends <paramref name="other"/>'s words to this report, preserving paint order. Used by
    /// <c>PdfGenerator.AddPdfPages</c> so a document built over several calls carries every call's
    /// findings rather than only the last one's.
    /// </summary>
    internal void Append(ClipReport other) => Words.AddRange(other.Words);

    /// <summary>How many characters were carried by words that were truncated.</summary>
    /// <remarks>
    /// The whole word's length, not an estimate of the invisible part: a word is the smallest unit
    /// the painter knows, and apportioning characters to a sub-rectangle would invent precision the
    /// measurement does not have.
    /// </remarks>
    public int ClippedChars
    {
        get
        {
            var n = 0;
            foreach (var w in Words) n += w.Text.Length;
            return n;
        }
    }
}
