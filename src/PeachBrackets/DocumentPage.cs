namespace PeachBrackets;

/// <summary>
/// One page of a laid-out <see cref="HtmlDocument"/>.
/// </summary>
/// <param name="Index">the zero-based position of the page in the document</param>
/// <param name="WidthPoints">the width of the sheet, in points</param>
/// <param name="HeightPoints">the height of the sheet, in points</param>
public readonly record struct DocumentPage(int Index, double WidthPoints, double HeightPoints);
