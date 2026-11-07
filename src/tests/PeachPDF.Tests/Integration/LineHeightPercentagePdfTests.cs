using System.Globalization;
using System.Text.RegularExpressions;

using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A percentage <c>line-height</c> is a share of the element's own <c>font-size</c> (CSS 2.1 section 10.8.1). It was resolved against the box's
    /// own height, which is 0 while its lines are laid out, so every line was 0 tall and the blocks had no height. The same rules are asserted on
    /// the box tree in <c>PeachBrackets.Engine.Tests</c> (<c>LineHeightPercentageTests</c>); these run through <see cref="PdfGenerator"/>, which is
    /// what the coverage gate measures the engine through. PDF space has its origin at the bottom, so a rectangle's top edge is <c>y + h</c>.
    /// </summary>
    public class LineHeightPercentagePdfTests
    {
        private const string Red = "1 0 0";
        private const string Lime = "0 1 0";
        private const string Blue = "0 0 1";

        private readonly record struct Rect(double X, double Y, double W, double H)
        {
            public double Top => Y + H;
        }

        private static async Task<Dictionary<string, Rect>> RenderAsync(string body)
        {
            // Text in a fixture is set in the bundled font: the platform's default one has metrics that differ between Windows, Linux and macOS.
            var generator = new PdfGenerator();
            await using (var stream = File.OpenRead(BundledFonts.LiberationSans))
                await generator.AddFontFromStream(stream);
            var family = TypefaceFixtures.FamilyNameOf(BundledFonts.LiberationSans);

            var html = $"<!DOCTYPE html><html><head><style>body {{ margin: 0; font-family: '{family}'; font-size: 14px }}</style></head><body>" +
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

        // 150% of a 14px font is 21px (15.75pt), 200% of a 20px font 40px (30pt): each block is as tall as its one line, and the next one starts
        // below it. With the old basis the blocks had no height at all and were not painted.
        [Fact]
        public async Task APercentage_IsAShareOfTheFontSize()
        {
            var r = await RenderAsync(
                "<div style='background:#f00;line-height:150%'>First</div>" +
                "<div style='background:#0f0;line-height:200%;font-size:20px'>Second</div>");

            Assert.Equal(21 * 0.75, r[Red].H, 1);
            Assert.Equal(40 * 0.75, r[Lime].H, 1);
            Assert.Equal(r[Red].Y, r[Lime].Top, 1);
        }

        // A calc() mixing lh (the parent's line-height) with a percentage is resolved when the box is laid out, not at the cascade: the 0% leaves
        // the parent's 20px.
        [Fact]
        public async Task APercentageInACalcNextToLh_IsResolvedAgainstTheFontSize()
        {
            var r = await RenderAsync("<div style='line-height:20px'><div style='background:#00f;line-height:calc(1lh + 0%)'>Text</div></div>");

            Assert.Equal(20 * 0.75, r[Blue].H, 1);
        }
    }
}
