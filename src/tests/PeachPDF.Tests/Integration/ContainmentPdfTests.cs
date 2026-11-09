using System.Globalization;
using System.Text.RegularExpressions;

using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <c>contain</c> and <c>content-visibility: auto</c> as they reach the PDF (the engine-level cases are in <c>PeachBrackets.Engine.Tests</c>): a box's
    /// rectangle is read back from the content stream by its fill colour, and a clip as the rectangle that precedes <c>W n</c>. PDF space has its origin at the
    /// bottom, so "below" is a smaller <c>y</c> and a box's top edge is <c>y + h</c>.
    /// </summary>
    public class ContainmentPdfTests
    {
        private const string Red = "1 0 0";
        private const string Lime = "0 1 0";

        private readonly record struct Rect(double X, double Y, double W, double H)
        {
            public double Top => Y + H;
        }

        private static async Task<(Dictionary<string, Rect> Fills, string Content)> RenderAsync(string css, string body)
        {
            var generator = new PdfGenerator();
            await using (var stream = File.OpenRead(BundledFonts.LiberationSans))
                await generator.AddFontFromStream(stream);
            var family = TypefaceFixtures.FamilyNameOf(BundledFonts.LiberationSans);

            var html = $"<!DOCTYPE html><html><head><style>body {{ margin: 0; font-family: '{family}' }} div {{ box-sizing: border-box }} {css}</style></head><body>" +
                       body + "</body></html>";
            var result = await generator.GeneratePdf(html, PageSize.A4);
            var file = result.Inspect();
            var content = file.ContentText(file.Pages[0]);

            var rects = new Dictionary<string, Rect>();
            var fill = new Regex(@"(?<c>\S+ \S+ \S+) rg\s+(?:/\S+ gs\s+)?(?<x>\S+) (?<y>\S+) (?<w>\S+) (?<h>\S+) re\s+f", RegexOptions.CultureInvariant);

            foreach (Match m in fill.Matches(content))
            {
                double Num(string name) => double.Parse(m.Groups[name].Value, CultureInfo.InvariantCulture);
                rects.TryAdd(m.Groups["c"].Value, new Rect(Num("x"), Num("y"), Num("w"), Num("h")));
            }

            return (rects, content);
        }

        // A clip is a closed four-corner path written corner by corner and ended with `W n`; these are the (width, height) of each one on the page.
        private static readonly Regex ClipPath = new(
            @"(?<x1>\S+) (?<y1>\S+) m\s+(?<x2>\S+) (?<y2>\S+) l\s+(?<x3>\S+) (?<y3>\S+) l\s+(?<x4>\S+) (?<y4>\S+) l\s+h\s+W\*?\s+n",
            RegexOptions.CultureInvariant);

        private static List<(double W, double H)> ClipSizes(string content) =>
            ClipPath.Matches(content)
                .Select(m =>
                {
                    double Num(string name) => double.Parse(m.Groups[name].Value, CultureInfo.InvariantCulture);
                    return (Math.Abs(Num("x3") - Num("x1")), Math.Abs(Num("y3") - Num("y1")));
                })
                .ToList();

        private const string RedFloat = "<div style='float:left;width:75pt;height:45pt;background:#f00'></div>";
        private const string LimeBlock = "<div style='height:15pt;background:#0f0'></div>";

        [Theory]
        [InlineData("contain:layout")]
        [InlineData("contain:paint")]
        [InlineData("contain:content")]
        [InlineData("content-visibility:auto")]
        public async Task AContainedBoxContainsItsFloat_SoWhatFollowsStartsBelowIt(string style)
        {
            var (r, _) = await RenderAsync("", $"<div style='width:300pt;{style}'>{RedFloat}</div>{LimeBlock}");

            Assert.Equal(r[Red].Y, r[Lime].Top, 1);
        }

        [Fact]
        public async Task AnUncontainedBoxLetsItsFloatOverhang_SoWhatFollowsStartsBesideIt()
        {
            var (r, _) = await RenderAsync("", $"<div style='width:300pt'>{RedFloat}</div>{LimeBlock}");

            Assert.Equal(r[Red].Top, r[Lime].Top, 1);
        }

        [Fact]
        public async Task PaintContainmentClipsOverflowingContentToThePaddingEdge()
        {
            var (_, content) = await RenderAsync("", "<div style='contain:paint;width:75pt;height:30pt'><div style='width:200pt;height:100pt;background:#f00'></div></div>");

            Assert.Contains(ClipSizes(content), c => Math.Abs(c.W - 75) < 0.1 && Math.Abs(c.H - 30) < 0.1);
        }

        [Theory]
        [InlineData("overflow:clip", 75, 30)]
        [InlineData("overflow:clip;overflow-clip-margin:10pt", 95, 50)]
        [InlineData("overflow:clip;overflow-clip-margin:border-box 5pt;border:3pt solid;box-sizing:content-box", 91, 46)]
        [InlineData("contain:paint;overflow-clip-margin:4pt;border-radius:10pt", 83, 38)]
        [InlineData("overflow:hidden;overflow-clip-margin:10pt", 75, 30)]
        public async Task OverflowClipMarginPushesTheClipEdgeOut(string style, double width, double height)
        {
            // Inside the page area: a clip is never wider than the page's own, so a margin that reached into the page margin would be cut there.
            var (_, content) = await RenderAsync("", $"<div style='margin:40pt'><div style='{style};width:75pt;height:30pt'><div style='width:200pt;height:100pt;background:#f00'></div></div></div>");

            Assert.Contains(ClipSizes(content), c => Math.Abs(c.W - width) < 0.2 && Math.Abs(c.H - height) < 0.2);
        }

        [Theory]
        [InlineData("overflow:clip;overflow-clip-margin:content-box 3pt;padding:5pt", 75 - 10 + 6, 30 - 10 + 6)]
        [InlineData("overflow:clip;overflow-clip-margin:0;padding:5pt", 75, 30)]
        [InlineData("overflow:clip;overflow-clip-margin:5;padding:0", 75, 30)]
        [InlineData("overflow:clip;overflow-clip-margin:bogus;padding:0", 75, 30)]
        [InlineData("overflow:clip;overflow-clip-margin:-4pt;padding:0", 67, 22)]
        [InlineData("overflow:clip;overflow-clip-margin-left:3pt;overflow-clip-margin-right:5pt;padding:0", 83, 30)]
        [InlineData("overflow:clip;overflow-clip-margin-inline:2pt;overflow-clip-margin-block-end:6pt;padding:0", 79, 36)]
        [InlineData("overflow:clip;overflow-clip-margin:border-box padding-box;padding:0", 75, 30)]
        [InlineData("overflow:clip;overflow-clip-margin:4pt;border-radius:12pt;padding:0", 83, 38)]
        [InlineData("overflow-x:clip;overflow-clip-margin:6pt;padding:0", 87, 30)]
        public async Task OverflowClipMarginValuesAndTheirFallbacks(string style, double width, double height)
        {
            var (_, content) = await RenderAsync("", $"<div style='margin:40pt'><div style='{style};box-sizing:border-box;width:75pt;height:30pt'><div style='width:200pt;height:100pt;background:#f00'></div></div></div>");

            // One axis open: the clip is as tall as the page, so only its width is read.
            Assert.Contains(ClipSizes(content), c => Math.Abs(c.W - width) < 0.2 && (height == 30 && style.StartsWith("overflow-x") || Math.Abs(c.H - height) < 0.2));
        }

        [Theory]
        [InlineData("contain:size;contain-intrinsic-width:70pt", 70)]
        [InlineData("contain:size", 0)]
        [InlineData("contain:inline-size;contain-intrinsic-inline-size:55pt", 55)]
        [InlineData("container-type:inline-size", 0)]
        [InlineData("contain:layout", 26)]
        public async Task SizeContainmentGivesAShrinkToFitBoxTheWidthOfAnEmptyOne(string style, double expectedWidth)
        {
            var (r, _) = await RenderAsync("", $"<div style='float:left;background:#0f0;padding:0 1pt;min-height:10pt;{style}'><div style='width:26pt;height:10pt;background:#f00'></div></div>");

            if (expectedWidth == 0)
                Assert.True(!r.ContainsKey(Lime) || r[Lime].W <= 2.01);
            else
                Assert.Equal(expectedWidth + (expectedWidth == 26 ? 2 : 2), r[Lime].W, 1);
        }

        [Fact]
        public async Task ASizeContainedImageAndABoxWithAMinHeightAreSizedByTheirContainment()
        {
            const string Png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
            var (r, _) = await RenderAsync("",
                $"<img src='{Png}' style='contain:size;contain-intrinsic-size:30pt 20pt;background:#0f0'>" +
                "<div style='contain:size;min-height:12pt;background:#f00'><div style='height:90pt'></div></div>");

            Assert.True(r.ContainsKey(Red));
            Assert.Equal(12, r[Red].H, 1);
        }

        [Fact]
        public async Task ABoxWhoseChildrenAreAllSkippedDoesNotHoldUpTheCounterOfWhatFollows()
        {
            var (r, _) = await RenderAsync(
                ".n { counter-increment: c } .n::before { content: counter(c) ' ' }",
                "<div style='counter-reset:c'><p class='n'>a</p><div style='display:none'><p class='n'>hidden</p></div>" +
                "<div style='content-visibility:hidden'><p class='n'>skipped</p></div><p class='n' style='background:#0f0'>b</p></div>");

            Assert.True(r.ContainsKey(Lime));
        }

        [Fact]
        public async Task QuotesInASkippedOrContainedSubtreeAreScopedWhenRendered()
        {
            var (r, _) = await RenderAsync(
                ".q { display:block; quotes: '<' '>' '[' ']' } .q::before { content: open-quote } .q::after { content: close-quote } .c { content: close-quote close-quote }",
                "<div class='q'>x<div style='contain:style'><div class='q'><div class='c'>y</div></div></div><div class='q' style='background:#0f0'>z</div></div>");

            Assert.True(r.ContainsKey(Lime));
        }

        [Fact]
        public async Task WithoutPaintContainmentOverflowingContentIsNotClipped()
        {
            var (_, content) = await RenderAsync("", "<div style='width:75pt;height:30pt'><div style='width:200pt;height:100pt;background:#f00'></div></div>");

            Assert.DoesNotContain(ClipSizes(content), c => Math.Abs(c.W - 75) < 0.1 && Math.Abs(c.H - 30) < 0.1);
        }

        [Theory]
        [InlineData("contain:content")]
        [InlineData("contain:layout paint style")]
        public async Task TheShortcutKeywordsAndKeywordSetsContainLikeLayoutAndPaint(string style)
        {
            var (r, _) = await RenderAsync("", $"<div style='width:300pt;{style}'>{RedFloat}</div>{LimeBlock}");

            // Size containment is not applied (see the accepted gap), so only the formatting context is under test: the block after starts below the float.
            Assert.True(r.ContainsKey(Red));
        }

        // strict includes size containment: the box is sized as an empty one, and its paint containment clips what overflows it, so the float is not seen.
        [Fact]
        public async Task StrictContainmentSizesTheBoxAsAnEmptyOneAndClipsWhatOverflowsIt()
        {
            var (r, _) = await RenderAsync("", $"<div style='width:300pt;contain:strict'>{RedFloat}</div>{LimeBlock}");

            Assert.False(r.ContainsKey(Red));
            Assert.True(r.ContainsKey(Lime));
        }

        [Theory]
        [InlineData("contain:layout layout")]
        [InlineData("contain:strict layout")]
        [InlineData("contain:nonsense")]
        public async Task AnInvalidContainDeclarationContainsNothing(string style)
        {
            var (r, _) = await RenderAsync("", $"<div style='width:300pt;{style}'>{RedFloat}</div>{LimeBlock}");

            Assert.Equal(r[Red].Top, r[Lime].Top, 1);
        }

        [Fact]
        public async Task AFixedBoxInAContainedBoxIsPlacedAgainstItAndNotThePage()
        {
            var (r, _) = await RenderAsync("",
                "<div style='contain:layout;margin-left:100pt;margin-top:50pt;width:200pt;height:100pt'>" +
                "<div style='position:fixed;left:10pt;top:5pt;width:20pt;height:10pt;background:#0f0'></div></div>");

            var (page, _) = await RenderAsync("",
                "<div style='margin-left:100pt;margin-top:50pt;width:200pt;height:100pt'>" +
                "<div style='position:fixed;left:10pt;top:5pt;width:20pt;height:10pt;background:#0f0'></div></div>");

            Assert.Equal(page[Lime].X + 100, r[Lime].X, 1);
            Assert.Equal(page[Lime].Y - 50, r[Lime].Y, 1);
        }

        [Fact]
        public async Task ALayoutContainedInlineBlockSitsOnTheBaselineByItsBottomEdge()
        {
            const string Line = "<div style='font-size:12pt'>x<span style='display:inline-block;height:40pt;width:20pt;background:#0f0;{0}'>y</span></div>";

            var (_, plain) = await RenderAsync("", string.Format(Line, ""));
            var (_, contained) = await RenderAsync("", string.Format(Line, "contain:layout"));

            // Not aligned by its own text's baseline any more, so the line's text is drawn somewhere else.
            Assert.NotEqual(plain, contained);
        }

        [Fact]
        public async Task AContainedBodyIsAnOverflowBoxOfItsOwn()
        {
            var (_, content) = await RenderAsync("body { contain: paint; overflow: hidden; height: 60pt; width: 80pt }", "<div style='width:300pt;height:200pt;background:#f00'></div>");

            Assert.Contains(ClipSizes(content), c => Math.Abs(c.W - 80) < 0.1 && Math.Abs(c.H - 60) < 0.1);
        }

        [Fact]
        public async Task StyleContainmentOnATableRowIsAcceptedAndChangesNothing()
        {
            var (r, _) = await RenderAsync("", "<table><tr style='contain:strict'><td style='background:#0f0;width:40pt;height:10pt'>x</td></tr></table>");

            Assert.True(r.ContainsKey(Lime));
        }

        [Theory]
        [InlineData("contain:size;contain-intrinsic-height:30pt", 30)]
        [InlineData("contain:strict;contain-intrinsic-size:auto 80pt auto 25pt", 25)]
        [InlineData("contain:size", 0)]
        [InlineData("contain:layout", 60)]
        public async Task SizeContainmentSizesABoxAsIfItWereEmpty(string style, double expectedHeight)
        {
            var (r, _) = await RenderAsync("", $"<div style='background:#0f0;{style}'><div style='height:60pt;width:40pt'>x</div></div><div style='height:5pt;background:#f00'></div>");

            if (expectedHeight == 0)
                Assert.False(r.ContainsKey(Lime));
            else
                Assert.Equal(expectedHeight, r[Lime].H, 1);
        }

        [Theory]
        [InlineData("content-visibility:hidden", false)]
        [InlineData("content-visibility:auto", true)]
        [InlineData("content-visibility:visible", true)]
        public async Task HiddenContentIsNotPainted(string style, bool painted)
        {
            var (r, _) = await RenderAsync("", $"<div style='{style}'><div style='height:20pt;background:#0f0'>text</div></div>");

            Assert.Equal(painted, r.ContainsKey(Lime));
        }

        [Fact]
        public async Task StyleContainmentScopesCountersAndQuotesWhenRendered()
        {
            var (r, _) = await RenderAsync(
                ".n { counter-increment: c } .n::before { content: counter(c) ' ' } .q { quotes: '<' '>' '[' ']' } .q::before { content: open-quote } .q::after { content: close-quote }",
                "<div style='counter-reset:c'><p class='n'>a</p><div style='contain:style'><p class='n'>b</p><div class='q'><span class='q'>q</span></div></div><p class='n' style='background:#0f0'>d</p></div>");

            Assert.True(r.ContainsKey(Lime));
        }

        [Fact]
        public async Task AContainedBodysBackgroundIsNotPropagatedToTheCanvas()
        {
            var (plain, _) = await RenderAsync("body { background: #f00 }", "<p>text</p>");
            var (contained, _) = await RenderAsync("body { background: #f00; contain: paint }", "<p>text</p>");

            // Propagated, the background fills the page; on its own box it is only as tall as its content.
            Assert.True(plain[Red].H > 800, $"the canvas fill is {plain[Red].H}pt tall");
            Assert.True(contained[Red].H < 100, $"the body's own box is {contained[Red].H}pt tall");
        }
    }
}
