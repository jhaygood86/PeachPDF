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
using PeachPDF.Svg;
using System;
using System.Collections.Generic;
using System.Linq;
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
        private Dictionary<CssBox, ISvgForeignContent>? _foreignContent;

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
        internal override async ValueTask MeasureWordsSize(Canvas g)
        {
            if (!_wordsSizeMeasured)
            {
                // Fetch any network/file <image> hrefs through the async resource pipeline before the
                // synchronous SvgTreeBuilder runs (in EnsureDocument). This is the first async touch
                // point for this box and runs before paint/link-collection, whose EnsureDocument calls
                // then reuse the already-built _document. A null base override resolves relative hrefs
                // against the host document base, correct for inline SVG.
                _prefetchedImages = await SvgTreeBuilder.PrefetchImageResourcesAsync(new CssBoxSvgSourceNode(this), HtmlContainer!);
                await LayoutForeignObjectsAsync(g, this);
                EnsureDocument();
                MeasureWordSpacing(g);
                _wordsSizeMeasured = true;
            }

            var (intrinsicWidth, intrinsicHeight) = SvgIntrinsicSize.Resolve(_document);
            CssLayoutEngine.MeasureIntrinsicSize(_svgWord, intrinsicWidth, intrinsicHeight);
        }

        /// <summary>The laid-out HTML of <paramref name="foreignObject"/>, or null when it has none.</summary>
        internal ISvgForeignContent? GetForeignContent(CssBox foreignObject) =>
            _foreignContent is not null && _foreignContent.TryGetValue(foreignObject, out var content) ? content : null;

        /// <summary>
        /// Lays out the HTML of every <c>&lt;foreignObject&gt;</c> in this SVG (nested <c>&lt;svg&gt;</c>s included),
        /// ahead of the synchronous scene-graph build and before <see cref="EnsureDocument"/> drops the boxes.
        /// Not descended into: HTML inside a foreignObject is not SVG, and a nested foreignObject there is
        /// laid out as part of its parent's content.
        /// </summary>
        private async ValueTask LayoutForeignObjectsAsync(Canvas g, CssBox parent)
        {
            // Copied: laying a box out temporarily reparents it.
            foreach (var child in parent.Boxes.ToArray())
            {
                var index = parent.Boxes.IndexOf(child);

                if (child.HtmlTag is null)
                    continue;

                if (child.HtmlTag.Name == "foreignObject")
                {
                    var width = SvgValueParsers.ParseLength(child.GetAttribute("width", null)) ?? 0;
                    var height = SvgValueParsers.ParseLength(child.GetAttribute("height", null)) ?? 0;

                    if (await SvgForeignObjectContent.LayoutAsync(g, child, width, height, HtmlContainer!) is { } content)
                        (_foreignContent ??= [])[child] = content;

                    // Putting the box back appends it to its parent's children; document order (z-order, and which
                    // child a <switch> picks) must not change.
                    parent.Boxes.Remove(child);
                    parent.Boxes.Insert(index, child);
                }
                else
                {
                    await LayoutForeignObjectsAsync(g, child);
                }
            }
        }

        /// <summary>
        /// The phantom word carrying this box's replaced content, whose own rectangle positions the
        /// rendered SVG within the box. Read by <c>SvgFragmentPainter</c>.
        /// </summary>
        internal CssRectSvg SvgWord => _svgWord;

        /// <summary>
        /// The built scene graph. Null only if <see cref="EnsureDocument"/> could not build one.
        /// </summary>
        internal SvgDocument? Document => _document;

        /// <summary>
        /// Builds the scene graph from this element's own children, once. Synchronous and idempotent:
        /// any network <c>&lt;image&gt;</c> hrefs it needs were prefetched during measurement.
        /// </summary>
        internal void EnsureDocument()
        {
            if (_document is not null)
                return;

            _document = SvgTreeBuilder.Build(new CssBoxSvgSourceNode(this), HtmlContainer!.Adapter, ActualColor, _prefetchedImages,
                fontPaletteValues: HtmlContainer.FontPaletteValues, fontFeatureValues: HtmlContainer.FontFeatureValues);

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
        /// The built <see cref="SvgDocument"/> and its rendered rectangle in full document space, for
        /// <c>&lt;a&gt;</c> link-annotation discovery (see <see cref="SvgRenderer.CollectLinks"/>) -
        /// deliberately not reusing the painter's own rect, which is local to whichever fragmentainer
        /// is being painted and would have to be mapped back out again.
        /// </summary>
        internal (SvgDocument Document, Rect Rect)? GetLinkSource()
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
