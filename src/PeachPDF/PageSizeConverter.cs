using PeachDrawing.Core;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace PeachPDF;

/// <summary>
/// Converts a <see cref="PageSize"/> to its dimensions in points.
/// </summary>
internal static class PageSizeConverter
{
    // The international definitions are:
    //   1 inch == 25.4 mm
    //   1 inch == 72 point
    // Source http://www.din-formate.de/reihe-a-din-groessen-mm-pixel-dpi.html for the ISO sizes.
    static readonly FrozenDictionary<PageSize, Size> Sizes = new Dictionary<PageSize, Size>
    {
        [PageSize.A0] = new Size(2384, 3370),
        [PageSize.A1] = new Size(1684, 2384),
        [PageSize.A2] = new Size(1191, 1684),
        [PageSize.A3] = new Size(842, 1191),
        [PageSize.A4] = new Size(595, 842),
        [PageSize.A5] = new Size(420, 595),
        [PageSize.A6] = new Size(298, 420),
        [PageSize.RA0] = new Size(2438, 3458),
        [PageSize.RA1] = new Size(1729, 2438),
        [PageSize.RA2] = new Size(1219, 1729),
        [PageSize.RA3] = new Size(865, 1219),
        [PageSize.RA4] = new Size(609, 865),
        [PageSize.RA5] = new Size(433, 609),
        [PageSize.B0] = new Size(2835, 4008),
        [PageSize.B1] = new Size(2004, 2835),
        [PageSize.B2] = new Size(1417, 2004),
        [PageSize.B3] = new Size(1001, 1417),
        [PageSize.B4] = new Size(709, 1001),
        [PageSize.B5] = new Size(499, 709),
        // The non-ISO sizes ...
        [PageSize.Quarto] = new Size(576, 720), // 8 x 10 inch
        [PageSize.Foolscap] = new Size(576, 936), // 8 x 13 inch
        [PageSize.Executive] = new Size(540, 720), // 7.5 x 10 inch
        [PageSize.GovernmentLetter] = new Size(576, 756), // 8 x 10.5 inch
        [PageSize.Letter] = new Size(612, 792), // 8.5 x 11 inch
        [PageSize.Legal] = new Size(612, 1008), // 8.5 x 14 inch
        [PageSize.Ledger] = new Size(1224, 792), // 17 x 11 inch
        [PageSize.Tabloid] = new Size(792, 1224), // 11 x 17 inch
        [PageSize.Post] = new Size(1126, 1386), // 15.5 x 19.25 inch
        [PageSize.Crown] = new Size(1440, 1080), // 20 x 15 inch
        [PageSize.LargePost] = new Size(1188, 1512), // 16.5 x 21 inch
        [PageSize.Demy] = new Size(1260, 1584), // 17.5 x 22 inch
        [PageSize.Medium] = new Size(1296, 1656), // 18 x 23 inch
        [PageSize.Royal] = new Size(1440, 1800), // 20 x 25 inch
        [PageSize.Elephant] = new Size(1565, 2016), // 23 x 28 inch
        [PageSize.DoubleDemy] = new Size(1692, 2520), // 23.5 x 35 inch
        [PageSize.QuadDemy] = new Size(2520, 3240), // 35 x 45 inch
        [PageSize.STMT] = new Size(396, 612), // 5.5 x 8.5 inch
        [PageSize.Folio] = new Size(612, 936), // 8.5 x 13 inch
        [PageSize.Statement] = new Size(396, 612), // 5.5 x 8.5 inch
        [PageSize.Size10x14] = new Size(720, 1008), // 10 x 14 inch
    }.ToFrozenDictionary();

    /// <summary>
    /// Converts the specified page size enumeration to a pair of values in point.
    /// </summary>
    public static Size ToSize(PageSize value, int dpi = 72)
    {
        if (!Sizes.TryGetValue(value, out var size))
            throw new ArgumentException("Invalid PageSize.", nameof(value));

        var ratio = dpi / 72.0;

        return new Size(size.Width * ratio, size.Height * ratio);
    }
}
