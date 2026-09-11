using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A pseudo-element is generated on an <i>element</i> (Selectors 4 §3.3), so a blanket
    /// <c>*::before</c>/<c>*::after</c> rule must not hang one off an anonymous text box — the box a raw
    /// text node became, which is not an element.
    /// </summary>
    /// <remarks>
    /// This is not cosmetic. A synthesized pseudo box leaves the text box holding <i>both</i> its own
    /// words and child boxes, and <c>CssLayoutEngine.FlowBox</c> flows a box's own words only when it has
    /// no child boxes — so the text was never positioned by an inline flow again, stayed flagged
    /// <c>CssRect.AwaitsTheNextFragmentainer</c>, and <c>FragmentEmitter</c> dropped the word entirely.
    /// Charts.css's own <c>.charts-css *::before { box-sizing: border-box }</c> is what found this: every
    /// bar value and axis label vanished from the rendered chart.
    /// </remarks>
    public class UniversalPseudoElementIntegrationTests
    {
        [Fact]
        public async Task UniversalBefore_DoesNotGenerateBoxOnAnonymousTextBox()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>*::before, *::after { box-sizing: border-box; }</style>" +
                "<span id='s'>value</span>"));

            var span = LayoutHarness.FindById(root, "s")!;
            var text = Assert.Single(span.Boxes);

            Assert.Empty(text.Boxes);
            Assert.Equal("value", Assert.Single(text.Words).Text);
        }

        [Fact]
        public async Task UniversalBefore_TextInsideNestedFlexStillEmitsItsWord()
        {
            // The Charts.css shape, reduced: a flex container whose flex item is itself a flex container
            // holding raw text, under a blanket universal pseudo-element rule. Asserted on the fragment
            // tree, because that is exactly where the word went missing — the box laid out at the right
            // size the whole time, it simply had no TextFragment to paint.
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>" +
                "  *::before, *::after { box-sizing: border-box; }" +
                "  #outer { display: flex; width: 200pt; }" +
                "  #inner { display: flex; }" +
                "</style>" +
                "<div id='outer'><span id='inner'>$42M</span></div>"));

            var inner = LayoutHarness.FindById(root, "inner")!;
            var text = Assert.Single(inner.Boxes);

            var fragment = FragmentPaintHarness.FragmentOf(container, text);
            var word = Assert.Single(fragment.Words);

            Assert.Equal("$42M", word.Word.Text);
            Assert.True(word.Rect.Width > 0);
        }

        [Fact]
        public async Task UniversalBefore_StillGeneratesBoxOnRealElement()
        {
            // The guard is "not an element", not "not matched by a universal selector": a real element
            // still gets its ::before when the rule gives it content.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>*::before { content: 'A'; }</style>" +
                "<span id='s'>value</span>"));

            var span = LayoutHarness.FindById(root, "s")!;
            var before = Assert.Single(span.Boxes, b => b.IsBeforePseudoElement);

            Assert.Equal("A", Assert.Single(before.Words).Text);
        }
    }
}
