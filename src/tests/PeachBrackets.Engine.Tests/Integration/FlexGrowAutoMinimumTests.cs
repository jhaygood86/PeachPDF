using System.Linq;
using PeachBrackets.Html.Dom;
using System.Threading.Tasks;
using static PeachBrackets.Engine.Tests.TestSupport.LayoutHarness;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// The automatic minimum size also floors an item a line grows (CSS Flexbox 1 sections 4.5 and 9.7): <c>flex: 1</c> items
    /// with a zero basis no longer split the line equally when one holds content wider (or taller) than its share.
    /// </summary>
    public class FlexGrowAutoMinimumTests
    {
        private static string Row(string firstStyle = "") =>
            Wrap("<div style='display:flex;width:240pt;font-size:20pt'>" +
                 $"<div id='a' style='flex:1;{firstStyle}'>wwwwwwwwwwwww</div><div id='b' style='flex:1'>x</div><div id='c' style='flex:1'>y</div></div>");

        [Fact]
        public async Task ARowItemWithAWideWordKeepsItsWidthAndTheOthersShareTheRest()
        {
            var (root, _) = await LayoutAsync(Row());
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;
            var c = FindById(root, "c")!;

            Assert.True(a.ActualBoxSizingWidth > 80, $"a keeps its word's width, was {a.ActualBoxSizingWidth}");
            Assert.Equal(b.ActualBoxSizingWidth, c.ActualBoxSizingWidth, 0.5);
            Assert.Equal(240, a.ActualBoxSizingWidth + b.ActualBoxSizingWidth + c.ActualBoxSizingWidth, 0.5);
        }

        private static string FloorBeyondTheFreeSpaceRow(string firstStyle = "") =>
            Wrap("<div style='display:flex;width:240pt;font-size:20pt'>" +
                 $"<div id='a' style='width:60%;{firstStyle}'></div><div id='b' style='flex:1 1 30%'></div><div id='c' style='flex:2'>wwwwww</div></div>");

        [Fact]
        public async Task AGrowingItemRaisedToItsWordShrinksTheOthersWhenTheLineNoLongerFits()
        {
            // 144 + 72 + 0 leaves 24pt of free space on the flex base sizes, but the third item cannot be narrower than its
            // word (about 87pt), so the line overflows by that much and the first two shrink in proportion to their sizes.
            var (root, _) = await LayoutAsync(FloorBeyondTheFreeSpaceRow());
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;
            var c = FindById(root, "c")!;

            Assert.True(c.ActualBoxSizingWidth > 85, $"c keeps its word's width, was {c.ActualBoxSizingWidth}");
            Assert.Equal(240, a.ActualBoxSizingWidth + b.ActualBoxSizingWidth + c.ActualBoxSizingWidth, 0.5);
            Assert.Equal(2, a.ActualBoxSizingWidth / b.ActualBoxSizingWidth, 0.05);
            Assert.Equal(a.ActualBoxSizingWidth + b.ActualBoxSizingWidth, c.Location.X - a.Location.X, 0.5);
        }

        [Fact]
        public async Task AnItemThatCannotShrinkKeepsItsSizeWhenTheRaisedItemTakesTheRoom()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='display:flex;width:240pt;font-size:20pt'>" +
                "<div id='a' style='width:60%;flex-shrink:0'></div><div id='b' style='flex:1 1 30%'></div><div id='c' style='flex:2'>wwwwww</div></div>"));

            Assert.Equal(144, FindById(root, "a")!.ActualBoxSizingWidth, 0.5);
            Assert.True(FindById(root, "c")!.ActualBoxSizingWidth > 85);
            Assert.Equal(240, FindById(root, "a")!.ActualBoxSizingWidth + FindById(root, "b")!.ActualBoxSizingWidth
                + FindById(root, "c")!.ActualBoxSizingWidth, 0.5);
        }

        [Fact]
        public async Task ARaisedItemThatStillFitsLeavesTheOthersAtTheirSizes()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='display:flex;width:240pt;font-size:20pt'>" +
                "<div id='a' style='width:30%'></div><div id='b' style='flex:1 1 30%'></div><div id='c' style='flex:2'>wwwwww</div></div>"));

            Assert.Equal(72, FindById(root, "a")!.ActualBoxSizingWidth, 0.5);
            Assert.True(FindById(root, "c")!.ActualBoxSizingWidth > 85);
            Assert.Equal(240, FindById(root, "a")!.ActualBoxSizingWidth + FindById(root, "b")!.ActualBoxSizingWidth
                + FindById(root, "c")!.ActualBoxSizingWidth, 0.5);
        }

        private static async Task<(double A, double B, double C)> WidthsOf(string html)
        {
            var (root, _) = await LayoutAsync(Wrap(html));
            return (FindById(root, "a")!.ActualBoxSizingWidth, FindById(root, "b")!.ActualBoxSizingWidth, FindById(root, "c")!.ActualBoxSizingWidth);
        }

        [Fact]
        public async Task AGapBetweenTheItemsIsPartOfTheRoomTheRaisedLineNeeds()
        {
            // 20pt gaps leave 200pt for the items: 72 + 48 + 0 fit, but the word's width raises the third item past that.
            var (a, b, c) = await WidthsOf("<div style='display:flex;gap:20pt;width:240pt;font-size:20pt'>" +
                "<div id='a' style='width:30%'></div><div id='b' style='flex:1 1 20%'></div><div id='c' style='flex:2'>wwwwww</div></div>");

            Assert.Equal(200, a + b + c, 0.5);
            Assert.Equal(1.5, a / b, 0.05);
        }

        [Fact]
        public async Task AnItemsMarginsArePartOfTheRoomTheRaisedLineNeeds()
        {
            var (a, b, c) = await WidthsOf("<div style='display:flex;width:240pt;font-size:20pt'>" +
                "<div id='a' style='width:30%;margin:0 30pt'></div><div id='b' style='flex:1 1 20%'></div><div id='c' style='flex:2'>wwwwww</div></div>");

            Assert.Equal(180, a + b + c, 0.5);
            Assert.Equal(1.5, a / b, 0.05);
        }

        [Fact]
        public async Task AnItemRaisedByOnlyAFewPointsStillShrinksTheOthers()
        {
            var (a, b, c) = await WidthsOf("<div style='display:flex;width:240pt;font-size:20pt'>" +
                "<div id='a' style='width:60%'></div><div id='b' style='flex:1 1 30%'></div><div id='c' style='flex:2'>www</div></div>");

            Assert.Equal(240, a + b + c, 0.5);
            Assert.Equal(2, a / b, 0.05);
        }

        [Fact]
        public async Task ALineThatOverflowsByAFewPointsOnceTheItemIsRaisedShrinksTheOthers()
        {
            var (_, _, wordWidth) = await WidthsOf("<div style='display:flex;width:240pt;font-size:20pt'>" +
                "<div id='a'></div><div id='b'></div><div id='c' style='flex:0 0 auto'>wwwwww</div></div>");
            var first = 240 - 72 - wordWidth + 2;

            var (a, b, c) = await WidthsOf("<div style='display:flex;width:240pt;font-size:20pt'>" +
                $"<div id='a' style='width:{first.ToString(System.Globalization.CultureInfo.InvariantCulture)}pt'></div><div id='b' style='flex:1 1 30%'></div><div id='c' style='flex:2'>wwwwww</div></div>");

            Assert.Equal(240, a + b + c, 0.5);
            Assert.Equal(first / 72, a / b, 0.05);
        }

        [Fact]
        public async Task ALineWhoseBaseSizesFillTheContainerExactlyShrinksWhenAnItemIsRaised()
        {
            // 144 + 96 + 0 is exactly the 240pt container, so there is no free space to grow into, yet the third item's word
            // still raises it and the line has to give that room back.
            var (a, b, c) = await WidthsOf("<div style='display:flex;width:240pt;font-size:20pt'>" +
                "<div id='a' style='width:60%'></div><div id='b' style='flex:1 1 40%'></div><div id='c' style='flex:2'>wwwwww</div></div>");

            Assert.True(c > 85, $"c keeps its word's width, was {c}");
            Assert.Equal(240, a + b + c, 0.5);
            Assert.Equal(1.5, a / b, 0.05);
        }

        [Fact]
        public async Task AColumnItemRaisedToItsContentHeightShrinksTheOthers()
        {
            var (root, _) = await LayoutAsync(Wrap("<div style='display:flex;flex-direction:column;height:240pt;font-size:20pt'>" +
                "<div id='a' style='height:60%'></div><div id='b' style='flex:1 1 30%'></div><div id='c' style='flex:2'>w<br>w<br>w<br>w<br>w<br>w</div></div>"));
            var a = FindById(root, "a")!.ActualBoxSizingHeight;
            var b = FindById(root, "b")!.ActualBoxSizingHeight;
            var c = FindById(root, "c")!.ActualBoxSizingHeight;

            Assert.Equal(240, a + b + c, 0.5);
            Assert.Equal(2, a / b, 0.05);
        }

        [Fact]
        public async Task AFlexNoneItemIsNeitherGrownNorShrunkWhenAnotherItemIsRaised()
        {
            // flex: none is 0 0 auto; its shrink factor used to stay at 1, so the raised sibling made it give up width.
            var (a, _, c) = await WidthsOf("<div style='display:flex;width:240pt;font-size:20pt'>" +
                "<div id='a' style='flex:none;width:160pt'></div><div id='b' style='flex:1'></div><div id='c' style='flex:2'>wwwwww</div></div>");

            Assert.Equal(160, a, 0.5);
            Assert.True(c > 85, $"c keeps its word's width, was {c}");
        }

        [Fact]
        public async Task APercentageWidthCapsTheAutomaticMinimumOfARaisedItem()
        {
            // The first item's word is 90pt wide, but its width: 20% (48pt of the 240pt line) is the smaller specified size, so
            // that is its minimum and the other two items keep the 50% and 30% they asked for.
            var (a, b, c) = await WidthsOf("<div style='display:flex;width:240pt'>" +
                "<div id='a' style='width:20%;flex:1 1 0'><i style='display:inline-block;width:90pt'></i></div>" +
                "<div id='b' style='width:50%'></div><div id='c' style='width:30%'></div></div>");

            Assert.Equal(48, a, 0.5);
            Assert.Equal(120, b, 0.5);
            Assert.Equal(72, c, 0.5);
        }

        [Fact]
        public async Task AZeroMinWidthOrAScrollContainerLetsTheRowSplitEqually()
        {
            foreach (var style in new[] { "min-width:0", "overflow:hidden" })
            {
                var (root, _) = await LayoutAsync(Row(style));
                Assert.Equal(80, FindById(root, "a")!.ActualBoxSizingWidth, 0.5);
            }
        }

        [Fact]
        public async Task ARowWhoseItemsFitTheirSharesIsUnchanged()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='display:flex;width:240pt;font-size:10pt'><div id='a' style='flex:1'>one</div><div id='b' style='flex:2'>two</div></div>"));

            Assert.Equal(80, FindById(root, "a")!.ActualBoxSizingWidth, 0.5);
            Assert.Equal(160, FindById(root, "b")!.ActualBoxSizingWidth, 0.5);
        }

        [Fact]
        public async Task ColumnItemsWithAZeroBasisKeepTheirContentHeight()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<style>.i{line-height:20pt;margin:0;flex:1}</style><div style='display:flex;flex-direction:column;height:100pt;width:200pt'>"
                + string.Concat(Enumerable.Range(0, 3).Select(n => $"<div id='i{n}' class='i'>a<br>b<br>c</div>")) + "</div>"));

            for (var n = 0; n < 3; n++)
            {
                Assert.Equal(60, FindById(root, $"i{n}")!.ActualBoxSizingHeight, 1);
            }
        }

        [Fact]
        public async Task ColumnItemsThatFitTheirSharesStillSplitTheHeightEqually()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<style>.i{line-height:10pt;margin:0;flex:1}</style><div style='display:flex;flex-direction:column;height:120pt;width:200pt'>"
                + string.Concat(Enumerable.Range(0, 3).Select(n => $"<div id='i{n}' class='i'>a</div>")) + "</div>"));

            for (var n = 0; n < 3; n++)
            {
                Assert.Equal(40, FindById(root, $"i{n}")!.ActualBoxSizingHeight, 1);
            }
        }

        [Fact]
        public async Task AFlexNoneItemWhoseChildrenHaveADefiniteWidthIsAsWideAsThatWidthNotItsText()
        {
            // The child is 100pt wide however long its text: the text wraps inside it, so the item (which cannot shrink) is 100pt.
            var item = "<div style='flex:none'><div style='width:100pt;height:10pt'></div>"
                + "<div style='width:100pt'>one two three four five six seven eight nine ten eleven</div></div>";
            var (root, _) = await LayoutAsync(Wrap($"<div style='display:flex;width:420pt;font-size:12pt'>{item.Replace("<div style='flex:none'>", "<div id='a' style='flex:none'>")}{item.Replace("<div style='flex:none'>", "<div id='b' style='flex:none'>")}</div>"));

            Assert.Equal(100, FindById(root, "a")!.ActualBoxSizingWidth, 0.5);
            Assert.Equal(100, FindById(root, "b")!.ActualBoxSizingWidth, 0.5);
            Assert.Equal(100, FindById(root, "b")!.Location.X - FindById(root, "a")!.Location.X, 0.5);
        }

        [Fact]
        public async Task AWrappingRowBreaksBeforeAnItemThatWouldBeRaisedPastTheLine()
        {
            // The third item's basis is 0, but its word cannot be narrower than about 140pt: with the first two it does not fit
            // the 225pt line, so it starts the second line instead of squeezing the first item onto several lines.
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='display:flex;flex-wrap:wrap;width:225pt;font-size:12pt'>"
                + "<div id='a' style='flex:0 1 auto'>aa bb cc</div><div id='b' style='flex:0 0 60pt'>dd</div>"
                + "<div id='c' style='flex:1 1 0'>Lwwwwwwwwwwwwwwwwwwwwwwww</div></div>"));
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;
            var c = FindById(root, "c")!;

            Assert.Equal(a.Location.Y, b.Location.Y, 0.5);
            Assert.True(c.Location.Y > a.Location.Y + 1, $"c starts a second line, a.Y={a.Location.Y} c.Y={c.Location.Y}");
            Assert.True(a.ActualBoxSizingWidth > 40, $"a keeps its one-line width, was {a.ActualBoxSizingWidth}");
            Assert.Equal(60, b.ActualBoxSizingWidth, 0.5);
        }

        [Fact]
        public async Task AWrappingRowStillKeepsItemsTogetherWhenTheirMinimumsFit()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='display:flex;flex-wrap:wrap;width:300pt;font-size:12pt'>"
                + "<div id='a' style='flex:1 1 0'>aa</div><div id='b' style='flex:1 1 0'>bb</div><div id='c' style='flex:1 1 0'>cc</div></div>"));

            Assert.Equal(FindById(root, "a")!.Location.Y, FindById(root, "c")!.Location.Y, 0.5);
            Assert.Equal(100, FindById(root, "b")!.ActualBoxSizingWidth, 0.5);
        }

        [Fact]
        public async Task AnItemWithAZeroBasisThatWrapsOntoItsOwnLineIsRaisedToItsWord()
        {
            // The break puts the item on a line of its own (its word does not fit beside the 60pt item); with no growing item and
            // no overflow on that line it was left at its basis of 0 and laid out empty.
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='display:flex;flex-wrap:wrap;width:150pt;font-size:12pt'><div id='a' style='width:60pt'>s1</div>"
                + "<div id='b' style='flex:0'>Lwwwwwwwwwwwwwwwwwwwwwwww</div></div>"));
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;

            Assert.True(b.Location.Y > a.Location.Y + 1, $"b starts a second line, a.Y={a.Location.Y} b.Y={b.Location.Y}");
            Assert.True(b.ActualBoxSizingWidth > 100, $"b is raised to its word's width, was {b.ActualBoxSizingWidth}");
        }

        [Fact]
        public async Task ABlockWithADefiniteWidthContributesItsMinAndMaxWidthClampedWidthAndItsMargins()
        {
            string Item(string id, string style) =>
                $"<div id='{id}' style='flex:none'><div style='height:10pt;{style}'></div></div>";
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='display:flex;width:600pt'>"
                + Item("min", "width:100pt;min-width:150pt") + Item("max", "width:300pt;max-width:80pt") + Item("margin", "width:90pt;margin:0 10pt")
                + "</div>"));

            Assert.Equal(150, FindById(root, "min")!.ActualBoxSizingWidth, 0.5);
            Assert.Equal(80, FindById(root, "max")!.ActualBoxSizingWidth, 0.5);
            Assert.Equal(110, FindById(root, "margin")!.ActualBoxSizingWidth, 0.5);
        }
    }
}
