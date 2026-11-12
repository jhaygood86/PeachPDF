using PeachBrackets.Engine.Tests.TestSupport;
using PeachBrackets.Html.Paint;
using PeachPDF.Tests.TestSupport;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// The optional accounting of what a page paint walked (<c>PEACH_PAINT_STATS</c>): a line per paint with how much of the page the paint entered and how long it took,
    /// and a note saying why a restyle was not applied in place.
    /// </summary>
    [Collection("PaintStatistics")]
    public class PaintStatisticsTests
    {
        private static async Task<ConcurrentQueue<string>> CaptureAsync(System.Func<Task> work)
        {
            var lines = new ConcurrentQueue<string>();
            PaintStatistics.Sink = lines.Enqueue;
            PaintStatistics.Enabled = true;
            try
            {
                await work();
            }
            finally
            {
                PaintStatistics.Enabled = false;
                PaintStatistics.Sink = null;
            }

            return lines;
        }

        [Fact]
        public async Task APaint_ReportsHowMuchOfThePageItEnteredAndHowLongItTook()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap("<div style='border-radius: 6pt; overflow: hidden'><div style='height: 20pt; background: rgb(10,20,200)'>text</div></div>"));

            var lines = await CaptureAsync(() =>
            {
                FragmentPaintHarness.PaintPage(container, new TestRecordingGraphics());
                return Task.CompletedTask;
            });

            // Other tests paint while the statistics are on, so their lines arrive too: one of them is this paint's.
            Assert.Contains(lines, l => System.Text.RegularExpressions.Regex.IsMatch(l,
                @"^PEACH_PAINT_STATS\(engine\) pageFragments=[1-9]\d* entered=[1-9]\d* hoistSearchNodes=\d+ escapeCheckNodes=\d+ reach=[\d.,]+ms paint=[\d.,]+ms$"));
        }

        [Fact]
        public async Task ANote_IsWrittenWhileTheStatisticsAreOn_AndNothingWhenTheyAreOff()
        {
            var lines = await CaptureAsync(() =>
            {
                PaintStatistics.Note("a restyle was not applied in place");
                return Task.CompletedTask;
            });

            Assert.Contains("PEACH_PAINT_STATS(note) a restyle was not applied in place", lines);

            var off = new ConcurrentQueue<string>();
            PaintStatistics.Sink = off.Enqueue;
            try
            {
                PaintStatistics.Note("ignored");
            }
            finally
            {
                PaintStatistics.Sink = null;
            }

            Assert.Empty(off);
        }

        [Fact]
        public async Task WithoutASink_ALineGoesToTheDebuggerAndTheFileTheEnvironmentNames()
        {
            var file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"paint-stats-{System.Guid.NewGuid():N}.txt");
            System.Environment.SetEnvironmentVariable("PEACH_PAINT_STATS_FILE", file);
            PaintStatistics.Enabled = true;
            try
            {
                PaintStatistics.Note("to the file");
            }
            finally
            {
                PaintStatistics.Enabled = false;
                System.Environment.SetEnvironmentVariable("PEACH_PAINT_STATS_FILE", null);
            }

            Assert.Contains("PEACH_PAINT_STATS(note) to the file", await System.IO.File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
            System.IO.File.Delete(file);
        }
    }
}
