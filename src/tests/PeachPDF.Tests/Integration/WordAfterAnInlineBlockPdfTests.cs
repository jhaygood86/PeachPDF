using System.Globalization;
using System.Text.RegularExpressions;

using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A word after an inline-block that overflows its line wraps to the next line (CSS Text 3 section 5.1); it used to stay on the first
    /// line, past the box's right edge. The same rule is asserted on the layout in <c>PeachBrackets.Engine.Tests</c>
    /// (<c>WordAfterAnInlineBlockWrapTests</c>); this one goes through <see cref="PdfGenerator"/>, which is what the coverage gate measures
    /// the engine through. The two spans' backgrounds show where each was put: PDF space has its origin at the bottom, so a rectangle's top
    /// edge is <c>y + h</c>.
    /// </summary>
    public class WordAfterAnInlineBlockPdfTests
    {
        private readonly record struct Rect(double X, double Y, double W, double H);

        [Fact]
        public async Task AWordAfterAnInlineBlockWiderThanItsBlock_IsOnTheNextLine()
        {
            var generator = new PdfGenerator();
            await using (var stream = File.OpenRead(BundledFonts.LiberationSans))
                await generator.AddFontFromStream(stream);
            var family = TypefaceFixtures.FamilyNameOf(BundledFonts.LiberationSans);

            var html = $"<!DOCTYPE html><html><head><style>body {{ margin: 0; font-family: '{family}'; font-size: 14px }}</style></head><body>" +
                       "<div style='width:60px'><span style='display:inline-block;background:#f00'>unbreakable</span> <span style='background:#0f0'>after</span></div></body></html>";
            var result = await generator.GeneratePdf(html, PageSize.A4);
            var file = result.Inspect();

            var rects = new Dictionary<string, Rect>();
            var fill = new Regex(@"(?<c>\S+ \S+ \S+) rg\s+(?:/\S+ gs\s+)?(?<x>\S+) (?<y>\S+) (?<w>\S+) (?<h>\S+) re\s+f", RegexOptions.CultureInvariant);

            foreach (Match m in fill.Matches(file.ContentText(file.Pages[0])))
            {
                double Num(string name) => double.Parse(m.Groups[name].Value, CultureInfo.InvariantCulture);
                rects.TryAdd(m.Groups["c"].Value, new Rect(Num("x"), Num("y"), Num("w"), Num("h")));
            }

            var box = rects["1 0 0"];
            var after = rects["0 1 0"];

            // The word's background is on the line below the inline-block's and starts at the line's left edge.
            Assert.True(Math.Abs(after.X - box.X) < 1, $"the word is at x={after.X}, not at the line start {box.X}");
            Assert.True(after.Y + after.H <= box.Y + 0.5, $"the word's top {after.Y + after.H} is not below the inline-block's bottom {box.Y}");
        }
    }
}
