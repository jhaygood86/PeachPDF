using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using static PeachPDF.Tests.TestSupport.LayoutHarness;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// CSS 2.1 §10.3.9: an atomic inline-level box occupies its own used width on the line, not the
    /// width its content happened to measure. The inline flow accumulates word widths, so a
    /// <c>width</c> on a <c>display: inline-block</c> had no effect and whatever followed sat flush
    /// against its text.
    /// <para>
    /// Reference measurements are Chrome 152 on the same shapes, rendered headless and read with
    /// <c>pdftotext -bbox</c>: a <c>width: 160px</c> box puts the following text exactly 120pt past
    /// the container's edge, and an empty <c>width: 120px</c> box with a 1px border puts it at 91.5pt.
    /// </para>
    /// </summary>
    public class InlineBlockDeclaredWidthTests
    {
        [Fact]
        public async Task ADeclaredWidthWiderThanTheContentReservesThatWidthOnTheLine()
        {
            // 160px is 120pt at 96 CSS dpi, and "Hi" is nowhere near that wide.
            var advance = await AdvanceAsync("<span style='display:inline-block;width:160px'>Hi</span>");

            Assert.Equal(120.0, advance, 1);
        }

        [Fact]
        public async Task AnEmptyBorderedInlineBlockStillTakesItsWidth()
        {
            // The checkbox-glyph shape: no content at all, so an accumulate-the-words flow gives it
            // no room whatsoever. 120px is 90pt, plus the 1px border on each side.
            var advance = await AdvanceAsync(
                "<span style='display:inline-block;width:120px;border:1px solid black'></span>");

            Assert.Equal(91.5, advance, 1);
        }

        [Fact]
        public async Task BorderBoxDoesNotCountThePaddingAndBorderTwice()
        {
            // Under box-sizing: border-box the declared width ALREADY covers padding and border, and
            // the line re-adds them either side of the content (leftSpacing/rightSpacing). Treating
            // the declared width as a content width therefore counts them twice.
            // Chrome 152: a border-box width:120px box with a 1px border advances exactly 90pt.
            var advance = await AdvanceAsync(
                "<span style='display:inline-block;width:120px;border:1px solid black;box-sizing:border-box'></span>");

            Assert.Equal(90.0, advance, 1);
        }

        [Fact]
        public async Task BorderBoxWithPaddingAlsoAdvancesItsDeclaredWidth()
        {
            // The same with padding rather than border, so the fix is not passing by only covering
            // one of the two. Chrome 152: exactly 120pt (160px).
            var advance = await AdvanceAsync(
                "<span style='display:inline-block;width:160px;padding:0 10px;box-sizing:border-box'>Hi</span>");

            Assert.Equal(120.0, advance, 1);
        }

        [Fact]
        public async Task ContentWiderThanTheDeclaredWidthOverflowsRatherThanBeingPulledBack()
        {
            // Only ever forward — which is what `overflow: visible` means. The advance must be the
            // content's, not the declared 15pt.
            var declared = await AdvanceAsync(
                "<span style='display:inline-block;width:20px'>Wider than the box</span>");
            var auto = await AdvanceAsync(
                "<span style='display:inline-block'>Wider than the box</span>");

            Assert.Equal(auto, declared, 1);
            Assert.True(declared > 15, $"content wider than the declared 15pt must not be pulled back, was {declared}");
        }

        [Fact]
        public async Task APercentageWidthIsLeftAlone()
        {
            // A percentage resolves against a containing block this line does not know, so it is
            // deliberately not handled here — asserted so the exclusion is deliberate rather than
            // discovered later.
            var percent = await AdvanceAsync("<span style='display:inline-block;width:50%'>Hi</span>");
            var auto = await AdvanceAsync("<span style='display:inline-block'>Hi</span>");

            Assert.Equal(auto, percent, 1);
        }

        /// <summary>
        /// How far the text after <paramref name="inlineBlock"/> is pushed along the line, measured
        /// from where it sits with nothing before it at all.
        /// </summary>
        private static async Task<double> AdvanceAsync(string inlineBlock)
        {
            var withBox = await AfterWordXAsync($"<div style='width:400pt'>{inlineBlock}<span id='a'>AFTER</span></div>");
            var without = await AfterWordXAsync("<div style='width:400pt'><span id='a'>AFTER</span></div>");
            return withBox - without;
        }

        private static async Task<double> AfterWordXAsync(string body)
        {
            var (root, _) = await LayoutAsync(Wrap(body));
            return Descendants(FindById(root, "a")!)
                .SelectMany(b => b.Words)
                .Select(w => w.Left)
                .DefaultIfEmpty(0)
                .Min();
        }
    }
}
