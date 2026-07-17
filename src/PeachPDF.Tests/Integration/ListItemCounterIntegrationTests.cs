using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Verifies the implicit CSS "list-item" counter (CSS2.1 12.5.1 / CSS Lists Level 3) - the same
    /// counter <c>content: counter(list-item)</c> and the default (non-overridden) marker's own
    /// numbering both resolve through (<see cref="CssCounterEngine"/>) - correctly accounts for the
    /// WHATWG HTML presentational hints for &lt;ol start&gt;/&lt;ol reversed&gt;/&lt;li value&gt;, and
    /// applies to any element with a computed <c>Display: list-item</c>, not just &lt;li&gt;.
    /// </summary>
    public class ListItemCounterIntegrationTests
    {
        [Fact]
        public async Task DefaultNumbering_MatchesExplicitCounterOverride()
        {
            // The core "single source of truth" regression guard: an author ::marker override using
            // content: counter(list-item) must always agree with what the default (non-overridden)
            // marker on the same list would have shown.
            var defaultRoot = await BuildAndLayout(Wrap("<ol><li id='a'>a</li><li id='b'>b</li></ol>"));
            var overrideRoot = await BuildAndLayout(Wrap(
                "<style>li::marker { content: counter(list-item) \".\" }</style><ol><li id='a'>a</li><li id='b'>b</li></ol>"));

            var defaultA = (CssBoxMarker)FindById(defaultRoot, "a")!.Boxes.Single(b => b.IsMarkerPseudoElement);
            var defaultB = (CssBoxMarker)FindById(defaultRoot, "b")!.Boxes.Single(b => b.IsMarkerPseudoElement);
            var overrideA = (CssBoxMarker)FindById(overrideRoot, "a")!.Boxes.Single(b => b.IsMarkerPseudoElement);
            var overrideB = (CssBoxMarker)FindById(overrideRoot, "b")!.Boxes.Single(b => b.IsMarkerPseudoElement);

            Assert.Equal(defaultA.Text, overrideA.Text);
            Assert.Equal(defaultB.Text, overrideB.Text);
            Assert.Equal("1.", defaultA.Text);
            Assert.Equal("2.", defaultB.Text);
        }

        [Fact]
        public async Task OlStart_OffsetsNumbering()
        {
            var root = await BuildAndLayout(Wrap("<ol start='5'><li id='a'>a</li><li id='b'>b</li></ol>"));

            var a = (CssBoxMarker)FindById(root, "a")!.Boxes.Single(b => b.IsMarkerPseudoElement);
            var b = (CssBoxMarker)FindById(root, "b")!.Boxes.Single(b => b.IsMarkerPseudoElement);

            Assert.Equal("5.", a.Text);
            Assert.Equal("6.", b.Text);
        }

        [Fact]
        public async Task OlReversed_WithoutStart_CountsDownFromChildCount()
        {
            var root = await BuildAndLayout(Wrap("<ol reversed><li id='a'>a</li><li id='b'>b</li><li id='c'>c</li></ol>"));

            var a = (CssBoxMarker)FindById(root, "a")!.Boxes.Single(b => b.IsMarkerPseudoElement);
            var b = (CssBoxMarker)FindById(root, "b")!.Boxes.Single(b => b.IsMarkerPseudoElement);
            var c = (CssBoxMarker)FindById(root, "c")!.Boxes.Single(b => b.IsMarkerPseudoElement);

            Assert.Equal("3.", a.Text);
            Assert.Equal("2.", b.Text);
            Assert.Equal("1.", c.Text);
        }

        [Fact]
        public async Task OlReversedWithStart_CountsDownFromStart()
        {
            var root = await BuildAndLayout(Wrap("<ol reversed start='10'><li id='a'>a</li><li id='b'>b</li></ol>"));

            var a = (CssBoxMarker)FindById(root, "a")!.Boxes.Single(b => b.IsMarkerPseudoElement);
            var b = (CssBoxMarker)FindById(root, "b")!.Boxes.Single(b => b.IsMarkerPseudoElement);

            Assert.Equal("10.", a.Text);
            Assert.Equal("9.", b.Text);
        }

        [Fact]
        public async Task LiValue_ResetsNumberingAndSubsequentItemsContinue()
        {
            var root = await BuildAndLayout(Wrap(
                "<ol><li id='a'>a</li><li id='b' value='100'>b</li><li id='c'>c</li></ol>"));

            var a = (CssBoxMarker)FindById(root, "a")!.Boxes.Single(b => b.IsMarkerPseudoElement);
            var b = (CssBoxMarker)FindById(root, "b")!.Boxes.Single(b => b.IsMarkerPseudoElement);
            var c = (CssBoxMarker)FindById(root, "c")!.Boxes.Single(b => b.IsMarkerPseudoElement);

            Assert.Equal("1.", a.Text);
            Assert.Equal("100.", b.Text);
            Assert.Equal("101.", c.Text);
        }

        [Fact]
        public async Task AuthorCounterReset_OverridesOlStartPresentationalHint()
        {
            // Literal author CSS targeting list-item must win over the attribute-derived hint -
            // presentational hints are the lowest possible precedence.
            var root = await BuildAndLayout(Wrap(
                "<style>ol { counter-reset: list-item 41 }</style><ol start='5'><li id='a'>a</li></ol>"));

            var a = (CssBoxMarker)FindById(root, "a")!.Boxes.Single(b => b.IsMarkerPseudoElement);
            Assert.Equal("42.", a.Text);
        }

        [Fact]
        public async Task DisplayListItem_OnNonLiElement_GetsMarkerAndSequentialNumbering()
        {
            // Marker generation and the implicit list-item counter both key off computed
            // Display: list-item, not off the <li> tag/UA-rule selector match.
            var root = await BuildAndLayout(Wrap(
                "<div style='display: list-item; list-style-type: decimal' id='a'>a</div>" +
                "<div style='display: list-item; list-style-type: decimal' id='b'>b</div>"));

            var divA = FindById(root, "a")!;
            var divB = FindById(root, "b")!;

            var markerA = (CssBoxMarker)divA.Boxes.Single(b => b.IsMarkerPseudoElement);
            var markerB = (CssBoxMarker)divB.Boxes.Single(b => b.IsMarkerPseudoElement);

            Assert.Equal("1.", markerA.Text);
            Assert.Equal("2.", markerB.Text);
        }

        [Fact]
        public async Task DisplayListItem_MixedWithRealLiSiblings_ShareOneCounterScope()
        {
            var root = await BuildAndLayout(Wrap(
                "<ol><li id='a'>a</li><div style='display: list-item' id='b'>b</div><li id='c'>c</li></ol>"));

            var markerA = (CssBoxMarker)FindById(root, "a")!.Boxes.Single(b => b.IsMarkerPseudoElement);
            var markerB = (CssBoxMarker)FindById(root, "b")!.Boxes.Single(b => b.IsMarkerPseudoElement);
            var markerC = (CssBoxMarker)FindById(root, "c")!.Boxes.Single(b => b.IsMarkerPseudoElement);

            Assert.Equal("1.", markerA.Text);
            Assert.Equal("2.", markerB.Text);
            Assert.Equal("3.", markerC.Text);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static string Wrap(string body) =>
            $"<!DOCTYPE html><html><head></head><body>{body}</body></html>";

        private static async Task<CssBox> BuildAndLayout(string html)
        {
            var adapter = new PdfSharpAdapter();
            adapter.PixelsPerPoint = 1.0;
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return container.Root!;
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            var val = box.HtmlTag?.TryGetAttribute("id", "");
            if (val != null && val.Equals(id, System.StringComparison.OrdinalIgnoreCase))
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
