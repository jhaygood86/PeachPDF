namespace PeachBrackets;

/// <summary>
/// One page of a laid-out <see cref="HtmlDocument"/>.
/// </summary>
/// <param name="Index">the zero-based position of the page in the document</param>
/// <param name="WidthPoints">the width of the page box, in points</param>
/// <param name="HeightPoints">the height of the page box, in points</param>
/// <param name="BleedPoints">
/// how far the bleed area (css-page-3 <c>bleed</c>) extends beyond the page box on every side, in points; 0 for a page with none. The
/// page's background is painted into it.
/// </param>
/// <param name="MarksPoints">
/// how much further the sheet extends beyond the bleed area to hold printer's marks (css-page-3 <c>marks</c>), in points; 0 for a page
/// with none. The marks are drawn there.
/// </param>
/// <remarks>
/// A page's canvas keeps its origin at the top-left corner of the page box, so the bleed area and the marks are at negative coordinates
/// and past <see cref="WidthPoints"/>/<see cref="HeightPoints"/>: the sheet a format writes is the page box grown by
/// <c>BleedPoints + MarksPoints</c> on every side, with the page box as its trim box.
/// </remarks>
public readonly record struct DocumentPage(int Index, double WidthPoints, double HeightPoints, double BleedPoints = 0, double MarksPoints = 0)
{
    /// <summary>Whether the sheet is larger than the page box (the page has a bleed or marks).</summary>
    public bool HasPrintBoxes => BleedPoints > 0 || MarksPoints > 0;
}
