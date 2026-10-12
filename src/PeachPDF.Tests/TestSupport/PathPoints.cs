using PeachDrawing.Core;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Tests.TestSupport
{
    /// <summary>Reads back the geometry recorded in a <see cref="GraphicsPath"/> as a flat point list.</summary>
    internal static class PathPoints
    {
        /// <summary>
        /// Every point of the path in drawing order: each contour's start, then for each segment the end of a line or
        /// the two control points and end of a curve.
        /// </summary>
        public static PaintPoint[] Of(GraphicsPath path)
        {
            var points = new List<PaintPoint>();
            foreach (var contour in path.GetCurveContours())
            {
                points.Add(contour.Start);
                foreach (var command in contour.Commands)
                {
                    if (command.Kind == PathCommandKind.Cubic)
                    {
                        points.Add(command.Control1);
                        points.Add(command.Control2);
                    }

                    points.Add(command.End);
                }
            }

            return points.ToArray();
        }

        /// <summary>The number of separate contours in the path.</summary>
        public static int ContourCount(GraphicsPath path) => path.GetCurveContours().Count;
    }
}
