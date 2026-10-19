using PeachDrawing.Core;
using PeachPDF.Html.Core.Fragmentation;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Html.Core.Paint;
using PeachPDF.Svg;
using System.Numerics;
using System.Threading.Tasks;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// The HTML inside one <c>&lt;foreignObject&gt;</c> of an inline <c>&lt;svg&gt;</c>, laid out once as an
    /// isolated block <c>width</c> x <c>height</c> user units wide (see <see cref="LayoutAsync"/>) and
    /// frozen into a fragment tree, so painting it inside the SVG is synchronous like the rest of paint.
    /// </summary>
    internal sealed class SvgForeignObjectContent : ISvgForeignContent
    {
        private readonly HtmlContainerInt _container;
        private readonly BoxFragment _fragment;
        private readonly Rect _viewport;

        private SvgForeignObjectContent(HtmlContainerInt container, BoxFragment fragment, Rect viewport)
        {
            _container = container;
            _fragment = fragment;
            _viewport = viewport;
        }

        /// <summary>
        /// Lays <paramref name="box"/> out against a <paramref name="width"/> x <paramref name="height"/> user-unit
        /// rectangle (one user unit being one CSS pixel) and captures the result.
        /// </summary>
        internal static async ValueTask<SvgForeignObjectContent?> LayoutAsync(
            Canvas g, CssBox box, double width, double height, HtmlContainerInt container)
        {
            if (width <= 0 || height <= 0)
                return null;

            var unit = CSS.Length.PointsPerPx * g.PixelsPerPoint;
            var rect = new Rect(0, 0, width * unit, height * unit);

            await RunningElementLayout.LayoutRunningElementFor(g, box, rect, container);

            return new SvgForeignObjectContent(container, MarginBoxContentFragmentBuilder.Build(box), rect);
        }

        public void Paint(Canvas g)
        {
            // The HTML draws in layout units, which the canvas turns into points itself: a CSS pixel is PointsPerPx points.
            // The SVG's user space is one unit per CSS pixel under whatever scale its viewBox applies, so the content is
            // scaled up to make a CSS pixel one user unit - independent of PixelsPerPoint, which both sides already divide out.
            var scale = (float)(1.0 / CSS.Length.PointsPerPx);
            g.PushTransform(Matrix3x2.CreateScale(scale));

            // In the content's own (layout-unit) space, so the canvas's clip bound used to skip off-screen words agrees with it.
            g.PushClip(_viewport);
            new FragmentPainter(_container) { SuppressTagging = true }.PaintFragment(g, _fragment);
            g.PopClip();
            g.PopTransform();
        }
    }
}
