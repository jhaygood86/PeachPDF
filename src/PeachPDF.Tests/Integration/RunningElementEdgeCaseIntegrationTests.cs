using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// css-gcpm-3 <c>running()</c>/<c>element()</c> edge cases called out in the implementation plan's
    /// §6 (referenced-zero-times, in-flow-only <c>element()</c> inertness, nested running() elements).
    /// </summary>
    public class RunningElementEdgeCaseIntegrationTests
    {
        [Fact]
        public async Task ElementFunction_UnmatchedName_RendersNothing_NoException()
        {
            var html = """
                <!DOCTYPE html><html><head><style>
                @page { size: a6; margin: 12mm; }
                @page { @top-center { content: element(never-registered); } }
                </style></head><body><p>content</p></body></html>
                """;

            var (_, container) = await PdfGeneratorLayoutHarness.LayoutAsync(
                html, new PdfGenerateConfig { PageSize = PageSize.A6 });

            var fragmentainer = Assert.Single(container.FragmentTree!.Fragmentainers);
            Assert.Empty(fragmentainer.MarginBoxes);
        }

        [Fact]
        public async Task ElementFunction_UsedOutsideMarginBox_IsInert_NoException()
        {
            // content: element() is only meaningful inside a page margin box - grammar-valid everywhere
            // (like string()), semantically meaningful only where a page-aware resolver consumes it.
            var html = """
                <!DOCTYPE html><html><head><style>
                div.running { position: running(heading); }
                div.target::before { content: element(heading); }
                </style></head><body>
                <div class="running">Heading</div>
                <div class="target">Body</div>
                </body></html>
                """;

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var target = LayoutHarness.Descendants(root).First(b => b.HtmlTag?.TryGetAttribute("class") == "target");
            var beforePseudo = target.Boxes.FirstOrDefault(b => b.IsBeforePseudoElement);

            // No crash, and no text materializes - CssContentEngine has no "element" case, so it falls
            // through to its default (empty) the same way any other unrecognized function would.
            Assert.True(beforePseudo is null || string.IsNullOrEmpty(beforePseudo.Text));
        }

        [Fact]
        public async Task NestedRunningElement_InsideAnotherRunningElement_InnerNeverRegisters_NoException()
        {
            // The outer running box registers normally (its parent's LayoutBlockChildren loop reaches
            // it). But the outer box's own content is never laid out during the main pass (that's what
            // "excluded from flow" means), so the inner position:running() descendant is never reached
            // by any container's child-walk at all during the main pass - it simply never registers.
            var html = """
                <!DOCTYPE html><html><body>
                <div id="outer" style="position:running(outer-name);">
                    <div id="inner" style="position:running(inner-name);">Inner</div>
                </div>
                <p>body</p>
                </body></html>
                """;

            var (root, container) = await LayoutHarness.LayoutAsync(html);

            var entry = Assert.Single(container.RunningElements);
            Assert.Equal("outer-name", entry.Name);

            var inner = LayoutHarness.Descendants(root).First(b => b.HtmlTag?.TryGetAttribute("id") == "inner");
            Assert.Equal(0, inner.Location.Y);
        }
    }
}
