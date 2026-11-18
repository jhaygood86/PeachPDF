namespace PeachReader.Core;

/// <summary>A place in a document: a page and how far below its top, in points, as the page is shown.</summary>
/// <param name="PageIndex">the zero-based page number</param>
/// <param name="TopPoints">the distance below the top of the page in points; 0 is the top of the page</param>
public readonly record struct ReaderLocation(int PageIndex, double TopPoints = 0);
