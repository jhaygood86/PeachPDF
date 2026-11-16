namespace PeachPDF.Layout
{
    /// <summary>One run of text within a <see cref="ITextSpanContainer"/>, styleable via <see cref="ITextStyle"/>.</summary>
    public interface ITextSpan : ITextStyle
    {
        /// <summary>Sets the decoration line's stroke style (requires a decoration line - <see cref="ITextStyle.Underline"/>/<see cref="ITextStyle.Overline"/>/<see cref="ITextStyle.Strikethrough"/> - to be visible).</summary>
        ITextSpan DecorationStyle(PdfTextDecorationStyle style);

        /// <summary>Draws the decoration line solid - <see cref="DecorationStyle"/> with <see cref="PdfTextDecorationStyle.Solid"/>.</summary>
        ITextSpan DecorationSolid();

        /// <summary>Draws the decoration line double - <see cref="DecorationStyle"/> with <see cref="PdfTextDecorationStyle.Double"/>.</summary>
        ITextSpan DecorationDouble();

        /// <summary>Draws the decoration line dotted - <see cref="DecorationStyle"/> with <see cref="PdfTextDecorationStyle.Dotted"/>.</summary>
        ITextSpan DecorationDotted();

        /// <summary>Draws the decoration line dashed - <see cref="DecorationStyle"/> with <see cref="PdfTextDecorationStyle.Dashed"/>.</summary>
        ITextSpan DecorationDashed();

        /// <summary>Draws the decoration line wavy - <see cref="DecorationStyle"/> with <see cref="PdfTextDecorationStyle.Wavy"/>.</summary>
        ITextSpan DecorationWavy();

        /// <summary>Sets the decoration line's color, independent of the text's own color.</summary>
        ITextSpan DecorationColor(PdfColor color);

        /// <summary>Sets the decoration line's stroke thickness.</summary>
        ITextSpan DecorationThickness(PdfLength thickness);
    }
}
