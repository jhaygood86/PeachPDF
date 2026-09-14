using PeachPDF.PdfSharpCore;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <see cref="ClipReport"/> catches the one loss class reading the finished PDF cannot see:
    /// glyphs that were DRAWN into the content stream and then truncated by a clip.
    /// <para>
    /// A reader that parses the content stream finds those glyphs and reports the document complete;
    /// a renderer that honours the clip shows only part of the word. Both are correct — the
    /// difference is not recorded in the file — so the engine, at the moment it decides to draw, is
    /// the only thing that can report it.
    /// </para>
    /// <para>
    /// Asserted through <see cref="PdfGenerator.GeneratePdf"/> rather than at paint level, because
    /// the subject is the report a caller receives, and because the collector is drained by
    /// <c>AddPdfPages</c> after the page loop — a paint-level harness would not exercise that.
    /// </para>
    /// </summary>
    public class ClipReportTests
    {
        private static async Task<ClipReport> RenderAsync(string body)
        {
            var doc = await new PdfGenerator().GeneratePdf(
                $"<html><body>{body}</body></html>",
                new PdfGenerateConfig { PageSize = PageSize.Letter });
            return doc.ClipReport;
        }

        [Fact]
        public async Task AWordTruncatedByAnOverflowHiddenBoxIsReported()
        {
            // THE CONTROL CASE, and the shape the feature is named after: a box narrower than its
            // own unbreakable content, so the word is drawn in full and then cut by the clip.
            // A change that reports nothing here is testing nothing.
            var report = await RenderAsync(
                "<div style=\"width:20px;overflow:hidden;white-space:nowrap\">Extended</div>");

            var word = Assert.Single(report.ClippedWords);
            Assert.Equal("Extended", word.Text);
            Assert.Equal(8, report.ClippedChars);
            Assert.True(word.VisibleWidth < word.DrawnWidth,
                $"a clipped word must report less visible than drawn: {word.VisibleWidth} vs {word.DrawnWidth}");
        }

        [Fact]
        public async Task OneWordDeliberately_BecauseAMultiWordRunIsADifferentFinding()
        {
            // Measured while writing this: a multi-word run in the same box is NOT a clean partial
            // clip. The later words fall entirely outside, hit the existing full-clip guard, are
            // never drawn at all, and a content-stream reader reports them missing — correctly.
            // Full-cull and partial-clip are different findings, and conflating them is how a
            // fixture ends up asserting the wrong thing.
            var report = await RenderAsync(
                "<div style=\"width:20px;overflow:hidden;white-space:nowrap\">Alpha Bravo Extended</div>");

            Assert.All(report.ClippedWords,
                w => Assert.True(w.VisibleWidth > 0, $"'{w.Text}' was never drawn, so it is a full cull, not a clip"));
        }

        [Fact]
        public async Task RepeatedAddPdfPagesCallsAccumulateTheReport()
        {
            // AddPdfPages is a repeatable public API — a caller appends more pages to an existing
            // document across several calls, each building its own container and its own report.
            // Replacing the property wholesale silently discards every earlier call's findings,
            // which is the one failure mode a report about silent loss must not have.
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.Letter };
            const string clipping =
                "<html><body><div style=\"width:20px;overflow:hidden;white-space:nowrap\">Extended</div></body></html>";

            var document = await generator.GeneratePdf(clipping, config);
            Assert.Single(document.ClipReport.ClippedWords);

            await generator.AddPdfPages(document, clipping, config);

            Assert.Equal(2, document.ClipReport.ClippedWords.Count);
            Assert.Equal(16, document.ClipReport.ClippedChars);
        }

        [Fact]
        public async Task AWordClippedOnlyVerticallyIsAlsoReported()
        {
            // A box short enough to cut a line's height but wide enough to keep the whole word is
            // the same silent loss, and is recorded — so VisibleWidth can equal DrawnWidth on a
            // reported word. Pins the boundary the XML doc describes.
            var report = await RenderAsync(
                "<div style=\"width:400px;height:4px;overflow:hidden\">Extended</div>");

            var word = Assert.Single(report.ClippedWords);
            Assert.Equal(word.DrawnWidth, word.VisibleWidth, 1);
            Assert.True(word.VisibleHeight < word.DrawnHeight,
                $"the clip was vertical, so height must have been reduced: {word.VisibleHeight} of {word.DrawnHeight}");
        }

        [Fact]
        public async Task AnOrdinaryDocumentReportsNothing()
        {
            // If this fires the report is noise rather than a signal, and the rate it produces
            // cannot be read. A plain paragraph and a plain table clip nothing.
            var report = await RenderAsync(
                "<p>A paragraph that fits.</p>"
                + "<table><tr><th>Header</th><th>Second</th></tr>"
                + "<tr><td>a value</td><td>another value</td></tr></table>");

            Assert.Empty(report.ClippedWords);
            Assert.Equal(0, report.ClippedChars);
        }

        [Fact]
        public async Task TextOverflowEllipsisIsNotReported()
        {
            // Truncation the author asked for and the reader can see, which is the opposite of the
            // silent loss this exists for.
            var report = await RenderAsync(
                "<div style=\"width:30px;overflow:hidden;white-space:nowrap;text-overflow:ellipsis\">"
                + "Extended text here</div>");

            Assert.Empty(report.ClippedWords);
        }

        [Fact]
        public async Task KeptFractionDescribesHowMuchSurvived()
        {
            // The calibration handle: what counts as material loss is a measurement over real
            // documents rather than a constant chosen here, so the geometry has to be reported
            // rather than a boolean.
            var report = await RenderAsync(
                "<div style=\"width:20px;overflow:hidden;white-space:nowrap\">Extended</div>");

            var word = Assert.Single(report.ClippedWords);
            Assert.InRange(word.KeptFraction, 0d, 1d);
            Assert.True(word.KeptFraction < 1d,
                $"a reported word lost something, so it cannot have kept all of itself: {word.KeptFraction}");
        }

        [Fact]
        public async Task AWideEnoughBoxReportsNothing_EvenWithOverflowHidden()
        {
            // The contrast case for the control above: `overflow: hidden` alone must not report —
            // only content that genuinely did not fit.
            var report = await RenderAsync(
                "<div style=\"width:400px;overflow:hidden;white-space:nowrap\">Extended</div>");

            Assert.Empty(report.ClippedWords);
        }
    }
}
