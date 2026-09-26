using System.Collections.Generic;

namespace PeachDrawing.Text.Outlines
{
    /// <summary>What a gradient does beyond its first and last colour stop (the <c>Extend</c> mode of a COLR colour line).</summary>
    public enum ColorExtend
    {
        /// <summary>The first and last colours continue outwards unchanged.</summary>
        Pad = 0,

        /// <summary>The gradient repeats.</summary>
        Repeat = 1,

        /// <summary>The gradient repeats, alternately reversed.</summary>
        Reflect = 2
    }

    /// <summary>One colour of a gradient.</summary>
    /// <param name="Offset">Where along the gradient the colour is, from 0 at its start to 1 at its end.</param>
    /// <param name="PaletteIndex">The colour's entry in the palette, or 0xFFFF for the colour the text is drawn in.</param>
    /// <param name="Alpha">The opacity, from 0 to 1, to apply on top of the palette entry's own alpha.</param>
    public readonly record struct ColorStop(double Offset, int PaletteIndex, double Alpha);

    /// <summary>The colours of a gradient and what it does outside them.</summary>
    public sealed class ColorLine
    {
        internal ColorLine()
        {
        }

        /// <summary>What the gradient does beyond its first and last stop.</summary>
        public ColorExtend Extend { get; internal init; }

        /// <summary>The colour stops, in order of offset.</summary>
        public IReadOnlyList<ColorStop> Stops => _readOnlyStops ??= StopList.AsReadOnly();

        private IReadOnlyList<ColorStop>? _readOnlyStops;

        internal List<ColorStop> StopList { get; } = [];
    }

    /// <summary>An affine map of the plane: <c>x' = XX*x + XY*y + DX</c> and <c>y' = YX*x + YY*y + DY</c> (the <c>Affine2x3</c> record of COLR).</summary>
    /// <param name="XX">The x coefficient of x'.</param>
    /// <param name="YX">The x coefficient of y'.</param>
    /// <param name="XY">The y coefficient of x'.</param>
    /// <param name="YY">The y coefficient of y'.</param>
    /// <param name="DX">The translation of x.</param>
    /// <param name="DY">The translation of y.</param>
    public readonly record struct Affine2x3(double XX, double YX, double XY, double YY, double DX, double DY)
    {
        /// <summary>The map that changes nothing.</summary>
        public static readonly Affine2x3 Identity = new(1, 0, 0, 1, 0, 0);

        /// <summary>Composes two maps: the result applies <paramref name="b"/> first and then <paramref name="a"/>.</summary>
        /// <param name="a">The map applied second.</param>
        /// <param name="b">The map applied first.</param>
        public static Affine2x3 Multiply(Affine2x3 a, Affine2x3 b) => new(
            a.XX * b.XX + a.XY * b.YX,
            a.YX * b.XX + a.YY * b.YX,
            a.XX * b.XY + a.XY * b.YY,
            a.YX * b.XY + a.YY * b.YY,
            a.XX * b.DX + a.XY * b.DY + a.DX,
            a.YX * b.DX + a.YY * b.DY + a.DY);
    }

    /// <summary>
    /// A node of the paint graph of a colour glyph (COLR version 1): a fill, a transform, a clip to a glyph's shape, or a way of
    /// combining other nodes. Every node is one of the sealed types that derive from this one, named as the COLR specification
    /// names its paint formats.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The translate, scale, rotate and skew formats, variable ones included, all arrive as a <see cref="PaintTransform"/> with
    /// the matrix worked out, and a format this library does not know arrives as no node at all. Variable paints are read at the
    /// default instance of the font, since variation deltas are not applied.
    /// </para>
    /// <para>
    /// The nodes are shared with every other reader of the font and must be treated as read-only. Nodes can be shared within a
    /// graph and, in a malformed font, can refer back to themselves through <see cref="PaintColrGlyph"/> and
    /// <see cref="PaintColrLayers"/>, so a walk has to bound its depth and its work.
    /// </para>
    /// </remarks>
    public abstract class ColorPaint
    {
        internal ColorPaint()
        {
        }
    }

    /// <summary>Paints a run of entries of the font's layer list, in order, each on top of the one before.</summary>
    public sealed class PaintColrLayers : ColorPaint
    {
        internal PaintColrLayers()
        {
        }

        /// <summary>The index of the first layer to paint; the paint of each is <see cref="Typeface.GetColorLayerPaint"/>.</summary>
        public int FirstLayerIndex { get; internal init; }

        /// <summary>How many layers to paint.</summary>
        public int NumLayers { get; internal init; }
    }

    /// <summary>Fills with one colour of the palette.</summary>
    public sealed class PaintSolid : ColorPaint
    {
        internal PaintSolid()
        {
        }

        /// <summary>The colour's entry in the palette, or 0xFFFF for the colour the text is drawn in.</summary>
        public int PaletteIndex { get; internal init; }

