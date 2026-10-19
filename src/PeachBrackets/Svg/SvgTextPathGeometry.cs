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

using PeachDrawing.Core;
using PeachDrawing.Core.Geometry;
using System.Collections.Generic;

namespace PeachPDF.Svg
{
    /// <summary>
    /// Arc-length parameterization of a parsed path, used to lay <c>&lt;textPath&gt;</c> glyphs along a
    /// curve: <see cref="PointAtLength"/> gives the point and tangent direction at a distance along the
    /// path. The measuring itself is <see cref="PathMeasure"/>'s; this only turns the parsed
    /// <see cref="PathSegment"/> list into a path for it. A <c>MoveTo</c> starts a fresh subpath without
    /// contributing the pen-up jump to the length (SVG textPath concatenates subpath lengths).
    /// </summary>
    internal sealed class SvgTextPathGeometry
    {
        private readonly PathMeasure _measure;

        public SvgTextPathGeometry(IReadOnlyList<PathSegment> segments)
        {
            using var path = new MeasuringPath();
            SvgRenderer.AppendPathSegments(path, segments);
            _measure = new PathMeasure(path);
        }

        /// <summary>The measured path, for placing glyphs on it with <see cref="PathText"/>.</summary>
        public PathMeasure Measure => _measure;

        public double TotalLength => _measure.Length;

        public bool IsEmpty => _measure.IsEmpty;

        /// <summary>The axis-aligned bounding box of the path, curves included (default when <see cref="IsEmpty"/>).</summary>
        public Rect Bounds => IsEmpty ? default : _measure.Bounds;

        /// <summary>
        /// The point and tangent direction (degrees) at distance <paramref name="s"/> along the path.
        /// <paramref name="s"/> is clamped to <c>[0, TotalLength]</c>.
        /// </summary>
        public (double X, double Y, double TangentDegrees) PointAtLength(double s)
        {
            var sample = _measure.PointAtLength(s);
            return (sample.X, sample.Y, sample.TangentDegrees);
        }

        /// <summary>A path that only records geometry, for measuring: it draws nothing and needs no canvas.</summary>
        private sealed class MeasuringPath : GraphicsPath
        {
            public override FillMode FillMode { get; set; }

            public override GraphicsPath ClipToRect(Rect rect) => throw new System.NotSupportedException();

            public override void Dispose()
            {
            }
        }
    }
}
