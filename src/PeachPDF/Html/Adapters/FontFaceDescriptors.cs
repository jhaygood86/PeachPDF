using PeachDrawing.Text;

namespace PeachPDF.Html.Adapters
{
    /// <summary>
    /// What an <c>@font-face</c> rule declares about the face it registers, in place of what the font file says about itself: the weights,
    /// widths and oblique angles it covers (CSS Fonts 4 section 4.4, where each descriptor is a value or a range) and whether it is
    /// italic. A null member says nothing, so the file is asked, and a variable font then covers the range of its own axes.
    /// </summary>
    /// <param name="Weight">The weights the face covers, on the CSS scale of 1 to 1000.</param>
    /// <param name="IsItalic">Whether the face is italic or oblique.</param>
    /// <param name="Width">The widths the face covers, as percentages of the normal width.</param>
    /// <param name="Oblique">The oblique angles the face covers, in degrees leaning to the right.</param>
    internal readonly record struct FontFaceDescriptors(
        AxisRange? Weight = null,
        bool? IsItalic = null,
        AxisRange? Width = null,
        AxisRange? Oblique = null);
}
