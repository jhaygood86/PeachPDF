using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using System;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Strokes a <c>text-decoration-style: wavy</c> line (css-text-decor-3 §2.2: "Draw a wavy line") -
    /// shared by <see cref="Paint.FragmentPainter"/> (HTML) and <see cref="Svg.SvgRenderer"/> (SVG),
    /// the same two callers <see cref="TextDecorationStyleMapper"/> already serves, since neither has a
    /// dash pattern that could express a wave the way it does for <c>dotted</c>/<c>dashed</c>. Building
    /// two independent wave-path implementations would be exactly the "same grammar/geometry across
    /// layers" duplication this codebase's own architecture conventions rule out - see the shared
    /// <c>CalcParser</c>/<c>BackgroundPositionGrammar</c> precedent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Geometry.</b> Inspired by, not copied from, how Blink (<c>DecorationLinePainter::MakeWave</c>)
    /// and WebKit (<c>wavyStrokeParameters</c>) build the same style: one cubic Bézier per full
    /// wavelength, both control points horizontally at the period's midpoint and offset
    /// ±<c>controlPointDistance</c> vertically from the centerline - Gecko instead draws an angular
    /// straight-line zigzag, a worse fit for PDF vector output. For a curve built this way the actual
    /// peak amplitude works out to <c>3·controlPointDistance·t(1−t)(1−2t)</c>, maximized at
    /// <c>t = ½ − √3⁄6 ≈ 0.211</c>, giving peak ≈ <c>0.289 × controlPointDistance</c>. The constants below
    /// are chosen relative to the resolved decoration thickness (unlike Blink's own physical-pixel "+1"
    /// terms, which don't have a meaningful equivalent once <see cref="RGraphics.PixelsPerPoint"/> can
    /// differ from 1) so the wave scales correctly at any thickness: a control-point distance of
    /// <c>4 × thickness</c> gives a peak amplitude of about <c>1.15 × thickness</c> - a total
    /// (peak-to-trough) vertical span of about <c>2.3 × thickness</c>, close to the "~2.5× the resolved
    /// thickness" a Chrome 141 rasterization of the same case showed (issue #1114). No test pins an
    /// exact pixel amplitude: browsers do not agree with each other on this shape either, and
    /// css-text-decor-3 leaves it entirely UA-defined.
    /// </para>
    /// <para>
    /// <b>Phase.</b> Each call starts its own wave at <c>x1</c> - i.e. phase is anchored to
    /// this <i>segment's</i> own start, not to the decoration line's start as a whole. That is the
    /// opposite of what Blink and Gecko do (both anchor to an absolute line/block origin so unrelated
    /// segments split by, say, differently-styled inline spans stay in phase - WebKit's own per-segment
    /// reset is a documented interop bug there), but it is deliberate here:
    /// <see cref="Paint.DecorationSegments.Subtract"/> already produces independent segments for
    /// <c>text-decoration-skip-ink</c> gaps and atomic-inline exclusions, each already visually separated
    /// by a real gap, so restarting phase at each segment's own start keeps every segment's wave starting
    /// and ending at a zero-crossing rather than letting an arbitrary absolute phase strand a partial
    /// wave at a gap's edge.
    /// </para>
    /// <para>
    /// <b>Units.</b> <c>x1</c>/<c>x2</c>/<c>y</c>/<c>thickness</c>
    /// are in the caller's raw, un-divided layout-space pixels - the same convention
    /// <see cref="RGraphics.DrawLine"/> uses (its own backend implementation divides by
    /// <see cref="RGraphics.PixelsPerPoint"/> before reaching the PDF). Unlike <c>DrawLine</c>,
    /// <see cref="RGraphics.DrawPath(RPen, RGraphicsPath)"/> never does that division itself - a
    /// box-geometry path has no ambient transform to divide it back down (issue #812; see
    /// <c>RenderUtils.GetRoundRect</c>'s remarks) - so this method divides every path coordinate, and the
    /// stroking pen's own width, by <see cref="RGraphics.PixelsPerPoint"/> itself, the same way
    /// <c>RenderUtils.GetRoundRect</c>/<c>FragmentPainter.BuildRingPath</c> already do.
    /// </para>
    /// </remarks>
    internal static class WavyDecorationRenderer
    {
        /// <summary>The control-point distance, relative to the resolved decoration thickness - see this
        /// class's remarks for the derivation of the resulting peak amplitude.</summary>
        private const double ControlPointDistanceFactor = 4.0;

        /// <summary>The wave's period, relative to the resolved decoration thickness.</summary>
        private const double WavelengthFactor = 4.5;

        /// <summary>
        /// How far the wave's centerline sits from <c>y</c> (the position a solid line of
        /// the same decoration would use), relative to the resolved decoration thickness - the wave
        /// grows away from the text the same direction <see cref="Paint.FragmentPainter"/>'s
        /// <c>double</c> style already does (see <see cref="GrowthDirection"/>), so a wavy underline
        /// doesn't intrude further up into descenders than a solid one would.
        /// </summary>
        private const double CenterlineOffsetFactor = 1.0;

        /// <summary>
        /// A floor on the thickness used for the wave's own proportions (wavelength, control-point
        /// distance, centerline offset) - never for the stroke's own width, which always uses the true
        /// resolved thickness. Without this floor, a declared <c>text-decoration-thickness: 0</c> would
        /// zero the wavelength and hang the per-period loop in <see cref="StrokeWavyLine"/> forever
        /// (<c>x += wavelength</c> never advancing); this also keeps the wave's shape visible rather than
        /// visually flat for any other pathologically small resolved thickness.
        /// </summary>
        private const double MinimumThickness = 0.5;

        /// <summary>
        /// Strokes one wavy decoration segment from <paramref name="x1"/> to <paramref name="x2"/>.
        /// </summary>
        /// <param name="g">the device to draw into</param>
        /// <param name="color">the decoration's resolved color</param>
        /// <param name="line">
        /// the decoration line keyword (<see cref="Keywords.Underline"/>/<see cref="Keywords.Overline"/>/
        /// <see cref="Keywords.LineThrough"/>) - decides which way the wave's centerline is offset from
        /// <paramref name="y"/>, via <see cref="GrowthDirection"/>.
        /// </param>
        /// <param name="x1">the segment's start, in the caller's raw layout-space units</param>
        /// <param name="x2">the segment's end - must be greater than <paramref name="x1"/></param>
        /// <param name="y">where a solid line of the same decoration would be drawn</param>
        /// <param name="thickness">the resolved decoration thickness, in the same raw units as <paramref name="y"/></param>
        internal static void StrokeWavyLine(RGraphics g, RColor color, string? line,
            double x1, double x2, double y, double thickness)
        {
            var t = Math.Max(thickness, MinimumThickness);
            var wavelength = WavelengthFactor * t;
            var controlPointDistance = ControlPointDistanceFactor * t;
            var centerY = y + GrowthDirection(line) * CenterlineOffsetFactor * t;

            // The last period generally overshoots x2 (wavelength rarely divides the segment width
            // evenly) - clipping, rather than hand-trimming the final Bézier, is the same trick both
            // Blink and WebKit use for the same reason.
            var verticalMargin = controlPointDistance + t;
            g.PushClip(RRect.FromLTRB(x1, centerY - verticalMargin, x2, centerY + verticalMargin));

            var ppp = g.PixelsPerPoint;
            var path = g.GetGraphicsPath();
            path.Start(x1 / ppp, centerY / ppp);

            for (var x = x1; x < x2; x += wavelength)
            {
                var next = x + wavelength;
                var midX = (x + next) / 2 / ppp;
                path.AddBezierTo(midX, (centerY + controlPointDistance) / ppp,
                                  midX, (centerY - controlPointDistance) / ppp,
                                  next / ppp, centerY / ppp);
            }

            // The stroke itself uses the true (un-clamped) thickness, not t - MinimumThickness exists to
            // keep the wave's own shape and loop step sane, not to change what the pen actually draws at
            // (an author-declared sub-hairline thickness should render exactly that thin, matching every
            // other style, even though the wave around it keeps a visible amplitude).
            var pen = g.GetPen(color);
            pen.Width = thickness / ppp;
            pen.DashStyle = RDashStyle.Solid;

            g.DrawPath(pen, path);

            path.Dispose();
            g.PopClip();
        }

        /// <summary>
        /// +1 (grows toward increasing Y - downward) for <see cref="Keywords.Underline"/> and
        /// <see cref="Keywords.LineThrough"/>, -1 (upward) for <see cref="Keywords.Overline"/> - the same
        /// direction <see cref="Paint.FragmentPainter"/>'s <c>double</c> style already grows its second
        /// stroke, measured against Chrome 141 rather than reasoned about (see
        /// <c>FragmentPainter.StrokeDecorationSegment</c>'s remarks).
        /// </summary>
        internal static double GrowthDirection(string? line) => line == Keywords.Overline ? -1 : 1;
    }
}
