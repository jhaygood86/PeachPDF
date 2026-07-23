using PeachPDF.Html.Adapters;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Utils;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// Lays out a CSS Grid container (<c>display: grid</c> / <c>inline-grid</c>,
    /// <see href="https://www.w3.org/TR/css-grid-2/">CSS Grid Layout Module Level 1/2</see>).
    ///
    /// v1 scope (issue #232): explicit and auto track sizing (lengths, %, <c>fr</c>, <c>auto</c>,
    /// <c>min-content</c>/<c>max-content</c>, <c>minmax()</c>, <c>fit-content()</c>, <c>repeat()</c>
    /// including <c>auto-fill</c>/<c>auto-fit</c>), line-based placement with spans, the auto-placement
    /// algorithm (<c>grid-auto-flow</c>), implicit tracks, gaps, and box/content alignment. Named lines,
    /// <c>grid-template-areas</c>, subgrid, masonry, baseline alignment, and the <c>grid</c>/
    /// <c>grid-template</c> shorthands are out of scope — see docs/html-css-support.md.
    ///
    /// The engine mirrors <see cref="CssLayoutEngineFlex"/>: it resolves the container's content width via
    /// <see cref="CssLayoutEngine.GetBoxWidth"/>, lays each item out at a provisional origin, then
    /// translates it into its final cell with <see cref="CssBox.OffsetLeft"/>/<see cref="CssBox.OffsetTop"/>.
    /// </summary>
    internal sealed class CssLayoutEngineGrid
    {
        private readonly CssBox _gridBox;

        private CssLayoutEngineGrid(CssBox gridBox)
        {
            _gridBox = gridBox;
        }

        public static async ValueTask PerformLayout(RGraphics g, CssBox gridBox)
        {
            try
            {
                await new CssLayoutEngineGrid(gridBox).Layout(g);
            }
            catch (Exception ex)
            {
                gridBox.HtmlContainer?.ReportError(HtmlRenderErrorType.Layout, "Failed grid layout", ex);
            }
        }

        private async ValueTask Layout(RGraphics g)
        {
            // Container inline size — resolved exactly like any other block box's width (this also carries
            // the per-page reflow seam via GetBoxWidth).
            var containerWidth = await CssLayoutEngine.GetBoxWidth(g, _gridBox);
            _gridBox.ActualRight = _gridBox.Location.X + containerWidth + _gridBox.ActualBoxSizeIncludedWidth;

            var items = _gridBox.Boxes
                .Where(b => b.Display != CssConstants.None && !b.IsOutOfFlow
                            && (b.HtmlTag != null || !b.IsSpaceOrEmpty))
                .ToList();

            if (items.Count == 0)
            {
                _gridBox.ActualBottom = _gridBox.Location.Y + _gridBox.ActualBoxSizeIncludedHeight;
                return;
            }

            // v1 Stage 1 — a single implicit column: lay each item out as ordinary block flow (which stacks
            // items vertically via each child's own previous-sibling positioning) so the plumbing, dispatch,
            // and container sizing are exercised end-to-end before track sizing lands.
            _gridBox.ActualBottom = _gridBox.Location.Y;

            foreach (var childBox in _gridBox.Boxes)
            {
                await childBox.PerformLayout(g);
            }

            _gridBox.ActualRight = _gridBox.CalculateActualRight();

            if (_gridBox.Boxes.Any(b => !b.IsOutOfFlow))
            {
                _gridBox.ActualBottom = _gridBox.MarginBottomCollapse();
            }
        }
    }
}
