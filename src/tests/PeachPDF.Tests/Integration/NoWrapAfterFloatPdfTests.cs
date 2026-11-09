using System.Globalization;
using System.Text.RegularExpressions;

using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A word that cannot wrap (<c>white-space: nowrap</c> on its span) is shifted below the floats that leave it no room, as any other word is
    /// (CSS 2.1 section 9.5); it used to be placed past the float's right edge, off the page. The same rule is asserted on the layout in
    /// <c>PeachBrackets.Engine.Tests</c> (<c>FloatPlacementTests</c>); this one goes through <see cref="PdfGenerator"/>, which is what the
    /// coverage gate measures the engine through. The span's background shows where its line box was put: PDF space has its origin at the
    /// bottom, so a rectangle's top edge is <c>y + h</c>.
    /// </summary>
    public class NoWrapAfterFloatPdfTests
    {
        private readonly record struct Rect(double X, double Y, double W, double H)
        {
            public double Top => Y + H;
        }

        [Fact]
        public async Task ANoWrapSpanWithNoRoomBesideAFloat_IsPlacedBelowIt()
        {
            var generator = new PdfGenerator();
            await using (var stream = File.OpenRead(BundledFonts.LiberationSans))
                await generator.AddFontFromStream(stream);
            var family = TypefaceFixtures.FamilyNameOf(BundledFonts.LiberationSans);

            var html = $"<!DOCTYPE html><html><head><style>body {{ margin: 0; font-family: '{family}'; font-size: 14px }}</style></head><body>" +
                       "<div style='float:left;width:100%;height:40px;background:#0f0'>float</div>" +
                       "<p style='margin:0'><span style='white-space:nowrap;background:#f00'>unbreakable</span></p></body></html>";
            var result = await generator.GeneratePdf(html, PageSize.A4);
            var file = result.Inspect();

            var rects = new Dictionary<string, Rect>();
            var fill = new Regex(@"(?<c>\S+ \S+ \S+) rg\s+(?:/\S+ gs\s+)?(?<x>\S+) (?<y>\S+) (?<w>\S+) (?<h>\S+) re\s+f", RegexOptions.CultureInvariant);

            foreach (Match m in fill.Matches(file.ContentText(file.Pages[0])))
            {
                double Num(string name) => double.Parse(m.Groups[name].Value, CultureInfo.InvariantCulture);
                rects.TryAdd(m.Groups["c"].Value, new Rect(Num("x"), Num("y"), Num("w"), Num("h")));
            }

            var float_ = rects["0 1 0"];
            var span = rects["1 0 0"];

            // The span starts at the left edge of the content area, below the float: its top edge is no higher than the float's bottom edge.
            Assert.True(Math.Abs(span.X - float_.X) < 1, $"the span is at x={span.X}, not at the float's left edge {float_.X}");
            Assert.True(span.Top <= float_.Y + 0.5, $"the span's top {span.Top} is above the float's bottom {float_.Y}");
        }
    }
}
