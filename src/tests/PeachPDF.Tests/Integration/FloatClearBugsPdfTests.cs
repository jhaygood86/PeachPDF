using System.Globalization;
using System.Text.RegularExpressions;

using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// The float and <c>clear</c> fixes as they reach the PDF (the engine-level cases are in <c>PeachBrackets.Engine.Tests</c>): a box's rectangle is
    /// read back from the content stream by its fill colour. PDF space has its origin at the bottom, so "below" is a smaller <c>y</c> and a box's top
    /// edge is <c>y + h</c>.
    /// </summary>
    public class FloatClearBugsPdfTests
    {
        private const string Red = "1 0 0";
        private const string Lime = "0 1 0";
        private const string Blue = "0 0 1";
        private const string Yellow = "1 1 0";

        private readonly record struct Rect(double X, double Y, double W, double H)
        {
            public double Top => Y + H;
        }

        private static async Task<Dictionary<string, Rect>> RenderAsync(string body)
        {
            var generator = new PdfGenerator();
            await using (var stream = File.OpenRead(BundledFonts.LiberationSans))
                await generator.AddFontFromStream(stream);
            var family = TypefaceFixtures.FamilyNameOf(BundledFonts.LiberationSans);

            var html = $"<!DOCTYPE html><html><head><style>body {{ margin: 0; font-family: '{family}' }} div {{ box-sizing: border-box }}</style></head><body>" +
                       body + "</body></html>";
            var result = await generator.GeneratePdf(html, PageSize.A4);
            var file = result.Inspect();

            var rects = new Dictionary<string, Rect>();
            var fill = new Regex(@"(?<c>\S+ \S+ \S+) rg\s+(?:/\S+ gs\s+)?(?<x>\S+) (?<y>\S+) (?<w>\S+) (?<h>\S+) re\s+f", RegexOptions.CultureInvariant);

            foreach (Match m in fill.Matches(file.ContentText(file.Pages[0])))
            {
                double Num(string name) => double.Parse(m.Groups[name].Value, CultureInfo.InvariantCulture);
                rects.TryAdd(m.Groups["c"].Value, new Rect(Num("x"), Num("y"), Num("w"), Num("h")));
            }

            return rects;
        }

        private const string RedFloat = "<div style='float:left;width:75pt;height:45pt;background:#f00'></div>";
        private const string LimeBlock = "<div style='clear:left;height:15pt;background:#0f0'></div>";

        [Fact]
        public async Task ClearInsideAWrapperThatFollowsAFloat_GoesBelowTheFloat()
        {
            var r = await RenderAsync($"<div style='width:300pt'>{RedFloat}<div><div style='clear:left;height:15pt;background:#0f0'></div></div></div>");

            Assert.Equal(r[Red].Y, r[Lime].Top, 1);
        }

        [Fact]
        public async Task ClearedFloatInsideAWrapperThatFollowsAFloat_GoesBelowTheFloat()
        {
            var r = await RenderAsync(
                $"<div style='width:300pt'>{RedFloat}<div><div style='float:left;clear:left;width:75pt;height:15pt;background:#0f0'></div></div></div>");

            Assert.Equal(r[Red].Y, r[Lime].Top, 1);
            Assert.Equal(r[Red].X, r[Lime].X, 1);
        }

        [Fact]
        public async Task BrClearAll_PutsTheNextBlockBelowTheFloat()
        {
            var r = await RenderAsync(
                $"<div style='width:300pt'>{RedFloat}<div style='height:15pt;background:#00f'></div><br clear='all'><div style='height:15pt;background:#0f0'></div></div>");

            Assert.Equal(r[Red].Y, r[Lime].Top, 1);
        }

        [Fact]
        public async Task BrClearInsideAnInline_PutsTheNextLineBelowTheFloat()
        {
            var r = await RenderAsync(
                $"<div style='width:300pt'>{RedFloat}<span>x<br clear='left'>y</span><div style='height:15pt;background:#0f0'></div></div>");

            Assert.True(r[Lime].Top <= r[Red].Y - 5, "the line after the break is below the float, so the block after it is further down");
        }

        [Fact]
        public async Task FloatAfterABlockInAnInline_IsPlacedAfterTheBlock()
        {
            var r = await RenderAsync(
                "<div style='width:300pt'><span><div style='height:15pt;background:#00f'></div>" +
                "<div><div style='clear:both;height:15pt;background:#0f0'></div></div>" +
                "<div style='float:left;width:75pt;height:45pt;background:#f00'></div></span></div>");

            Assert.Equal(r[Blue].Y, r[Lime].Top, 1);
            Assert.Equal(r[Lime].Y, r[Red].Top, 1);
        }

        [Theory]
        [InlineData("<fieldset style='margin:0;padding:0;border:0'>", "</fieldset>")]
        [InlineData("<button style='display:block;margin:0;padding:0;border:0;width:200pt'>", "</button>")]
        public async Task FieldsetAndBlockButton_ContainTheirFloat(string open, string close)
        {
            var r = await RenderAsync($"<div style='width:300pt'>{open}{RedFloat}{close}<div style='height:15pt;background:#0f0'></div></div>");

            Assert.Equal(r[Red].Y, r[Lime].Top, 1);
        }

        [Fact]
        public async Task LeftFloatGoesBesideTheClearedFloatAsSoonAsTheRightFloatEnds()
        {
            var r = await RenderAsync(
                "<div style='width:300pt'>" +
                "<div style='float:left;width:75pt;height:22.5pt;background:#f00'></div>" +
                "<div style='float:right;width:187.5pt;height:37.5pt;background:#00f'></div>" +
                "<div style='float:left;clear:left;width:75pt;height:22.5pt;background:#0f0'></div>" +
                "<div style='float:left;width:75pt;height:22.5pt;background:#ff0'></div></div>");

            Assert.Equal(r[Blue].Y, r[Yellow].Top, 1);
            Assert.True(r[Yellow].X > r[Lime].X);
        }

        [Fact]
        public async Task RightFloatGoesBesideTheClearedFloatAsSoonAsTheLeftFloatEnds()
        {
            var r = await RenderAsync(
                "<div style='width:300pt'>" +
                "<div style='float:right;width:75pt;height:22.5pt;background:#f00'></div>" +
                "<div style='float:left;width:187.5pt;height:37.5pt;background:#00f'></div>" +
                "<div style='float:right;clear:right;width:75pt;height:22.5pt;background:#0f0'></div>" +
                "<div style='float:right;width:75pt;height:22.5pt;background:#ff0'></div></div>");

            Assert.Equal(r[Blue].Y, r[Yellow].Top, 1);
            Assert.True(r[Yellow].X < r[Lime].X);
        }
    }
}
