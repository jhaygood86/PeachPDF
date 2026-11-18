using System;

namespace PeachDrawing.PDF;

/// <summary>
/// A run of pages that a PDF viewer labels the same way ("i, ii, iii", "A-1, A-2"), added with <see cref="PdfComposer.AddPageLabels"/>. A range
/// starts at <see cref="StartPageIndex"/> and lasts until the next range starts, or to the end of the document.
/// </summary>
public sealed class PdfPageLabelRange
{
    /// <summary>Creates a range.</summary>
    /// <param name="startPageIndex">the zero-based index of the first page of the range</param>
    /// <param name="style">how the page number is written</param>
    /// <param name="prefix">text before the number, or <see langword="null"/> for none</param>
    /// <param name="startNumber">the number of the first page of the range, 1 or more</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="startPageIndex"/> is negative, <paramref name="startNumber"/> is less than 1, or <paramref name="style"/> is not a defined style</exception>
    public PdfPageLabelRange(int startPageIndex, PdfPageLabelStyle style, string? prefix = null, int startNumber = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startPageIndex);
        ArgumentOutOfRangeException.ThrowIfLessThan(startNumber, 1);
        if (!Enum.IsDefined(style)) throw new ArgumentOutOfRangeException(nameof(style), style, "Not a page label style.");

        StartPageIndex = startPageIndex;
        Style = style;
        Prefix = prefix;
        StartNumber = startNumber;
    }

    /// <summary>The zero-based index of the first page of the range.</summary>
    public int StartPageIndex { get; }

    /// <summary>How the page number is written.</summary>
    public PdfPageLabelStyle Style { get; }

    /// <summary>The text before the number, or <see langword="null"/>.</summary>
    public string? Prefix { get; }

    /// <summary>The number of the first page of the range.</summary>
    public int StartNumber { get; }
}