        /// <summary>The opacity, from 0 to 1, to apply on top of the palette entry's own alpha.</summary>
        public double Alpha { get; internal init; }
    }

    /// <summary>Fills with a linear gradient along a line, given by three points as the COLR specification defines it.</summary>
    public sealed class PaintLinearGradient : ColorPaint
    {
        internal PaintLinearGradient()
        {
        }

        /// <summary>The colours of the gradient.</summary>
        public ColorLine Line { get; internal init; } = null!;

        /// <summary>The x coordinate of the start point.</summary>
        public double X0 { get; internal init; }

        /// <summary>The y coordinate of the start point.</summary>
        public double Y0 { get; internal init; }

        /// <summary>The x coordinate of the end point.</summary>
        public double X1 { get; internal init; }

        /// <summary>The y coordinate of the end point.</summary>
        public double Y1 { get; internal init; }

        /// <summary>The x coordinate of the rotation point: lines of equal colour run parallel to the line from the start point to this one.</summary>
        public double X2 { get; internal init; }

        /// <summary>The y coordinate of the rotation point.</summary>
        public double Y2 { get; internal init; }
    }

    /// <summary>Fills with a radial gradient between two circles.</summary>
    public sealed class PaintRadialGradient : ColorPaint
    {
        internal PaintRadialGradient()
        {
        }

        /// <summary>The colours of the gradient.</summary>
        public ColorLine Line { get; internal init; } = null!;

        /// <summary>The x coordinate of the start circle's centre.</summary>
        public double X0 { get; internal init; }

        /// <summary>The y coordinate of the start circle's centre.</summary>
        public double Y0 { get; internal init; }

        /// <summary>The radius of the start circle.</summary>
        public double R0 { get; internal init; }

        /// <summary>The x coordinate of the end circle's centre.</summary>
        public double X1 { get; internal init; }

        /// <summary>The y coordinate of the end circle's centre.</summary>
        public double Y1 { get; internal init; }

        /// <summary>The radius of the end circle.</summary>
        public double R1 { get; internal init; }
    }

    /// <summary>Fills with a sweep (conic) gradient round a centre.</summary>
    public sealed class PaintSweepGradient : ColorPaint
    {
        internal PaintSweepGradient()
        {
        }

        /// <summary>The colours of the gradient.</summary>
        public ColorLine Line { get; internal init; } = null!;

        /// <summary>The x coordinate of the centre.</summary>
        public double CenterX { get; internal init; }

        /// <summary>The y coordinate of the centre.</summary>
        public double CenterY { get; internal init; }

        /// <summary>The angle of the start of the gradient, in radians.</summary>
        public double StartAngle { get; internal init; }

        /// <summary>The angle of the end of the gradient, in radians.</summary>
        public double EndAngle { get; internal init; }
    }

    /// <summary>Clips another paint to the shape of a glyph.</summary>
    public sealed class PaintGlyph : ColorPaint
    {
        internal PaintGlyph()
        {
        }

        /// <summary>The glyph whose outline is the clip.</summary>
        public int GlyphId { get; internal init; }

        /// <summary>What to paint inside the clip.</summary>
        public ColorPaint? Paint { get; internal init; }
    }

    /// <summary>Paints the whole colour glyph of another base glyph, which is how one colour glyph reuses another.</summary>
    public sealed class PaintColrGlyph : ColorPaint
    {
        internal PaintColrGlyph()
        {
        }

        /// <summary>The base glyph whose paint graph to paint.</summary>
        public int GlyphId { get; internal init; }
    }

    /// <summary>Paints another paint under a transform of the plane.</summary>
    public sealed class PaintTransform : ColorPaint
    {
        internal PaintTransform()
        {
        }

        /// <summary>The transform to apply to what is painted.</summary>
        public Affine2x3 Affine { get; internal init; }

        /// <summary>What to paint.</summary>
        public ColorPaint? Paint { get; internal init; }
    }

    /// <summary>Paints one paint over another with a compositing mode.</summary>
    public sealed class PaintComposite : ColorPaint
    {
        internal PaintComposite()
        {
        }

        /// <summary>The paint that is composited onto the backdrop.</summary>
        public ColorPaint? Source { get; internal init; }

        /// <summary>The compositing mode, as the COLR specification numbers its <c>CompositeMode</c> values.</summary>
        public int Mode { get; internal init; }

        /// <summary>The paint that is composited onto.</summary>
        public ColorPaint? Backdrop { get; internal init; }
    }

    /// <summary>One layer of a version 0 colour glyph: a glyph, drawn in one colour of the palette.</summary>
    /// <param name="GlyphId">The glyph to draw; its outline is filled.</param>
    /// <param name="PaletteIndex">The colour's entry in the palette, or 0xFFFF for the colour the text is drawn in.</param>
    public readonly record struct ColorLayer(int GlyphId, int PaletteIndex);
}
