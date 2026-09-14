using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// CSS 2.1 <see href="https://www.w3.org/TR/CSS21/visudet.html#root-height">§10.6.7</see>: a box that
    /// establishes a formatting context of its own and takes its height from content grows to cover any
    /// floating descendant whose bottom margin edge falls below its bottom content edge. That is the whole
    /// reason the <c>overflow: hidden</c> containment idiom works, and why a float, an inline-block, an
    /// absolutely-positioned box and a flex/grid item all contain their floats while an ordinary block
    /// does not.
    ///
    /// <para>
    /// No box kind did. A formatting-context root whose only content was a float came out zero-height,
    /// which is what made Acid2's <c>blockquote.first.one</c> - the second row of the face, a
    /// shrink-wrapped absolutely-positioned box around one float - invisible: its 2em black side borders
    /// had no height to be drawn over. The same box was also twice as wide as it should have been, from a
    /// separate defect in §10.3.7 shrink-to-fit, covered here too since it is the same box.
    /// </para>
    ///
    /// <para>
    /// Every expectation was measured in headless Chrome on the same markup first. The control case - an
    /// ordinary block, which must NOT contain its float - is what keeps the containment from being applied
    /// to everything.
    /// </para>
    /// </summary>
    public class FloatContainmentTests
    {
        private const string RightFloat = "<div style='float:right; width:48pt; height:12pt'></div>";
        private const string LeftFloat = "<div style='float:left; width:48pt; height:12pt'></div>";

        [Theory]
        // Every kind of formatting-context root this engine recognises contains its float - CSS 2.1
        // §9.4.1's list, as enumerated by DomUtils.EstablishesIndependentFormattingContext. (`display:
        // flow-root` is deliberately absent: it is css-display-3's own addition to that list and is not
        // implemented here at all, so asserting it would be pinning an unrelated missing feature.)
        [InlineData("position:absolute; top:0; left:300pt", 12d)]
        [InlineData("float:left", 12d)]
        [InlineData("overflow:hidden", 12d)]
        [InlineData("overflow:auto", 12d)]
        [InlineData("display:inline-block", 12d)]
        public async Task FormattingContextRoot_GrowsToContainItsFloat(string style, double expectedHeight)
        {
            var box = await MeasureAsync(style, LeftFloat);

            Assert.Equal(expectedHeight, box.Height, precision: 6);
        }

        [Fact]
        public async Task OrdinaryBlock_DoesNotContainItsFloat()
        {
            // The control. An ordinary block establishes no formatting context of its own, so the float
            // overhangs it and the block stays zero-height - which is the behaviour `clear` and the
            // `overflow: hidden` idiom exist to work around. A containment rule applied unconditionally
            // would quietly break every such layout.
            var box = await MeasureAsync("", LeftFloat);

            Assert.Equal(0, box.Height, precision: 6);
        }

        [Fact]
        public async Task ContainedFloat_TallerThanTheOtherContent_IsWhatSetsTheHeight()
        {
            // The float is the tallest thing in the box, so it alone decides the height - the case a
            // content-only height measurement gets wrong in the direction that shows.
            var box = await MeasureAsync("position:absolute; top:0; left:300pt; width:200pt",
                "<div style='float:left; width:48pt; height:40pt'></div>");

            Assert.Equal(40, box.Height, precision: 6);
        }

        [Fact]
        public async Task ContainedFloat_ShorterThanTheLineBesideIt_DoesNotShrinkTheBox()
        {
            // The mirror: the float is shorter than the line box it sits beside, so the line still wins -
            // §10.6.7 only ever increases the height. Asserted against the same box measured without the
            // float rather than a literal, so it says exactly "the float changed nothing here" and cannot
            // drift with whatever line height the machine's fallback font resolves to.
            var withFloat = await MeasureAsync("position:absolute; top:0; left:300pt", LeftFloat + "hi");
            var withoutFloat = await MeasureAsync("position:absolute; top:0; left:300pt", "hi");

            Assert.True(withoutFloat.Height > 12,
                $"the line must be taller than the float's 12pt for this to be the case under test, " +
                $"got {withoutFloat.Height}pt");
            Assert.Equal(withoutFloat.Height, withFloat.Height, precision: 6);
        }

        [Fact]
        public async Task DefiniteHeight_WinsOverTheFloatItContains()
        {
            // §10.6.3: a definite height is the used height regardless of content, float included - the
            // float overflows it rather than growing it. §10.6.7 speaks only to the auto-height cases.
            var box = await MeasureAsync("position:absolute; top:0; left:300pt; height:5pt",
                "<div style='float:left; width:48pt; height:40pt'></div>");

            Assert.Equal(5, box.Height, precision: 6);
        }

        [Fact]
        public async Task NestedFormattingContextRoot_ContainsItsOwnFloat_SoTheOuterBoxDoesNotCountItTwice()
        {
            // The inner `overflow: hidden` box already contains the float, so its own box bottom is
            // ordinary content the outer box's content height has counted. Walking into it for float
            // edges as well would be harmless here but wrong in principle, and the walk stops at it.
            var box = await MeasureAsync("position:absolute; top:0; left:300pt",
                $"<div style='overflow:hidden'>{LeftFloat}</div>");

            Assert.Equal(12, box.Height, precision: 6);
        }

        [Theory]
        // §10.3.7's shrink-to-fit: 48pt of float between 24pt borders either side. The box's own border
        // must be counted once, not twice - it used to come out 144pt.
        [InlineData("position:absolute; top:0; left:300pt", RightFloat, 96d)]
        [InlineData("position:absolute; top:0; left:300pt", LeftFloat, 96d)]
        public async Task AbsolutelyPositionedBox_ShrinkToFitCountsItsOwnBorderOnce(
            string style, string content, double expectedWidth)
        {
            var box = await MeasureAsync(style, content);

            Assert.Equal(expectedWidth, box.Width, precision: 6);
        }

        [Fact]
        public async Task AbsolutelyPositionedBox_ShrinkToFitWidth_MatchesAFloatsOnTheSameContent()
        {
            // The two are the same formula (§10.3.7 restates §10.3.5), and the float branch was already
            // right - so measuring both on identical content is a check that cannot drift with either
            // one's own expected value.
            var abs = await MeasureAsync("position:absolute; top:0; left:300pt", LeftFloat);
            var flt = await MeasureAsync("float:left", LeftFloat);

            Assert.Equal(flt.Width, abs.Width, precision: 6);
        }

        /// <summary>
        /// Lays out a single <c>#t</c> box carrying <paramref name="style"/> on top of a 2em-black-sided
        /// border, wrapping <paramref name="content"/>, and returns its border-box size in points.
        /// </summary>
        private static async Task<(double Width, double Height)> MeasureAsync(string style, string content)
        {
            var html = $"""
                <!DOCTYPE html>
                <html><body style="margin: 0; font: 12pt sans-serif">
                  <div id="t" style="border:black 24pt; border-style:none solid; {style}">{content}</div>
                </body></html>
                """;

            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var container = new HtmlContainerInt(adapter)
            {
                MarginTop = 0,
                MarginLeft = 0,
                MarginRight = 0,
                MarginBottom = 0
            };

            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);

            var byId = new Dictionary<string, CssBox>();
            Collect(container.Root!, byId);

            var box = byId["t"];
            return (box.ActualRight - box.Location.X, box.ActualBottom - box.Location.Y);
        }

        private static void Collect(CssBox box, Dictionary<string, CssBox> into)
        {
            if (box.HtmlTag?.TryGetAttribute("id") is { } id) into[id] = box;
            foreach (var child in box.Boxes) Collect(child, into);
        }
    }
}
