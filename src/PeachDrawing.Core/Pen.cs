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


namespace PeachDrawing.Core
{
    /// <summary>
    /// A stroke style - used to draw graphics (lines, rectangles and paths). Plain, self-describing data
    /// (not a backend-owned handle): any <see cref="Canvas"/> backend reads a pen's properties directly
    /// to build whatever native stroke representation it needs, the same way <see cref="Brush"/>'s
    /// subtypes work. <see cref="RenderContext.GetPen(PaintColor)"/>/<see cref="RenderContext.GetPen(Brush)"/> are the
    /// only places one is constructed.
    /// </summary>
    public sealed class Pen
    {
        /// <summary>
        /// The width of this pen, in units of the graphics object used for drawing.
        /// </summary>
        public double Width { get; set; } = 1;

        /// <summary>
        /// The miter limit used when joining sharp corners of a stroked path.
        /// </summary>
        public double MiterLimit { get; set; }

        /// <summary>
        /// The style used for dashed lines drawn with this pen. Setting this to anything other than
        /// <see cref="DashStyle.Custom"/> clears <see cref="DashPattern"/> (the two are mutually
        /// exclusive ways of describing a dash - matching <see cref="SetDashPattern"/>'s existing
        /// contract, just expressed as an ordinary property now instead of a setter-only one plus a
        /// method).
        /// </summary>
        public DashStyle DashStyle
        {
            get => _dashStyle;
            set
            {
                _dashStyle = value;
                if (value != DashStyle.Custom)
                {
                    _dashPattern = [];
                    _dashOffset = 0;
                }
            }
        }
        private DashStyle _dashStyle = DashStyle.Solid;

        /// <summary>
        /// How the ends of an unclosed subpath are drawn.
        /// </summary>
        public LineCap LineCap { get; set; } = LineCap.Butt;

        /// <summary>
        /// How consecutive stroked segments are joined.
        /// </summary>
        public LineJoin LineJoin { get; set; } = LineJoin.Miter;

        /// <summary>
        /// An explicit numeric dash pattern (e.g. from SVG's <c>stroke-dasharray</c>), in the same
        /// absolute units as <see cref="Width"/>. Empty when <see cref="DashStyle"/> is not
        /// <see cref="DashStyle.Custom"/>.
        /// </summary>
        public double[] DashPattern => _dashPattern;
        private double[] _dashPattern = [];

        /// <summary>
        /// The dash pattern's starting offset (SVG's <c>stroke-dashoffset</c>), in the same units as
        /// <see cref="Width"/>.
        /// </summary>
        public double DashOffset => _dashOffset;
        private double _dashOffset;

        /// <summary>
        /// What this pen strokes with - a solid colour or a gradient, e.g. for an SVG
        /// <c>stroke="url(#gradient)"</c>. Never null once constructed via <see cref="RenderContext.GetPen(PaintColor)"/>/
        /// <see cref="RenderContext.GetPen(Brush)"/>.
        /// </summary>
        public Brush Paint { get; set; } = null!;

        /// <summary>
        /// Sets an explicit numeric dash pattern (e.g. from SVG's <c>stroke-dasharray</c>/
        /// <c>stroke-dashoffset</c>), overriding <see cref="DashStyle"/> with a custom pattern.
        /// <paramref name="pattern"/> and <paramref name="offset"/> are in the same absolute units as
        /// <see cref="Width"/> - a backend that needs a unit-normalized dash array (e.g. a GDI+-style one
        /// expressed as multiples of pen width rather than absolute lengths) converts when it reads
        /// these. An empty <paramref name="pattern"/> reverts to a solid line.
        /// </summary>
        public void SetDashPattern(double[] pattern, double offset)
        {
            if (pattern.Length == 0)
            {
                DashStyle = DashStyle.Solid;
                return;
            }

            _dashStyle = DashStyle.Custom;
            _dashPattern = pattern;
            _dashOffset = offset;
        }
    }
}
