// "Therefore those skilled at the unorthodox
// are infinite as heaven and earth,
// inexhaustible as the great rivers.
// When they come to an end,
// they begin again,
// like the days and months;
// they die and are reborn,
// like the four seasons."
//
// - Sun Tsu,
// "The Art of War"

using PeachPDF.Html.Adapters.Entities;
using System.Collections.Generic;

namespace PeachPDF.Svg
{
    /// <summary>
    /// Parses the <c>points</c> attribute of <c>&lt;polygon&gt;</c>/<c>&lt;polyline&gt;</c> - a list
    /// of coordinate pairs using the same whitespace/comma separator rules as path data.
    /// </summary>
    internal static class SvgPointsParser
    {
        public static RPoint[] Parse(string? points)
        {
            if (string.IsNullOrWhiteSpace(points))
                return [];

            var result = new List<RPoint>();
            var pos = 0;

            while (SvgNumberScanner.TryReadNumber(points, ref pos, out var x))
            {
                if (!SvgNumberScanner.TryReadNumber(points, ref pos, out var y))
                    break;

                result.Add(new RPoint(x, y));
            }

            return [.. result];
        }
    }
}
