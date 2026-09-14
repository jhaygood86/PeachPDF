namespace PeachPDF.Layout
{
    /// <summary>
    /// Text styling shared by a page/container's <c>DefaultTextStyle</c> and an individual
    /// <see cref="ITextSpan"/>. Every member maps directly onto an existing, already-fully-supported CSS
    /// property.
    /// </summary>
    public interface ITextStyle
    {
        /// <summary>Sets <c>font-weight: bold</c> (700).</summary>
        ITextStyle Bold();

        /// <summary>Sets a specific numeric <c>font-weight</c> (1-1000).</summary>
        ITextStyle FontWeight(int weight);

        /// <summary>Sets <c>font-style: italic</c>.</summary>
        ITextStyle Italic();

        /// <summary>Sets the font size.</summary>
        ITextStyle FontSize(PdfLength size);

        /// <summary>Sets the text color.</summary>
        ITextStyle FontColor(PdfColor color);

        /// <summary>Sets the font family.</summary>
        ITextStyle FontFamily(string family);

        /// <summary>Sets the font family with an explicit fallback.</summary>
        ITextStyle FontFamily(string primary, string fallback);

        /// <summary>Sets a background color behind the text's own inline box.</summary>
        ITextStyle BackgroundColor(PdfColor color);

        /// <summary>Underlines the text (<c>text-decoration-line: underline</c>).</summary>
        ITextStyle Underline();

        /// <summary>Draws a line over the text (<c>text-decoration-line: overline</c>).</summary>
        ITextStyle Overline();

        /// <summary>Strikes through the text (<c>text-decoration-line: line-through</c>).</summary>
        ITextStyle Strikethrough();

        /// <summary>Sets the extra space added between characters.</summary>
        ITextStyle LetterSpacing(PdfLength spacing);

        /// <summary>Sets the extra space added between words.</summary>
        ITextStyle WordSpacing(PdfLength spacing);

        /// <summary>Sets the line height (as a bare multiplier of the font size, e.g. <c>1.5</c>).</summary>
        ITextStyle LineHeight(double multiplier);

        /// <summary>Lowers and shrinks the text (<c>vertical-align: sub</c>).</summary>
        ITextStyle Subscript();

        /// <summary>Raises and shrinks the text (<c>vertical-align: super</c>).</summary>
        ITextStyle Superscript();

        /// <summary>Enables or disables an OpenType font feature (e.g. <c>"liga"</c>, <c>"smcp"</c>).</summary>
        ITextStyle FontFeature(string tag, bool enabled = true);

        /// <summary>Sets the base text direction, for bidi/RTL content.</summary>
        ITextStyle Direction(PdfTextDirection direction);

        /// <summary>Allows a line to break anywhere between characters when it would otherwise overflow (<c>word-break: break-all</c>).</summary>
        ITextStyle BreakAnywhere();
    }
}
