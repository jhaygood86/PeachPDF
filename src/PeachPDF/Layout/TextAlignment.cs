namespace PeachPDF.Layout
{
    /// <summary>A paragraph's horizontal text alignment (CSS <c>text-align</c>).</summary>
    public enum TextAlignment
    {
        /// <summary>Aligns text to the left edge.</summary>
        Left,

        /// <summary>Centers text.</summary>
        Center,

        /// <summary>Aligns text to the right edge.</summary>
        Right,

        /// <summary>Stretches each line (except the last) to fill the full width.</summary>
        Justify,

        /// <summary>Aligns text to the start edge, resolved against the paragraph's own <see cref="ITextStyle.Direction"/> (start = left in LTR, right in RTL).</summary>
        Start,

        /// <summary>Aligns text to the end edge, resolved against the paragraph's own <see cref="ITextStyle.Direction"/> (end = right in LTR, left in RTL).</summary>
        End
    }
}
