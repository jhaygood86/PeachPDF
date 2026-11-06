using System.Globalization;
using System.Text.RegularExpressions;

using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A table cell that ends with a <c>&lt;br/&gt;</c> or a bottom margin is as tall as its content, so a cell that centres its content (the
    /// default) puts it where a top-aligned cell does. The engine measured the content by its words' rectangles and moved it down by half of the
    /// phantom room the line, the <c>&lt;br/&gt;</c> and the margin left. The same rules are asserted on the box tree in
    /// <c>PeachBrackets.Engine.Tests</c> (<c>CellVerticalAlignContentExtentTests</c>); these run through <see cref="PdfGenerator"/>, which is
    /// what the coverage gate measures the engine through. PDF space has its origin at the bottom, so a rectangle's top edge is <c>y + h</c>.
    /// </summary>
    public class TableCellTrailingSpacePdfTests
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

        // The text is as far below the top of its cell whether the cell centres its content or aligns it to the top, because there is no room to spare.
        // The trailing space follows a block, as in the paragraph-then-<br/> markup of an e-mail.
        [Theory]
        [InlineData("<br/>")]
        [InlineData("<div style='height:0;margin-bottom:14px'></div>")]
        [InlineData("<br/><div style='height:0;margin-bottom:14px'></div>")]
        public async Task TrailingSpaceInACell_DoesNotPushItsContentDown(string trailing)
        {
            static string Cell(string cellColour, string textColour, string verticalAlign, string trailing) =>
                $"<table cellpadding='0' cellspacing='0' style='margin-bottom:20px'><tr><td style='background:{cellColour};line-height:1.5em;vertical-align:{verticalAlign}'>" +
                $"<p style='margin:7px 0 0'><span style='background:{textColour}'>Hello</span></p>{trailing}</td></tr></table>";

            var r = await RenderAsync(Cell("#f00", "#0f0", "top", trailing) + Cell("#00f", "#ff0", "middle", trailing));

            Assert.Equal(r[Red].Top - r[Lime].Top, r[Blue].Top - r[Yellow].Top, 1);
        }
    }
}
