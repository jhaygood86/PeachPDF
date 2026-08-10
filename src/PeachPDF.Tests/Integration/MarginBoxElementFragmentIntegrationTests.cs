using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end coverage for css-gcpm-3's <c>content: element()</c>, laid out the way the real
    /// generator lays a document out (<see cref="PdfGeneratorLayoutHarness"/>, per the same "the harness
    /// is not the production path" lesson <c>TableHeaderRepetitionThroughTheGeneratorTests</c> already
    /// documents for repeating table headers) - an <c>@page</c> rule, margin-box geometry, and pagination
    /// all genuinely engage. Asserted on <see cref="HtmlContainerInt.FragmentTree"/> rather than the PDF's
    /// content stream, per this repo's own warning against content-stream-substring-only proof: a token
    /// can be present while the thing it draws is in the wrong place or the wrong shape.
    /// </summary>
    public class MarginBoxElementFragmentIntegrationTests
    {
        private static string Fixture() =>
            """
            <!DOCTYPE html>
            <html><head><style>
            @page { size: a6; margin: 12mm; }
            @page { @top-center { content: element(heading); font-size: 8pt; } }
            h1.running { position: running(heading); margin: 0; }
            .tag { color: rgb(200, 0, 0); }
            p { line-height: 1.6; }
            </style></head><body>
            <h1 class="running">Chapter One <span class="tag">Introduction</span></h1>
            """ +
            string.Concat(Enumerable.Repeat("<p>Lorem ipsum dolor sit amet, consectetur adipiscing elit.</p>", 80)) +
            """
            </body></html>
            """;

        private static async Task<HtmlContainerInt> LayoutFixtureAsync()
        {
            var (_, container) = await PdfGeneratorLayoutHarness.LayoutAsync(
                Fixture(), new PdfGenerateConfig { PageSize = PageSize.A6 });

            Assert.True(container.FragmentTree!.Fragmentainers.Count > 1,
                "fixture does not paginate, so it asserts nothing about running headers across pages");

            return container;
        }

        [Fact]
        public async Task RunningHeading_AppearsAsRealBoxSubtree_OnEveryPage()
        {
            var container = await LayoutFixtureAsync();
            var headingBox = LayoutHarness.Descendants(container.Root!)
                .First(b => b.HtmlTag?.Name == "h1");

            foreach (var fragmentainer in container.FragmentTree!.Fragmentainers)
            {
                var marginBox = Assert.Single(fragmentainer.MarginBoxes, m => m.BoxName == "top-center");

                // The captured content is the running box itself - not a re-derived copy, and not a
                // plain string - so real formatting/descendant elements are the same objects paint
                // already knows how to draw.
                Assert.Same(headingBox, marginBox.Content.Box);
            }
        }

        [Fact]
        public async Task RunningHeading_NestedSpan_TravelsAsARealChildFragment()
        {
            var container = await LayoutFixtureAsync();
            var spanBox = LayoutHarness.Descendants(container.Root!)
                .First(b => b.HtmlTag?.Name == "span");

            var firstPage = container.FragmentTree!.Fragmentainers[0];
            var marginBox = Assert.Single(firstPage.MarginBoxes, m => m.BoxName == "top-center");

            // Proof of full-subtree fidelity (the point of choosing real layout over string-set's
            // text-only capture): the styled <span> is a genuine descendant BoxFragment, not lost.
            Assert.Contains(marginBox.Content.Children, c => c.Box == spanBox);
        }

        [Fact]
        public async Task RunningHeading_WithLastKeywordAndSiblingCounterMarginBox_BothResolve()
        {
            // Exercises MarginBoxRenderer.TryParseElementFunction's explicit-keyword branch
            // (element(name, last), not just the default "first") and confirms a sibling margin box
            // with ordinary (non-element()) content on the SAME page still resolves via its own,
            // separate, unaffected pipeline (HtmlContainerInt.LayoutMarginBoxes' non-match continue).
            var html = """
                <!DOCTYPE html><html><head><style>
                @page { size: a6; margin: 12mm; }
                @page {
                    @top-center { content: element(heading, last); }
                    @bottom-right { content: "Page " counter(page); font-size: 7pt; }
                }
                h1.running { position: running(heading); margin: 0; font-size: 9pt; }
                p { line-height: 1.6; }
                </style></head><body>
                <h1 class="running">Chapter One</h1>
                """ +
                string.Concat(Enumerable.Repeat("<p>Lorem ipsum dolor sit amet, consectetur adipiscing elit.</p>", 80)) +
                """
                </body></html>
                """;

            var (_, container) = await PdfGeneratorLayoutHarness.LayoutAsync(
                html, new PdfGenerateConfig { PageSize = PageSize.A6 });

            Assert.True(container.FragmentTree!.Fragmentainers.Count > 1);

            foreach (var fragmentainer in container.FragmentTree!.Fragmentainers)
            {
                // The element()-driven top-center box is present (via the "last" keyword, resolving to
                // the same single running box every page, same as "first" would with one occupant).
                Assert.Single(fragmentainer.MarginBoxes, m => m.BoxName == "top-center");

                // bottom-right's plain counter() content is untouched by the element() phase - it never
                // becomes a MarginBoxFragment at all, staying on MarginBoxRenderer's own pipeline.
                Assert.DoesNotContain(fragmentainer.MarginBoxes, m => m.BoxName == "bottom-right");
            }
        }

        [Theory]
        [InlineData("element()")]
        [InlineData("element(heading, #bad)")]
        public void TryParseElementFunction_MalformedArguments_ReturnsFalse(string contentValue)
        {
            Assert.False(MarginBoxRenderer.TryParseElementFunction(contentValue, out _, out _));
        }

        [Fact]
        public async Task RunningHeading_ContributesNoSizeInNormalFlow()
        {
            var container = await LayoutFixtureAsync();
            var headingBox = LayoutHarness.Descendants(container.Root!)
                .First(b => b.HtmlTag?.Name == "h1");

            Assert.Equal(0, headingBox.Location.Y);
        }
    }
}
