using PeachDrawing.Text.Shaping;
using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.Collections.Generic;

namespace PeachPDF.Tests.TestSupport
{
    /// <summary>
    /// A <see cref="TestRecordingGraphics"/> that answers <see cref="RGraphics.GetInkCrossings"/> with
    /// <b>real</b> glyph ink, measured by a real <see cref="GraphicsAdapter"/> over the same fonts layout
    /// used, while still recording every draw call the way the plain recorder does — and recording every
    /// ink query, so a test can assert which band the painter actually asked about.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>text-decoration-skip-ink</c> cannot be tested through the plain recorder: it has no font
    /// subsystem, so its inherited <c>GetInkCrossings</c> returns null and the painter correctly skips
    /// nothing — a test would pass while the feature did nothing at all. <see cref="GraphicsAdapter"/> is
    /// <c>sealed</c>, so the real implementation cannot be subclassed to add recording; delegating one
    /// method to an instance of it is what gets both halves.
    /// </para>
    /// <para>
    /// <see cref="ScriptedInk"/> replaces the real measurement for a test whose subject is the painter's
    /// own arithmetic rather than the font's outlines. It exists because a real overline sits at the
    /// font's ascent line, above every glyph's ink, so no real font can demonstrate the overline half of
    /// the feature — and a test that cannot fail is worse than none.
    /// </para>
    /// <para>
    /// The delegate adapter is built over a <see cref="XGraphics.CreateMeasureContext"/> context, which is
    /// enough: <c>GetInkCrossings</c> reads only the font's own descriptor and shaping, never the
    /// underlying page.
    /// </para>
    /// </remarks>
    internal sealed class InkAwareRecordingGraphics : TestRecordingGraphics
    {
        private readonly XGraphics _measure;
        private readonly GraphicsAdapter _real;

        internal InkAwareRecordingGraphics(PdfSharpAdapter adapter, double pixelsPerPoint = 1.0)
        {
            _measure = XGraphics.CreateMeasureContext(new XSize(595, 842), XGraphicsUnit.Point, XPageDirection.Downwards);
            _real = new GraphicsAdapter(adapter, _measure, pixelsPerPoint);
        }

        /// <summary>One <see cref="RGraphics.GetInkCrossings"/> call the painter made.</summary>
        internal sealed record InkQuery(string Text, RPoint BaselineOrigin, double BandTop, double BandBottom)
        {
            internal double BandCenter => (BandTop + BandBottom) / 2;
        }

        /// <summary>Every ink query, in the order the painter made them.</summary>
        internal List<InkQuery> InkQueries { get; } = [];

        /// <summary>How many times the painter asked for ink — 0 proves it never tried.</summary>
        internal int InkQueryCount => InkQueries.Count;

        /// <summary>
        /// When set, answers every query instead of the real font, so a test can put ink exactly where it
        /// needs it. Returning null models a font whose outlines cannot be decoded.
        /// </summary>
        internal Func<InkQuery, IReadOnlyList<RInkSpan>?>? ScriptedInk { get; set; }

        public override IReadOnlyList<RInkSpan>? GetInkCrossings(
            string str, RFont font, RPoint baselineOrigin, double bandTop, double bandBottom,
            double letterSpacing = 0, ShapeSettings? features = null)
        {
            var query = new InkQuery(str, baselineOrigin, bandTop, bandBottom);
            InkQueries.Add(query);

            return ScriptedInk is { } scripted
                ? scripted(query)
                : _real.GetInkCrossings(str, font, baselineOrigin, bandTop, bandBottom, letterSpacing, features);
        }

        public override void Dispose()
        {
            _real.Dispose();
            _measure.Dispose();
        }
    }
}
