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

using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Handlers;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Svg;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// CSS box for an inline <c>&lt;svg&gt;</c> element. Mirrors <see cref="CssBoxImage"/>'s
    /// replaced-element pattern (a phantom <see cref="CssRectSvg"/> word makes it participate in
    /// normal inline flow/line-breaking/min-max-width), but takes full manual control of what its
    /// descendant boxes mean: they are never laid out or painted through the generic box pipeline -
    /// <see cref="SvgTreeBuilder"/> reads them once (via <see cref="CssBoxSvgSourceNode"/>) as a plain
    /// tag/attribute data source and converts them into an internal <see cref="SvgDocument"/> scene
    /// graph, which <see cref="SvgRenderer"/> then paints directly.
    /// </summary>
    internal sealed class CssBoxSvg : CssBox
    {
        private readonly CssRectSvg _svgWord;
        private SvgDocument? _document;
        private IReadOnlyDictionary<string, SvgTreeBuilder.SvgImageResource>? _prefetchedImages;

        public CssBoxSvg(CssBox? parent, HtmlTag tag)
            : base(parent, tag)
        {
            _svgWord = new CssRectSvg(this);
            Words.Add(_svgWord);
        }

        /// <summary>
        /// Assigns the word its width and height
        /// </summary>
        /// <param name="g">the device to use</param>
        internal override async ValueTask MeasureWordsSize(RGraphics g)
        {
            if (!_wordsSizeMeasured)
            {
                // Fetch any network/file <image> hrefs through the async resource pipeline before the
                // synchronous SvgTreeBuilder runs (in EnsureDocument). This is the first async touch
                // point for this box and runs before paint/link-collection, whose EnsureDocument calls
                // then reuse the already-built _document. A null base override resolves relative hrefs
                // against the host document base, correct for inline SVG.
                _prefetchedImages = await SvgTreeBuilder.PrefetchImageResourcesAsync(new CssBoxSvgSourceNode(this), HtmlContainer!);
                EnsureDocument();
                MeasureWordSpacing(g);
                _wordsSizeMeasured = true;
            }

            var (intrinsicWidth, intrinsicHeight) = SvgIntrinsicSize.Resolve(_document);
            CssLayoutEngine.MeasureIntrinsicSize(_svgWord, intrinsicWidth, intrinsicHeight);
        }

        /// <summary>
        /// Paints the fragment
        /// </summary>
        /// <param name="g">the device to draw to</param>
        protected override ValueTask PaintImpCore(RGraphics g)
        {
            EnsureDocument();

            var rect = CommonUtils.GetFirstValueOrDefault(Rectangles);
            var offset = RPoint.Empty;

            if (!IsFixed)
                offset = HtmlContainer!.ScrollOffset;

            rect.Offset(offset);

            var clipped = RenderUtils.ClipGraphicsByOverflow(g, this);

            PaintBackground(g, rect, true);
            BordersDrawHandler.DrawBoxBorders(g, this, rect, true, true);

            var r = _svgWord.Rectangle;
            r.Offset(offset);
            r.Height -= ActualBorderTopWidth + ActualBorderBottomWidth + ActualPaddingTop + ActualPaddingBottom;
            r.Y += ActualBorderTopWidth + ActualPaddingTop;
            r.X = Math.Floor(r.X);
            r.Y = Math.Floor(r.Y);

            // object-fit / object-position honored via the shared replaced-content renderer.
            ReplacedContentRenderer.Paint(g, r, null, _document, this);

            if (clipped)
                g.PopClip();

            return ValueTask.CompletedTask;
        }

        private void EnsureDocument()
        {
            if (_document is not null)
                return;

            _document = SvgTreeBuilder.Build(new CssBoxSvgSourceNode(this), HtmlContainer!.Adapter, ActualColor, _prefetchedImages);

            // The parser builds a real (generic) CssBox for every SVG child element so
            // SvgTreeBuilder can read tag names/attributes off them - but once the scene graph above
            // is built, those boxes must not stick around: the base CssBox layout/paint pipeline
            // (which this class does not override) treats a box with children as a container and
            // skips populating its own Rectangles/line-box bookkeeping, breaking this box's own
            // background/border/content positioning. Clearing them makes this box a true leaf (one
            // phantom word, no children), exactly like CssBoxImage.
            Boxes.Clear();
        }

        /// <summary>
        /// The built <see cref="SvgDocument"/> and its unshifted (layout-time, not paint-time
        /// scroll-offset-adjusted) rendered rectangle, for <c>&lt;a&gt;</c> link-annotation discovery
        /// (see <see cref="SvgRenderer.CollectLinks"/>) - deliberately not reusing <see cref="PaintImpCore"/>'s
        /// rect computation, since that one applies <see cref="Html.Core.HtmlContainerInt.ScrollOffset"/>,
        /// which varies per output page during pagination and would need to be un-applied again to get
        /// back to the single full-document-space rectangle link annotations need.
        /// </summary>
        internal (SvgDocument Document, RRect Rect)? GetLinkSource()
        {
            EnsureDocument();

            if (_document is null)
                return null;

            var r = _svgWord.Rectangle;
            r.Height -= ActualBorderTopWidth + ActualBorderBottomWidth + ActualPaddingTop + ActualPaddingBottom;
            r.Y += ActualBorderTopWidth + ActualPaddingTop;
            r.X = Math.Floor(r.X);
            r.Y = Math.Floor(r.Y);

            return r is { Width: > 0, Height: > 0 } ? (_document, r) : null;
        }
    }
}
