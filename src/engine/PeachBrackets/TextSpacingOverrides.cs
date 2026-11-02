using System.Globalization;

namespace PeachBrackets;

/// <summary>
/// Spacing a reader asks for in place of the document's own, so text stays readable for people who need more room around it. Each value is a
/// multiple of the font size and is applied over the document's own rules, as a user style sheet with <c>!important</c> would.
/// </summary>
/// <remarks>
/// <see cref="Wcag"/> holds the minimums that WCAG 2.2 success criterion 1.4.12 (Text Spacing) asks a page to survive without losing content. The
/// overrides are added to <see cref="HtmlDocumentOptions.UserStyleSheet"/>, so they are ignored when <see cref="HtmlDocumentOptions.StyleSheet"/> is set.
/// </remarks>
/// <param name="LineHeight">the line height, as a multiple of the font size</param>
/// <param name="ParagraphSpacing">the space below a paragraph, as a multiple of the font size</param>
/// <param name="LetterSpacing">the space added after each letter, as a multiple of the font size</param>
/// <param name="WordSpacing">the space added after each word, as a multiple of the font size</param>
public sealed record TextSpacingOverrides(double LineHeight, double ParagraphSpacing, double LetterSpacing, double WordSpacing)
{
    /// <summary>The spacing WCAG 1.4.12 asks to be supported: line height 1.5, paragraph spacing 2, letter spacing 0.12 and word spacing 0.16.</summary>
    public static TextSpacingOverrides Wcag { get; } = new(1.5, 2, 0.12, 0.16);

    /// <summary>The style sheet that applies the spacing.</summary>
    internal string ToCss()
    {
        static string Number(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

        return $"*, *::before, *::after {{ line-height: {Number(LineHeight)} !important; letter-spacing: {Number(LetterSpacing)}em !important; word-spacing: {Number(WordSpacing)}em !important }}\n"
            + $"p {{ margin-bottom: {Number(ParagraphSpacing)}em !important }}\n";
    }
}
