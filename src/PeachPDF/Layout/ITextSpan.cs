namespace PeachPDF.Layout
{
    /// <summary>One run of text within a <see cref="ITextSpanContainer"/>, styleable via <see cref="ITextStyle"/>.</summary>
    public interface ITextSpan : ITextStyle
    {
        /// <summary>Sets the decoration line's stroke style (requires a decoration line - <see cref="ITextStyle.Underline"/>/<see cref="ITextStyle.Overline"/>/<see cref="ITextStyle.Strikethrough"/> - to be visible).</summary>
        ITextSpan DecorationStyle(PdfTextDecorationStyle style);

        /// <summary>Sets the decoration line's color, independent of the text's own color.</summary>
        ITextSpan DecorationColor(PdfColor color);

        /// <summary>Sets the decoration line's stroke thickness.</summary>
        ITextSpan DecorationThickness(PdfLength thickness);
    }
}
