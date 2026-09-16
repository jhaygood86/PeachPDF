using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// An automatically positioned underline hangs from the alphabetic baseline the glyphs on its line
    /// are actually painted on, whatever font the decorating box itself is set in.
    /// </summary>
    /// <remarks>
    /// Every assertion here is made against the <c>DrawString</c> call that painted the text, in the same
    /// paint pass — its <c>Point</c> is the origin the glyphs were drawn from and its <c>Font</c> is the
    /// face they were drawn with, so the baseline derived from the pair is the one on the page rather
    /// than one re-derived from the box tree the painter itself reads. That also makes these assertions
    /// relative, so they do not depend on which font the host resolves for the fixture.
    /// </remarks>
    public class UnderlineBaselineIntegrationTests
    {
        /// <summary>
        /// The documented gap between the alphabetic baseline and the top edge of an automatically
        /// positioned underline of the initial thickness: one CSS pixel (see
        /// <c>docs/html-css-support.md</c>, <c>text-decoration-thickness</c>).
        /// </summary>
        private const double OneCssPixel = Length.PointsPerPx;

        /// <summary>
        /// Documents whose decorated line carries a run set in the decorating box's own font, alongside
        /// content that is not. The run in the box's own font is the one the decoration is positioned
        /// from, so the gap above it is exact; the differently-sized content is what used to drag the
        /// underline away from the line entirely.
        /// </summary>
        public static TheoryData<string, string, string> Lines => new()
        {
            // description                                    the run in the box's own font    document
            {
                "one font, the control case", "Hxg",
                "<div id='d' style='text-decoration:underline; font-size:12pt'>Hxg</div>"
            },
            {
                "a larger run beside it", "Hxg",
                "<div id='d' style='text-decoration:underline; font-size:12pt'>Hxg <span style='font-size:30pt'>BIG</span></div>"
            },
            {
                "a smaller run beside it", "Hxg",
                "<div id='d' style='text-decoration:underline; font-size:30pt'>Hxg <span style='font-size:9pt'>small</span></div>"
            },
            {
                "an inline box with a smaller run inside it", "Hxg",
                "<span id='d' style='text-decoration:underline; font-size:30pt'>Hxg <span style='font-size:9pt'>small</span></span>"
            },
            {
                // A table cell carries vertical-align: middle from the UA stylesheet, which moves the
                // cell's whole content block down without tilting the line inside it.
                "a vertically centred table cell", "Hxg",
                "<table><tr><td id='d' style='text-decoration:underline; font-size:9pt; line-height:3'>Hxg</td></tr></table>"
            },
            {
                "a cell mixing two font sizes on one line", "Qty",
                "<table><tr><td id='d' style='text-decoration:underline; font-size:9pt'>Qty <span style='font-size:22pt'>12</span></td></tr></table>"
            },
        };

        [Theory]
        [MemberData(nameof(Lines))]
        public async Task Underline_TopEdgeSitsOneCssPixelBelowThePaintedBaseline(
            string because, string word, string document)
        {
            var (_, container) = await BuildAndLayout(Wrap(document));

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintPage(container, g);

            var underline = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            var glyphs = g.DrawStringCalls.Single(c => c.Text == word);

            // Where DrawString put this run's baseline, taken from the call that drew it.
            var baseline = glyphs.Point.Y + glyphs.Font.TextBaselineOffset;
            var underlineTop = underline.Y1 - underline.Width / 2;

            Assert.True(underlineTop > baseline,
                $"the underline should sit below the baseline, not through the glyphs ({because})");
            Assert.Equal(OneCssPixel, underlineTop - baseline, 3);
        }

        /// <summary>
        /// The same rule where the decorating box shares its font with nothing on the line: the underline
        /// still belongs to the line's own text rather than floating away from it. Bounded rather than
        /// exact, because the decoration is positioned from the box's font and the glyphs are painted from
        /// theirs, so the two disagree by each face's rounding of its ascent - under a point at these
        /// sizes, against the 12pt and 15pt displacements this case used to produce.
        /// </summary>
        [Theory]
        [InlineData("<div id='d' style='text-decoration:underline; font-size:9pt'><span style='font-size:24pt'>Hxg</span></div>")]
        [InlineData("<div id='d' style='text-decoration:underline; font-size:24pt'><span style='font-size:9pt'>Hxg</span></div>")]
        [InlineData("<span id='d' style='text-decoration:underline; font-size:24pt'><span style='font-size:9pt'>Hxg</span></span>")]
        public async Task Underline_StaysWithTheLine_WhenNothingOnItIsSetInTheDecoratingBoxFont(string document)
        {
            var (_, container) = await BuildAndLayout(Wrap(document));

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintPage(container, g);

            var underline = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            var glyphs = g.DrawStringCalls.Single(c => c.Text == "Hxg");

            var baseline = glyphs.Point.Y + glyphs.Font.TextBaselineOffset;
            var underlineTop = underline.Y1 - underline.Width / 2;

            Assert.InRange(underlineTop - baseline, 0, 2 * OneCssPixel);
        }

        /// <summary>
        /// The contrast case for the theory above: a superscript is raised off the line's alphabetic
        /// baseline by <c>vertical-align</c>, so it is not what the underline hangs from. Without this,
        /// a painter that simply followed the last run it met would still satisfy every row above.
        /// </summary>
        [Fact]
        public async Task Underline_FollowsTheAlphabeticBaseline_NotASuperscriptRaisedOffIt()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='d' style='text-decoration:underline; font-size:12pt'>Base<sup>sup</sup></div>"));

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintPage(container, g);

            var underline = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            var onTheBaseline = g.DrawStringCalls.Single(c => c.Text == "Base");
            var raised = g.DrawStringCalls.Single(c => c.Text == "sup");

            var baseline = onTheBaseline.Point.Y + onTheBaseline.Font.TextBaselineOffset;
            var raisedBaseline = raised.Point.Y + raised.Font.TextBaselineOffset;
            var underlineTop = underline.Y1 - underline.Width / 2;

            // The fixture is only meaningful while the superscript really is raised off the baseline.
            Assert.True(raisedBaseline < baseline, "the superscript should be raised above the baseline");
            Assert.Equal(OneCssPixel, underlineTop - baseline, 3);
        }

        /// <summary>
        /// The bug this file exists for, stated as a difference between two documents rather than as a
        /// number: adding a differently-sized run to a decorated line must not move the underline away
        /// from the text that was already there. Before the line's own baseline was consulted, the second
        /// document's underline sat roughly a 30pt ascent lower than the first's.
        /// </summary>
        [Fact]
        public async Task Underline_DoesNotMove_WhenADifferentlySizedRunJoinsTheLine()
        {
            var alone = await MeasureGapAsync(
                "<div style='text-decoration:underline; font-size:12pt'>Hxg</div>");
            var withABiggerNeighbour = await MeasureGapAsync(
                "<div style='text-decoration:underline; font-size:12pt'>Hxg <span style='font-size:30pt'>BIG</span></div>");

            Assert.Equal(alone, withABiggerNeighbour, 3);
        }

        /// <summary>
        /// A line with nothing on its alphabetic baseline at all — every word raised by
        /// <c>vertical-align</c> — has no baseline to report, and the decoration falls back to the
        /// line's content box rather than dropping the line or drawing it at zero.
        /// </summary>
        [Fact]
        public async Task Underline_FallsBackToTheContentBox_WhenEveryWordOnTheLineIsShiftedOffTheBaseline()
        {
            var (_, container) = await BuildAndLayout(Wrap(
                "<div id='d' style='text-decoration:underline; font-size:12pt'><sup>only</sup></div>"));

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintPage(container, g);

            var underline = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            var glyphs = Assert.Single(g.DrawStringCalls);

            Assert.True(underline.Y1 > glyphs.Point.Y,
                "the underline should still be drawn below the raised run, not discarded or placed at zero");
        }

        /// <summary>The gap between the run's painted baseline and the underline's top edge.</summary>
        private static async Task<double> MeasureGapAsync(string document)
        {
            var (_, container) = await BuildAndLayout(Wrap(document));

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintPage(container, g);

            var underline = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            var glyphs = g.DrawStringCalls.Single(c => c.Text == "Hxg");

            return underline.Y1 - underline.Width / 2 - (glyphs.Point.Y + glyphs.Font.TextBaselineOffset);
        }

        private static string Wrap(string body) =>
            $"<!DOCTYPE html><html><head></head><body style='margin:0'>{body}</body></html>";

        private static async Task<(CssBox root, HtmlContainerInt container)> BuildAndLayout(string html)
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return (container.Root!, container);
        }
    }
}
