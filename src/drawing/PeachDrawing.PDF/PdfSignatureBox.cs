using System;
using PeachDrawing.Core;

namespace PeachDrawing.PDF;

/// <summary>
/// Makes a signature visible: a box drawn on a page that says who signed, why and when. Without one the signature is an invisible field on
/// the first page. See <see cref="PdfSigningOptions.Box"/>.
/// </summary>
/// <remarks>
/// The box has a thin border and its text in Helvetica, one of the standard fonts every PDF reader has, so nothing is embedded for it: a
/// file that claims PDF/A or PDF/X, which require every font to be embedded, cannot have a visible signature, and neither can a page
/// that is rotated. Draw anything richer (a logo, a handwritten image) as ordinary page content in the box's place.
/// </remarks>
public sealed class PdfSignatureBox
{
    /// <summary>The page the box goes on, counted from 1. Defaults to the first page.</summary>
    public int PageNumber { get; set; } = 1;

    /// <summary>
    /// The box, in points from the top-left corner of the page as it is shown (the same way a <see cref="PdfComposer"/> canvas is laid
    /// out). Its width and height must be positive.
    /// </summary>
    public Rect Bounds { get; set; }

    /// <summary>
    /// The text, with lines separated by a line feed, or <see langword="null"/> (the default) for the signer's name, the reason, the place
    /// and the time of signing. Characters outside Latin-1 are drawn as a question mark.
    /// </summary>
    public string? Text { get; set; }

    /// <summary>The size of the text in points, from 4 to 72. Defaults to 9.</summary>
    public double FontSize { get; set; } = 9;

    internal PdfSignatureBox Clone() => new() { PageNumber = PageNumber, Bounds = Bounds, Text = Text, FontSize = FontSize };

    internal void Validate()
    {
        if (PageNumber < 1)
            throw new InvalidOperationException("PdfSignatureBox.PageNumber counts pages from 1.");
        if (!(Bounds.Width > 0) || !(Bounds.Height > 0) || !double.IsFinite(Bounds.X) || !double.IsFinite(Bounds.Y)
            || !double.IsFinite(Bounds.Width) || !double.IsFinite(Bounds.Height))
            throw new InvalidOperationException("PdfSignatureBox.Bounds must be a finite rectangle with a positive width and height.");
        if (!(FontSize >= 4 && FontSize <= 72))
            throw new InvalidOperationException("PdfSignatureBox.FontSize must be from 4 to 72 points.");
    }
}
