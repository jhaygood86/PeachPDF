using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Layer F of mixed page orientation/size support: an ORDINARY percentage-height box (whose
    /// containing block is some other, already page-aware ancestor - not the true root) already
    /// resolves correctly with no new plumbing, since it reads that ancestor's own already-computed
    /// height. The true initial containing block (<see cref="CssLayoutEngine.GetBoxHeight"/>'s
    /// <c>box == box.ContainingBlock</c> branch) is the one case that stays deliberately pinned to a
    /// single reference - narrowed here (issue #201's vertical dimension) to page 1's own resolved
    /// band (reflecting a <c>@page :first</c> margin override) rather than the document's base
    /// configured size, while still never tracking any LATER page's own override. Same harness
    /// convention as <see cref="PerPageGeometryLayoutIntegrationTests"/>.
    /// </summary>
    public class PercentageHeightPerPageResolutionIntegrationTests
    {
        private const double SheetW = 612;
        private const double SheetH = 792;
        private const double BaseMt = 60;
        private const double BaseMb = 60;
        private const double BaseBand = SheetH - BaseMt - BaseMb; // 672

        [Fact]
        public async Task NestedAncestorWithExplicitHeight_PercentageChildResolvesAgainstAncestorNotGlobalPageSize()
        {
            // No new plumbing needed for the ordinary case: the child's containing block is the 300pt
            // div, not the true root, so it already resolves against the ancestor's own computed height
            // regardless of any @page vertical margin override elsewhere in the document.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin: 0; }
                div, p { margin: 0; }
                </style></head><body>
                <div id='ancestor' style="height: 300pt">
                <div id='child' style="height: 50%"></div>
                </div>
                </body></html>
                """);

            var child = FindById(container.Root!, "child")!;
            Assert.Equal(150, child.Size.Height, 0.5);
        }

        [Fact]
        public async Task RootIcbPercentageHeight_TracksFirstPagesOwnMarginOverride_NotTheBaseConfiguredSize()
        {
            // @page :first { margin: 0 } makes page 1's own band the full 792pt sheet, taller than the
            // base 672pt band. GetBoxHeight's `box == box.ContainingBlock` branch fires only for the
            // true root itself (CssBox.ContainingBlock returns `this` only when ParentBox is null) -
            // it bumps the ROOT's own used height to at least the ICB, which must reflect THAT page's
            // own band, per the narrowed #201 fix, rather than the document's base configured size.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin: 0; }
                body, div, p { margin: 0; }
                </style></head><body>
                <p>short content</p>
                </body></html>
                """);

            Assert.Equal(SheetH, container.Root!.Size.Height, 0.5);
            Assert.True(container.Root!.Size.Height > BaseBand, "the ICB should reflect the :first page's own (taller) band, not the base band");
        }

        [Fact]
        public async Task PageGeometryForLaterNamedOverride_DoesNotAffectSlotZerosOwnBand()
        {
            // The ICB reads PageGeometry.GetPage(0) specifically (a literal constant index) - a later
            // named page's own margin override, reached only via a forced break well after the document
            // starts, cannot retroactively change what slot 0 itself resolves to. css-page-3 §3 pins the
            // ICB to page 1 specifically, not "whichever page happens to have the most extreme override";
            // this is what makes that guarantee true at the geometry-table level GetBoxHeight reads from.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page wide { margin: 0; }
                body, div, p { margin: 0; }
                </style></head><body>
                <p>short content</p>
                <div style='page: wide'>wide section</div>
                </body></html>
                """);

            Assert.Equal(BaseBand, container.PageGeometry.GetPage(0).BandHeight, 0.5);
            // The named page's own slot DOES get the override - confirming the fixture actually
            // exercises a real, differing later override rather than one that never took effect.
            Assert.Equal(SheetH, container.PageGeometry.GetPage(1).BandHeight, 0.5);
        }

        private static async Task<HtmlContainerInt> BuildLayoutAsync(string html, double ppp = 1.0)
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = ppp };
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            container.PageSize = new RSize(
                SheetW * ppp - container.MarginLeft - container.MarginRight,
                SheetH * ppp - container.MarginTop - container.MarginBottom);
            container.Location = new RPoint(container.MarginLeft, container.MarginTop);
            container.MaxSize = new RSize(container.PageSize.Width, 0);

            var measure = XGraphics.CreateMeasureContext(
                new XSize(container.PageSize.Width, container.PageSize.Height), XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, ppp);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return container;
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            if (string.Equals(box.HtmlTag?.TryGetAttribute("id", ""), id, System.StringComparison.OrdinalIgnoreCase))
                return box;

            foreach (var child in box.Boxes)
            {
                var found = FindById(child, id);
                if (found != null) return found;
            }

            return null;
        }
    }
}
