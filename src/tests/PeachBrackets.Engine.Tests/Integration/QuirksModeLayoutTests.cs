using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Utils;
using System.Threading.Tasks;
using static PeachBrackets.Engine.Tests.TestSupport.LayoutHarness;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// What a document's mode changes in layout: the quirks-mode rules of the HTML Standard's rendering section (15.3.x) and the Quirks Mode Standard.
    /// Each case is the same markup with and without a DOCTYPE. The figures are what Chrome measures for the document of the issue that asked for
    /// them (BackCompat against CSS1Compat), in CSS px with a 14px font.
    /// </summary>
    public class QuirksModeLayoutTests
    {
        private const double PointsPerPx = 0.75;
        private const string Doctype = "<!DOCTYPE html>";

        private static Task<(CssBox Root, PeachBrackets.Html.HtmlContainerInt Container)> LayoutPageAsync(string body, bool doctype) =>
            LayoutAsync((doctype ? Doctype : "") + $"<html><body style='margin:0;font-size:14px'>{body}</body></html>", margin: 0);

        [Theory]
        [InlineData(true, DocumentMode.NoQuirks)]
        [InlineData(false, DocumentMode.Quirks)]
        public async Task TheDoctypeDecidesTheMode(bool doctype, DocumentMode expected)
        {
            var (_, container) = await LayoutPageAsync("<p>x</p>", doctype);

            Assert.Equal(expected, container.DocumentMode);
        }

        [Theory]
        [InlineData(true, 14.0)]
        [InlineData(false, 0.0)]
        public async Task TheFirstParagraphOfTheBodyHasItsTopMarginOnlyOutsideQuirksMode(bool doctype, double expectedTopPx)
        {
            var (root, _) = await LayoutPageAsync("<p id='p1'>first p</p>", doctype);

            Assert.Equal(expectedTopPx, FindById(root, "p1")!.Location.Y / PointsPerPx, 0.1);
        }

        [Theory]
        [InlineData(true, 0.0)]
        [InlineData(false, 14.0)]
        public async Task AFormHasABottomMarginInQuirksMode(bool doctype, double expectedGapPx)
        {
            var (root, _) = await LayoutPageAsync("<div style='height:5px'></div><form id='f'><input></form><div id='after' style='height:5px'></div>", doctype);

            var form = FindById(root, "f")!;
            var after = FindById(root, "after")!;
            Assert.Equal(expectedGapPx, (after.Location.Y - form.ActualBottom) / PointsPerPx, 0.1);
        }

        [Theory]
        [InlineData(true, 14.0)]
        [InlineData(false, 0.0)]
        public async Task TheFirstParagraphOfACellHasItsTopMarginOnlyOutsideQuirksMode(bool doctype, double expectedMarginPx)
        {
            var (root, _) = await LayoutPageAsync("<table><tr><td id='cell'><p id='c'>a</p><p>b</p></td></tr></table>", doctype);

            var cell = FindById(root, "cell")!;
            var paragraph = FindById(root, "c")!;
            // The cell has 1px of padding; the paragraph's own top margin is what the quirk takes away.
            Assert.Equal(1.0 + expectedMarginPx, (paragraph.Location.Y - cell.Location.Y) / PointsPerPx, 0.6);
        }

        [Theory]
        [InlineData(true, 10.5)]
        [InlineData(false, 11.0)]
        public async Task ATableInheritsTheBodysFontSizeOnlyOutsideQuirksMode(bool doctype, double expectedPt)
        {
            var (root, _) = await LayoutPageAsync("<table><tr><td id='cell'>text</td></tr></table>", doctype);

            // In quirks mode the table's font size is the initial one, `medium`: 11pt on paper (a browser's 16px), not the body's 14px.
            Assert.Equal(expectedPt, FindById(root, "cell")!.ActualFont.Size, 0.01);
        }

        private static string Property(CssBox box, string name) => CssUtils.GetPropertyValue(box, name) ?? "";

        // HTML Standard 15.3.9: the margins a paragraph (an element with default margins) gives up in quirks mode, by what it sits next to.
        // 14px is the 1em margin of a paragraph at the 14px font these pages set.
        [Theory]
        [InlineData("<p id='p'>x</p>", 0.0, 14.0)]
        [InlineData("<p id='p'></p>", 0.0, 0.0)]
        [InlineData("<p id='p'> </p>", 0.0, 0.0)]
        [InlineData("<p id='p'><span></span></p>", 0.0, 14.0)]
        [InlineData("<p id='p'>x</p><div>d</div>", 0.0, 14.0)]
        [InlineData("text<p id='p'>x</p>", 14.0, 14.0)]
        [InlineData("  <p id='p'>x</p>", 0.0, 14.0)]
        [InlineData("<!-- c --><p id='p'>x</p>", 0.0, 14.0)]
        [InlineData("<div>d</div><p id='p'>x</p>", 14.0, 14.0)]
        [InlineData("<h1 id='p'>x</h1>", 0.0, 18.76)]
        [InlineData("<ul id='p'><li>x</li></ul>", 0.0, 14.0)]
        [InlineData("<div><p id='p'>x</p></div>", 14.0, 14.0)]
        public async Task AnElementWithDefaultMarginsGivesUpMarginsAtTheStartOfTheBody(string body, double topPx, double bottomPx)
        {
            var (root, _) = await LayoutPageAsync(body, doctype: false);
            var element = FindById(root, "p")!;

            Assert.Equal(topPx, element.ActualMarginTop / PointsPerPx, 0.1);
            Assert.Equal(bottomPx, element.ActualMarginBottom / PointsPerPx, 0.1);
        }

        [Theory]
        [InlineData("<p id='p'>x</p>")]
        [InlineData("<p id='p'></p>")]
        public async Task TheSameElementsKeepTheirMarginsInStandardsMode(string body)
        {
            var (root, _) = await LayoutPageAsync(body, doctype: true);
            var element = FindById(root, "p")!;

            Assert.Equal(14.0, element.ActualMarginTop / PointsPerPx, 0.1);
            Assert.Equal(14.0, element.ActualMarginBottom / PointsPerPx, 0.1);
        }

        [Theory]
        [InlineData("<td><p id='p'>x</p></td>", 0.0, 0.0)]
        [InlineData("<td><p id='p'>x</p><p>y</p></td>", 0.0, 14.0)]
        [InlineData("<td><p>y</p><p id='p'>x</p></td>", 14.0, 0.0)]
        [InlineData("<td><div>d</div><p id='p'></p></td>", 0.0, 0.0)]
        [InlineData("<td><div>d</div><p id='p'>x</p></td>", 14.0, 0.0)]
        [InlineData("<td>t<p id='p'>x</p></td>", 14.0, 0.0)]
        [InlineData("<td><p id='p'>x</p>t</td>", 0.0, 14.0)]
        [InlineData("<td><blockquote id='p'>x</blockquote></td>", 0.0, 14.0)]
        [InlineData("<th><p id='p'>x</p></th>", 0.0, 0.0)]
        public async Task AnElementWithDefaultMarginsGivesUpMarginsInATableCell(string cell, double topPx, double bottomPx)
        {
            // The quirks mode table has the initial font size, so the table says 14px for its cells to have the same 1em as the body does.
            var (root, _) = await LayoutPageAsync($"<table style='font-size:14px'><tr>{cell}</tr></table>", doctype: false);
            var element = FindById(root, "p")!;

            Assert.Equal(topPx, element.ActualMarginTop / PointsPerPx, 0.1);
            Assert.Equal(bottomPx, element.ActualMarginBottom / PointsPerPx, 0.1);
        }

        [Fact]
        public async Task AnAuthorMarginBeatsTheQuirkOfTheUserAgentSheet()
        {
            var (root, _) = await LayoutAsync("<html><head><style>p { margin-top: 20px }</style></head><body style='margin:0;font-size:14px'><p id='p'>x</p></body></html>", margin: 0);

            Assert.Equal(20.0, FindById(root, "p")!.ActualMarginTop / PointsPerPx, 0.1);
        }

        [Fact]
        public async Task TheQuirkRulesWinOverTheNestedListRuleOfTheStandardSheet()
        {
            var (root, _) = await LayoutPageAsync("<table><tr><td><ul id='p'><li>x</li></ul></td></tr></table>", doctype: false);

            Assert.Equal(0.0, FindById(root, "p")!.ActualMarginTop / PointsPerPx, 0.1);
        }

        [Theory]
        [InlineData(false, "inside")]
        [InlineData(true, "outside")]
        public async Task AListItemOutsideAListHasItsMarkerInsideOnlyInQuirksMode(bool doctype, string expected)
        {
            var (root, _) = await LayoutPageAsync("<div><li id='li'>x</li></div>", doctype);

            Assert.Equal(expected, Property(FindById(root, "li")!, "list-style-position"));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task AListItemInAListHasItsMarkerOutsideInEitherMode(bool doctype)
        {
            // `:is(dir, menu, ol, ul) :is(dir, menu, ol, ul, li)` takes the value the list has back (unset: inherited).
            var (root, _) = await LayoutPageAsync("<ul><li id='li'>x<ol><li id='inner'>y</li></ol></li></ul>", doctype);

            Assert.Equal("outside", Property(FindById(root, "li")!, "list-style-position"));
            Assert.Equal("outside", Property(FindById(root, "inner")!, "list-style-position"));
        }

        [Theory]
        [InlineData(false, "border-box")]
        [InlineData(true, "content-box")]
        public async Task ATextFieldAndTextAreaAreBorderBoxInQuirksMode(bool doctype, string expected)
        {
            var (root, _) = await LayoutPageAsync("<input id='i'><textarea id='t'></textarea><input id='img' type='image'>", doctype);

            Assert.Equal(expected, Property(FindById(root, "i")!, "box-sizing"));
            Assert.Equal(expected, Property(FindById(root, "t")!, "box-sizing"));
            Assert.Equal("content-box", Property(FindById(root, "img")!, "box-sizing"));
        }

        [Theory]
        [InlineData(false, 3.0)]
        [InlineData(true, 0.0)]
        public async Task AnImageAlignedLeftHasAMarginToItsRightInQuirksMode(bool doctype, double rightPx)
        {
            var (root, _) = await LayoutPageAsync("<img id='i' align='left' width='10' height='10'>", doctype);
            var image = FindById(root, "i")!;

            Assert.Equal(rightPx, image.ActualMarginRight / PointsPerPx, 0.1);
            Assert.Equal(0.0, image.ActualMarginLeft / PointsPerPx, 0.1);
        }

        [Fact]
        public async Task AnImageAlignedRightHasAMarginToItsLeftInQuirksMode()
        {
            var (root, _) = await LayoutPageAsync("<img id='i' align='RIGHT' width='10' height='10'>", doctype: false);

            Assert.Equal(3.0, FindById(root, "i")!.ActualMarginLeft / PointsPerPx, 0.1);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ATableTakesTheInitialFontAndTextAlignInQuirksMode(bool doctype)
        {
            var (root, _) = await LayoutPageAsync(
                "<div style='font-weight:bold;font-style:italic;text-align:right;white-space:pre'><table id='t'><tr><td id='c'>x</td></tr></table></div>", doctype);
            var cell = FindById(root, "c")!;

            if (doctype)
            {
                Assert.Equal("bold", Property(cell, "font-weight"));
                Assert.Equal("italic", Property(cell, "font-style"));
            }
            else
            {
                Assert.NotEqual("bold", Property(cell, "font-weight"));
                Assert.Equal("normal", Property(cell, "font-style"));
            }
        }

        [Theory]
        [InlineData(false, "rgb(255, 0, 0)")]
        [InlineData(true, "rgb(0, 0, 255)")]
        public async Task ATableTakesTheBodysColorNotItsParentsInQuirksMode(bool doctype, string expected)
        {
            var html = (doctype ? Doctype : "") + "<html><body style='color:rgb(255,0,0)'><div style='color:rgb(0,0,255)'><table id='t'><tr><td id='c'>x</td></tr></table></div></body></html>";
            var (root, _) = await LayoutAsync(html, margin: 0);

            Assert.Equal(expected, FindById(root, "c")!.Color);
        }

        [Fact]
        public async Task ATableWithAColorOfItsOwnKeepsItInQuirksMode()
        {
            var (root, _) = await LayoutAsync("<html><body style='color:rgb(255,0,0)'><table id='t' style='color:rgb(0,128,0)'><tr><td id='c'>x</td></tr></table></body></html>", margin: 0);

            Assert.Equal("rgb(0, 128, 0)", FindById(root, "c")!.Color);
        }

        [Theory]
        [InlineData(false, 60.0)]
        [InlineData(true, 80.0)]
        public async Task ATableCellsHeightIsItsBorderBoxHeightInQuirksMode(bool doctype, double expectedPx)
        {
            // height: 60px with 5px of padding and 5px of border above and below: 60 in quirks mode (border box), 80 in standards mode.
            var (root, _) = await LayoutPageAsync("<table><tr><td id='c' style='height:60px;padding:5px 0;border:5px solid red'>x</td></tr></table>", doctype);

            Assert.Equal(expectedPx, FindById(root, "c")!.ActualBoxSizingHeight / PointsPerPx, 0.6);
        }

        [Fact]
        public async Task ATableCellsWidthIsStillItsContentBoxWidthInQuirksMode()
        {
            var (root, _) = await LayoutPageAsync("<table style='table-layout:fixed;width:200px'><tr><td id='c' style='width:100px;padding:0 5px'>x</td><td></td></tr></table>", doctype: false);

            Assert.Equal(110.0, FindById(root, "c")!.ActualBoxSizingWidth / PointsPerPx, 0.6);
        }

        [Fact]
        public async Task AnImageOnlyLineIsAsTallAsTheImageInQuirksMode()
        {
            // No strut: the line holds the image and nothing hangs below its baseline. In standards mode the strut's descent makes the line taller.
            var body = "<div id='d'><img width='20' height='20'></div>";
            var (quirks, _) = await LayoutPageAsync(body, doctype: false);
            var (standards, _) = await LayoutPageAsync(body, doctype: true);

            Assert.Equal(20.0, FindById(quirks, "d")!.ActualBoxSizingHeight / PointsPerPx, 0.6);
            Assert.True(FindById(standards, "d")!.ActualBoxSizingHeight / PointsPerPx > 22.0, "standards mode leaves the strut's descent below the image");
        }

        [Fact]
        public async Task AnImageOnlyLineIsAsTallAsTheImageInLimitedQuirksMode()
        {
            var html = "<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Transitional//EN\" \"http://www.w3.org/TR/xhtml1/DTD/xhtml1-transitional.dtd\"><html><body style='margin:0;font-size:14px'><div id='d'><img width='20' height='20'></div></body></html>";
            var (root, container) = await LayoutAsync(html, margin: 0);

            Assert.Equal(DocumentMode.LimitedQuirks, container.DocumentMode);
            Assert.Equal(20.0, FindById(root, "d")!.ActualBoxSizingHeight / PointsPerPx, 0.6);
        }

        [Fact]
        public async Task ABlocksLineHeightGivesNoMinimumToALineOfAnImageInQuirksMode()
        {
            var (quirks, _) = await LayoutPageAsync("<div id='d' style='line-height:60px'><img width='20' height='20'></div>", doctype: false);
            var (standards, _) = await LayoutPageAsync("<div id='d' style='line-height:60px'><img width='20' height='20'></div>", doctype: true);

            Assert.Equal(20.0, FindById(quirks, "d")!.ActualBoxSizingHeight / PointsPerPx, 0.6);
            Assert.True(FindById(standards, "d")!.ActualBoxSizingHeight / PointsPerPx >= 60.0);
        }

        [Fact]
        public async Task TextStillTakesTheBlocksLineHeightInQuirksMode()
        {
            var (root, _) = await LayoutPageAsync("<div id='d' style='line-height:60px'>text</div>", doctype: false);

            Assert.Equal(60.0, FindById(root, "d")!.ActualBoxSizingHeight / PointsPerPx, 0.6);
        }

        [Theory]
        [InlineData("<span style='font-size:60px;line-height:60px'></span>text")]
        [InlineData("text<a name='x' style='font-size:60px;line-height:60px'></a>")]
        [InlineData("<span style='font-size:60px;line-height:60px'><img width='5' height='5'></span>text")]
        public async Task AnInlineBoxWithNoTextMakesNoLineTallerInQuirksMode(string content)
        {
            var (quirks, _) = await LayoutPageAsync($"<div id='d' style='line-height:16px'>{content}</div>", doctype: false);
            var (standards, _) = await LayoutPageAsync($"<div id='d' style='line-height:16px'>{content}</div>", doctype: true);

            Assert.Equal(16.0, FindById(quirks, "d")!.ActualBoxSizingHeight / PointsPerPx, 0.6);
            Assert.True(FindById(standards, "d")!.ActualBoxSizingHeight / PointsPerPx > 40.0);
        }

        [Fact]
        public async Task AnInlineBoxWithPaddingOrTextStillSizesTheLineInQuirksMode()
        {
            var (padded, _) = await LayoutPageAsync("<div id='d' style='line-height:16px'><span style='font-size:60px;line-height:60px;padding:0 1px'></span>text</div>", doctype: false);
            var (texted, _) = await LayoutPageAsync("<div id='d' style='line-height:16px'><span style='font-size:60px;line-height:60px'>big</span></div>", doctype: false);

            Assert.True(FindById(padded, "d")!.ActualBoxSizingHeight / PointsPerPx > 40.0);
            Assert.True(FindById(texted, "d")!.ActualBoxSizingHeight / PointsPerPx > 40.0);
        }

        [Fact]
        public async Task ABodyWithNoHeightFillsThePageInQuirksMode()
        {
            // The page is 842pt tall with no margin here. body: the page less its own 8px margins.
            var (root, _) = await LayoutAsync("<html><body id='b' style='background:red'>short</body></html>", margin: 0);
            var (standards, _) = await LayoutAsync(Doctype + "<html><body id='b' style='background:red'>short</body></html>", margin: 0);
            var html = DomUtils.GetBoxByTagName(root, "html")!;
            var body = FindById(root, "b")!;

            Assert.Equal(842.0, html.ActualBoxSizingHeight + html.ActualMarginTop + html.ActualMarginBottom, 0.5);
            Assert.Equal(842.0 - (16 * PointsPerPx), body.ActualBoxSizingHeight, 0.5);
            Assert.True(FindById(standards, "b")!.ActualBoxSizingHeight < 100);
        }

        [Fact]
        public async Task AFixedHeightIsNotChangedByTheViewportFillInQuirksMode()
        {
            var (root, _) = await LayoutAsync("<html><body id='b' style='height:100px'>short</body></html>", margin: 0);

            Assert.Equal(75.0, FindById(root, "b")!.ActualBoxSizingHeight, 0.5);
        }

        [Fact]
        public async Task APercentageHeightResolvesAgainstThePageThroughAutoHeightAncestorsInQuirksMode()
        {
            var body = "<div id='d' style='height:50%'>x</div>";
            var (quirks, _) = await LayoutAsync($"<html><body style='margin:0'>{body}</body></html>", margin: 0);
            var (standards, _) = await LayoutAsync($"{Doctype}<html><body style='margin:0'>{body}</body></html>", margin: 0);

            Assert.Equal(421.0, FindById(quirks, "d")!.ActualBoxSizingHeight, 1.0);
            Assert.True(FindById(standards, "d")!.ActualBoxSizingHeight < 100.0, "an indefinite percentage is auto in standards mode");
        }

        [Fact]
        public async Task APercentageHeightStopsAtAnAncestorWithAHeightOfItsOwnInQuirksMode()
        {
            var (root, _) = await LayoutAsync("<html><body style='margin:0'><div style='height:200px'><div><div id='d' style='height:50%'>x</div></div></div></body></html>", margin: 0);

            Assert.Equal(75.0, FindById(root, "d")!.ActualBoxSizingHeight, 0.6);
        }

        [Fact]
        public async Task ANowrapCellWithAWidthAttributeWrapsAsUsualInQuirksMode()
        {
            var cell = "<table><tr><td id='c' nowrap width='40'>aaa bbb ccc ddd eee</td></tr></table>";
            var (quirks, _) = await LayoutPageAsync(cell, doctype: false);
            var (standards, _) = await LayoutPageAsync(cell, doctype: true);

            Assert.Equal("wrap", Property(FindById(quirks, "c")!, "text-wrap-mode"));
            Assert.Equal("nowrap", Property(FindById(standards, "c")!, "text-wrap-mode"));
        }

        [Theory]
        [InlineData("<td id='c' nowrap>aaa</td>")]
        [InlineData("<td id='c' nowrap width='50%'>aaa</td>")]
        public async Task ANowrapCellWithNoLengthWidthStaysNowrapInQuirksMode(string cell)
        {
            var (root, _) = await LayoutPageAsync($"<table><tr>{cell}</tr></table>", doctype: false);

            Assert.Equal("nowrap", Property(FindById(root, "c")!, "text-wrap-mode"));
        }

        [Fact]
        public async Task ANowrapCellIsAsWideAsItsWidthInQuirksMode()
        {
            var cell = "<table><tr><td id='c' nowrap style='width:150px'>a</td><td>b</td></tr></table>";
            var (quirks, _) = await LayoutPageAsync(cell, doctype: false);

            Assert.True(FindById(quirks, "c")!.ActualBoxSizingWidth / PointsPerPx >= 150.0);
        }

        [Fact]
        public async Task ImagesInAnAutoWidthCellDoNotWrapApartInQuirksMode()
        {
            // Min-content: the images side by side are one unbreakable run, so the column is not narrower than all three.
            var content = "<table style='width:1px'><tr><td id='c'><img width='30' height='5'> <img width='30' height='5'> <img width='30' height='5'></td></tr></table>";
            var (quirks, _) = await LayoutPageAsync(content, doctype: false);
            var (standards, _) = await LayoutPageAsync(content, doctype: true);

            Assert.True(FindById(quirks, "c")!.ActualBoxSizingWidth / PointsPerPx >= 90.0);
            Assert.True(FindById(standards, "c")!.ActualBoxSizingWidth / PointsPerPx < 60.0);
        }
    }
}
