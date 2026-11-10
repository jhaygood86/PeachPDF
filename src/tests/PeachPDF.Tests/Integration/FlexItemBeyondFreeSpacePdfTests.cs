using System.Globalization;
using System.Text.RegularExpressions;

using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A flex item raised to its automatic minimum size (CSS Flexbox 1 section 4.5) can leave the line with no free space after all; the
    /// line then shrinks its other items (section 9.7 sums the hypothetical sizes, which the minimum has already clamped) instead of growing
    /// past the container. The same rule is asserted on the layout in <c>PeachBrackets.Engine.Tests</c> (<c>FlexGrowAutoMinimumTests</c>);
    /// this one goes through <see cref="PdfGenerator"/>, which is what the coverage gate measures the engine through.
    /// </summary>
    public class FlexItemBeyondFreeSpacePdfTests
    {
        private readonly record struct Rect(double X, double Y, double W, double H);

        [Fact]
        public async Task AGrowingItemRaisedToItsWord_ShrinksTheOthers_AndStaysInsideTheContainer()
        {
            var generator = new PdfGenerator();
            await using (var stream = File.OpenRead(BundledFonts.LiberationSans))
                await generator.AddFontFromStream(stream);
            var family = TypefaceFixtures.FamilyNameOf(BundledFonts.LiberationSans);

            var html = $"<!DOCTYPE html><html><head><style>body {{ margin: 0; font-family: '{family}'; font-size: 14px }}</style></head><body>" +
                       "<div style='display:flex;width:300px'><div style='width:60%;background:#f00'>&nbsp;</div>" +
                       "<div style='flex:1 1 30%;background:#0f0'>&nbsp;</div><div style='flex:2;background:#00f'>unbreakable</div></div></body></html>";
            var result = await generator.GeneratePdf(html, PageSize.A4);
            var file = result.Inspect();

            var rects = new Dictionary<string, Rect>();
            var fill = new Regex(@"(?<c>\S+ \S+ \S+) rg\s+(?:/\S+ gs\s+)?(?<x>\S+) (?<y>\S+) (?<w>\S+) (?<h>\S+) re\s+f", RegexOptions.CultureInvariant);
            foreach (Match m in fill.Matches(file.ContentText(file.Pages[0])))
            {
                double Num(string name) => double.Parse(m.Groups[name].Value, CultureInfo.InvariantCulture);
                rects.TryAdd(m.Groups["c"].Value, new Rect(Num("x"), Num("y"), Num("w"), Num("h")));
            }

            var first = rects["1 0 0"];
            var last = rects["0 0 1"];

            // The container is 300px = 225pt wide; the three items fill it exactly, none past its right edge.
            Assert.True(Math.Abs(last.X + last.W - (first.X + 225)) < 0.5,
                $"the last item ends at {last.X + last.W}, the container at {first.X + 225}");
        }
        [Fact]
        public async Task AFlexNoneItem_KeepsItsWidthWhenASiblingIsRaisedToItsWord()
        {
            var generator = new PdfGenerator();
            await using (var stream = File.OpenRead(BundledFonts.LiberationSans))
                await generator.AddFontFromStream(stream);
            var family = TypefaceFixtures.FamilyNameOf(BundledFonts.LiberationSans);

            // flex: none is 0 0 auto. Its shrink factor used to stay at 1, so the line (200 + 0 + 150 of 300 px once the third
            // item is raised to its content) took the overflow from the first item as well.
            var html = $"<!DOCTYPE html><html><head><style>body {{ margin: 0; font-family: '{family}'; font-size: 14px }}</style></head><body>" +
                       "<div style='display:flex;width:300px'><div style='flex:none;width:200px;height:10px;background:#f00'></div>" +
                       "<div style='flex:1;height:10px;background:#0f0'></div>" +
                       "<div style='flex:2;height:10px;background:#00f'><i style='display:inline-block;width:150px'></i></div></div></body></html>";
            var result = await generator.GeneratePdf(html, PageSize.A4);
            var file = result.Inspect();

            var fill = new Regex(@"(?<c>\S+ \S+ \S+) rg\s+(?:/\S+ gs\s+)?(?<x>\S+) (?<y>\S+) (?<w>\S+) (?<h>\S+) re\s+f", RegexOptions.CultureInvariant);
            var red = fill.Matches(file.ContentText(file.Pages[0])).Cast<Match>().First(m => m.Groups["c"].Value == "1 0 0");

            // 200px = 150pt.
            var width = double.Parse(red.Groups["w"].Value, CultureInfo.InvariantCulture);
            Assert.True(Math.Abs(width - 150) < 0.5, $"the flex: none item is {width}pt wide, not 150pt");
        }
    }
}
