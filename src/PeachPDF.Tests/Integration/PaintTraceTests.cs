#if DEBUG
using PeachPDF.Html.Core.Paint;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// The Debug-build paint trace (<c>FragmentPainter.TraceSink</c>): a line per page and per word the painter
    /// visits, with how much of the word the page clip leaves visible.
    /// </summary>
    public class PaintTraceTests
    {
        private const string Font = "PaintTraceSans";

        private static async Task<List<string>> TracePage0Async(string body)
        {
            var html = "<!DOCTYPE html><html><head></head>" +
                       $"<body style='margin:0;font-family:\"{Font}\";font-size:10pt;line-height:12pt'>{body}</body></html>";

            var (_, container) = await LayoutHarness.LayoutAsync(html, pageWidth: 300, pageHeight: 200, margin: 20,
                configureAdapter: adapter => BundledFonts.RegisterFont(adapter, BundledFonts.LiberationSans, Font));

            var lines = new List<string>();
            var painter = new FragmentPainter(container) { TraceSink = lines.Add };
            painter.Paint(new TestRecordingGraphics(), container.FragmentTree!.Fragmentainers[0]);

            return lines;
        }

        [Fact]
        public async Task EachPageAndEachWordIsTraced_WithThePageAndTheVisibleSize()
        {
            var lines = await TracePage0Async("<p style='margin:0'>alpha beta</p>");

            Assert.StartsWith("paintpage: 0 clip=(20.0,20.0,260.0x160.0)", lines[0]);

            var alpha = Assert.Single(lines, l => l.StartsWith("paintword: p0 alpha "));
            Assert.Contains("rect=(20.0,20.3,24.5x11.5) visible=24.5x11.5", alpha);
            Assert.EndsWith(" drawn", alpha);
            Assert.Single(lines, l => l.StartsWith("paintword: p0 beta "));
        }

        // A nowrap box 30pt wide with overflow hidden: the first word fits, the second starts inside the box
        // and runs out of it, the third starts past its edge.
        private const string ClippedLine = "<div style='width:30pt;overflow:hidden;white-space:nowrap'>aa bbbbbbbbbbbbbbbb cc</div>";

        [Fact]
        public async Task AWordTheClipLeavesNothingOf_IsTracedAsSkipped()
        {
            var lines = await TracePage0Async(ClippedLine);

            var skipped = Assert.Single(lines, l => l.StartsWith("paintword: p0 cc "));
            Assert.EndsWith(" SKIPPED", skipped);
            Assert.Contains("visible=0.0x0.0", skipped);
        }

        [Fact]
        public async Task AWordTheClipCutsInTwo_IsTracedAsDrawnWithLessThanItsWidthVisible()
        {
            var lines = await TracePage0Async(ClippedLine);

            Assert.EndsWith(" drawn", Assert.Single(lines, l => l.StartsWith("paintword: p0 aa ")));

            var cut = Assert.Single(lines, l => l.StartsWith("paintword: p0 bbbbbbbbbbbbbbbb "));
            Assert.EndsWith(" drawn", cut);

            var sizes = System.Text.RegularExpressions.Regex.Match(cut, @"rect=\([\d.]+,[\d.]+,([\d.]+)x([\d.]+)\) visible=([\d.]+)x([\d.]+)");
            Assert.True(sizes.Success, cut);

            var width = double.Parse(sizes.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            var visibleWidth = double.Parse(sizes.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
            Assert.InRange(visibleWidth, 0.5, width - 0.5);
        }

        [Fact]
        public async Task WithNoSink_NothingIsTraced()
        {
            var html = "<!DOCTYPE html><html><head></head><body><p>alpha</p></body></html>";
            var (_, container) = await LayoutHarness.LayoutAsync(html, pageWidth: 300, pageHeight: 200, margin: 20);

            var painter = new FragmentPainter(container) { TraceSink = null };

            painter.Paint(new TestRecordingGraphics(), container.FragmentTree!.Fragmentainers[0]);
        }
    }
}
#endif
