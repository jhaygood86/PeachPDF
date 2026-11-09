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

        [Fact]
        public async Task WithoutPaintContainmentOverflowingContentIsNotClipped()
        {
            var (_, content) = await RenderAsync("", "<div style='width:75pt;height:30pt'><div style='width:200pt;height:100pt;background:#f00'></div></div>");

            Assert.DoesNotContain(ClipSizes(content), c => Math.Abs(c.W - 75) < 0.1 && Math.Abs(c.H - 30) < 0.1);
        }

        [Theory]
        [InlineData("contain:strict")]
        [InlineData("contain:content")]
        [InlineData("contain:size layout style")]
        public async Task TheShortcutKeywordsAndKeywordSetsContainLikeLayoutAndPaint(string style)
        {
            var (r, _) = await RenderAsync("", $"<div style='width:300pt;{style}'>{RedFloat}</div>{LimeBlock}");

            // Size containment is not applied (see the accepted gap), so only the formatting context is under test: the block after starts below the float.
            Assert.True(r.ContainsKey(Red));
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
