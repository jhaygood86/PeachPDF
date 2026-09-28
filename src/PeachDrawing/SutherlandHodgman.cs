using PeachDrawing.Abstractions;
using System.Collections.Generic;

namespace PeachDrawing;

/// <summary>Clips a closed polygon to an axis-aligned rectangle (the classic four-half-plane algorithm).</summary>
internal static class SutherlandHodgman
{
    /// <summary>
    /// Clips <paramref name="polygon"/> (assumed closed - a glyph outline's own contours always are) to
    /// <paramref name="rect"/>, one half-plane (left/right/top/bottom) at a time.
    /// </summary>
    public static List<(double X, double Y)> ClipToRect(IReadOnlyList<PaintPoint> polygon, Rect rect)
    {
        var points = new List<(double X, double Y)>(polygon.Count);
        foreach (var p in polygon)
            points.Add((p.X, p.Y));

        points = ClipEdge(points, (x, _) => x >= rect.Left, (p1, p2) => Intersect(p1, p2, rect.Left, vertical: true));
        points = ClipEdge(points, (x, _) => x <= rect.Right, (p1, p2) => Intersect(p1, p2, rect.Right, vertical: true));
        points = ClipEdge(points, (_, y) => y >= rect.Top, (p1, p2) => Intersect(p1, p2, rect.Top, vertical: false));
        points = ClipEdge(points, (_, y) => y <= rect.Bottom, (p1, p2) => Intersect(p1, p2, rect.Bottom, vertical: false));
        return points;
    }

    private static List<(double X, double Y)> ClipEdge(List<(double X, double Y)> input,
        System.Func<double, double, bool> inside, System.Func<(double X, double Y), (double X, double Y), (double X, double Y)> intersect)
    {
        if (input.Count == 0)
            return input;

        var output = new List<(double X, double Y)>(input.Count);
        var previous = input[^1];
        var previousInside = inside(previous.X, previous.Y);
        foreach (var current in input)
        {
            var currentInside = inside(current.X, current.Y);
            if (currentInside)
            {
                if (!previousInside)
                    output.Add(intersect(previous, current));
                output.Add(current);
            }
            else if (previousInside)
            {
                output.Add(intersect(previous, current));
            }

            previous = current;
            previousInside = currentInside;
        }

        return output;
    }

    private static (double X, double Y) Intersect((double X, double Y) p1, (double X, double Y) p2, double boundary, bool vertical)
    {
        if (vertical)
        {
            var t = (boundary - p1.X) / (p2.X - p1.X);
            return (boundary, p1.Y + t * (p2.Y - p1.Y));
        }
        else
        {
            var t = (boundary - p1.Y) / (p2.Y - p1.Y);
            return (p1.X + t * (p2.X - p1.X), boundary);
        }
    }
}
