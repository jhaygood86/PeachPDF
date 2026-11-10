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

        private static async Task<Dictionary<string, Rect>> FillsOf(string body)
        {
            var generator = new PdfGenerator();
            await using (var stream = File.OpenRead(BundledFonts.LiberationSans))
                await generator.AddFontFromStream(stream);
            var family = TypefaceFixtures.FamilyNameOf(BundledFonts.LiberationSans);

            var html = $"<!DOCTYPE html><html><head><style>body {{ margin: 0; font-family: '{family}'; font-size: 14px }}</style></head><body>{body}</body></html>";
            var file = (await generator.GeneratePdf(html, PageSize.A4)).Inspect();

            var rects = new Dictionary<string, Rect>();
            var fill = new Regex(@"(?<c>\S+ \S+ \S+) rg\s+(?:/\S+ gs\s+)?(?<x>\S+) (?<y>\S+) (?<w>\S+) (?<h>\S+) re\s+f", RegexOptions.CultureInvariant);
            foreach (Match m in fill.Matches(file.ContentText(file.Pages[0])))
            {
                double Num(string name) => double.Parse(m.Groups[name].Value, CultureInfo.InvariantCulture);
                rects.TryAdd(m.Groups["c"].Value, new Rect(Num("x"), Num("y"), Num("w"), Num("h")));
            }
            return rects;
        }

        [Fact]
        public async Task AFlexNoneItem_IsAsWideAsItsChildrensDefiniteWidth_NotAsItsText()
        {
            // The text under each 230px block is wider than 230px, but it wraps inside the block: the item, which cannot shrink,
            // is 230px (172.5pt) wide, so the second starts 230 + 24 px (190.5pt) after the first and all three fit the page.
            string Item(string color) =>
                $"<div style='flex:none'><div style='width:230px;height:10px;background:{color}'></div>" +
                "<div style='width:230px;font-size:11px'>container perspective, rotateY(48deg): the near edge is taller N</div></div>";
            var rects = await FillsOf($"<div style='display:flex;gap:24px;width:700px'>{Item("#f00")}{Item("#0f0")}{Item("#00f")}</div>");

            Assert.True(Math.Abs(rects["0 1 0"].X - rects["1 0 0"].X - 190.5) < 0.5, $"second item starts {rects["0 1 0"].X - rects["1 0 0"].X}pt after the first");
            Assert.True(Math.Abs(rects["0 0 1"].X - rects["0 1 0"].X - 190.5) < 0.5, $"third item starts {rects["0 0 1"].X - rects["0 1 0"].X}pt after the second");
        }

        [Fact]
        public async Task AWrappingRow_BreaksBeforeAnItemWhoseMinimumDoesNotFit_InsteadOfShrinkingTheLine()
        {
            // The third item's basis is 0, but its word cannot be narrower than its content: with the first two it does not fit the
            // 300px line, so it starts the second line and the first item keeps its single line.
            var rects = await FillsOf(
                "<div style='display:flex;flex-wrap:wrap;width:300px'><div style='flex:0 1 auto;background:#f00'>aa bb cc dd</div>" +
                "<div style='flex:0 0 80px;background:#0f0'>ee</div><div style='flex:1 1 0;background:#00f'>Lmmmmmmmmmmmmmmmmmmmm</div></div>");

            var first = rects["1 0 0"];
            var third = rects["0 0 1"];
            Assert.True(third.Y < first.Y - 1, $"the third item starts a second line (first y {first.Y}, third y {third.Y})");
            Assert.True(first.H < 20, $"the first item keeps one line, was {first.H}pt tall");
        }

        [Fact]
        public async Task AWrappingRow_KeepsItemsWithAZeroMinWidthOrAMaxWidthOnTheLineTheirSizesFit()
        {
            // min-width: 0 has no automatic minimum, and a max-width caps it; neither item is raised, so all three share one line.
            var rects = await FillsOf(
                "<div style='display:flex;flex-wrap:wrap;width:300px'><div style='min-width:0;flex:0 1 80px;background:#f00'>Lmmmmmmmmmmmmmmmmmmmm</div>" +
                "<div style='max-width:100px;flex:0 0 80px;background:#0f0'>ee</div><div style='flex:0 0 60px;background:#00f'>ff</div></div>");

            Assert.True(Math.Abs(rects["1 0 0"].Y - rects["0 1 0"].Y) < 0.5 && Math.Abs(rects["0 1 0"].Y - rects["0 0 1"].Y) < 0.5, "all three items share one line");
            Assert.True(Math.Abs(rects["0 1 0"].W - 60) < 0.5, $"the second item is {rects["0 1 0"].W}pt wide, not 60pt");
        }

        [Fact]
        public async Task AFlexNoneItem_KeepsATableItsDeclaredWidthAsAFloor()
        {
            // A table's declared width is a floor (its columns cannot be narrower than their content), not its whole contribution.
            var rects = await FillsOf(
                "<div style='display:flex;width:600px'><div style='flex:none;background:#f00'><table style='width:200px'><tr><td>x</td></tr></table></div>" +
                "<div style='flex:none;background:#0f0'>y</div></div>");

            Assert.True(rects["1 0 0"].W >= 149.5, $"the item is {rects["1 0 0"].W}pt wide, a 200px table needs 150pt");
        }

        [Fact]
        public async Task AColumnItemWithADefiniteHeight_KeepsThatHeightAsItsMinimumWhenTheContainerIsTooShort()
        {
            // Each item is 80px tall around 100px of content: the minimum is the smaller of the two, so the 120px container
            // cannot squeeze them below 80px (60pt) each.
            var rects = await FillsOf(
                "<div style='display:flex;flex-direction:column;height:120px;width:200px'>" +
                "<div style='height:80px;background:#f00'><div style='height:100px'></div></div>" +
                "<div style='height:80px;background:#0f0'><div style='height:100px'></div></div></div>");

            Assert.True(Math.Abs(rects["1 0 0"].H - 60) < 0.5, $"the first item is {rects["1 0 0"].H}pt tall, not 60pt");
            Assert.True(Math.Abs(rects["0 1 0"].H - 60) < 0.5, $"the second item is {rects["0 1 0"].H}pt tall, not 60pt");
        }
    }
}
