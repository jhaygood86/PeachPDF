using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Utils;
using PeachDrawing.PDF;
using PeachDrawing.Core;
using PeachPDF.Rendering;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static PeachBrackets.Engine.Tests.TestSupport.LayoutHarness;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// The parts of quirks mode that reach the cascade and the style sheet text rather than layout: what a style sheet's values mean (the Quirks Mode
    /// Standard 3.1 and 3.2), which selectors match (4.1), the <c>@media</c> feature the user-agent sheet's quirks block hangs on, and how the
    /// block leaves other documents alone.
    /// </summary>
    public class QuirksModeCascadeTests
    {
        private const string Doctype = "<!DOCTYPE html>";

        private static string Html(bool doctype, string head, string body) =>
            (doctype ? Doctype : "") + $"<html><head><style>{head}</style></head><body style='margin:0'>{body}</body></html>";

        [Theory]
        [InlineData("color: 00ff00", "rgb(0, 255, 0)")]
        [InlineData("color: ff0000", "rgb(255, 0, 0)")]
        [InlineData("color: f00", "rgb(255, 0, 0)")]
        [InlineData("color: 123456", "rgb(18, 52, 86)")]
        [InlineData("color: 000123", "rgb(0, 1, 35)")]
        [InlineData("color: 123", "rgb(0, 1, 35)")]
        public async Task AHexColorMayLackItsHashInQuirksMode(string declaration, string expected)
        {
            var (quirks, _) = await LayoutAsync(Html(false, $"#t {{ {declaration} }}", "<div id='t'>x</div>"), margin: 0);
            var (standards, _) = await LayoutAsync(Html(true, $"#t {{ {declaration} }}", "<div id='t'>x</div>"), margin: 0);

            Assert.Equal(expected, FindById(quirks, "t")!.Color);
            Assert.NotEqual(expected, FindById(standards, "t")!.Color);
        }

        [Fact]
        public async Task AHashlessColorWorksInAStyleAttributeAndInTheOtherColorProperties()
        {
            var (root, _) = await LayoutAsync(
                "<html><body style='margin:0'><div id='t' style='color:00ff00;background-color:ff0000;border:1px solid;border-top-color:0000ff'>x</div></body></html>", margin: 0);
            var box = FindById(root, "t")!;

            Assert.Equal("rgb(0, 255, 0)", box.Color);
            Assert.Equal("rgb(255, 0, 0)", box.BackgroundColor);
            Assert.Equal("rgb(0, 0, 255)", box.BorderTopColor);
        }

        [Fact]
        public async Task AKeywordIsNotTakenForAHashlessColor()
        {
            // `red` is not hex digits; `bad` is, but no color property takes it as a keyword, so it stays invalid... the declaration that
            // is valid is the one that stands.
            var (root, _) = await LayoutAsync(Html(false, "#t { color: rgb(0,0,255) } #t { color: red }", "<div id='t'>x</div>"), margin: 0);

            Assert.Equal("rgb(255, 0, 0)", FindById(root, "t")!.Color);
        }

        [Fact]
        public async Task AHashlessColorIsNotRecognizedInAShorthand()
        {
            var (root, _) = await LayoutAsync(Html(false, "#t { background: ff0000 }", "<div id='t'>x</div>"), margin: 0);

            Assert.NotEqual("rgb(255, 0, 0)", FindById(root, "t")!.BackgroundColor);
        }

        [Theory]
        [InlineData("width: 100", 75.0)]
        [InlineData("width: 100px", 75.0)]
        [InlineData("width: 100.5", 75.375)]
        public async Task ALengthMayLackItsUnitInQuirksMode(string declaration, double expectedPt)
        {
            var (quirks, _) = await LayoutAsync(Html(false, $"#t {{ {declaration}; height: 10px }}", "<div id='t'>x</div>"), margin: 0);

            Assert.Equal(expectedPt, FindById(quirks, "t")!.ActualBoxSizingWidth, 0.01);
        }

        [Fact]
        public async Task ABareNumberIsNotALengthOutsideQuirksMode()
        {
            var (standards, _) = await LayoutAsync(Html(true, "#t { width: 100; height: 10px }", "<div id='t'>x</div>"), margin: 0);

            // Invalid: the width stays auto, the page's width.
            Assert.True(FindById(standards, "t")!.ActualBoxSizingWidth > 400);
        }

        [Fact]
        public async Task UnitlessLengthsWorkInAShorthandOfTheListAndInAStyleAttribute()
        {
            var (root, _) = await LayoutAsync(
                "<html><body style='margin:0'><div id='t' style='margin:4 8;padding:2;font-size:16;height:20;width:100'>x</div></body></html>", margin: 0);
            var box = FindById(root, "t")!;

            Assert.Equal(3.0, box.ActualMarginTop, 0.01);
            Assert.Equal(6.0, box.ActualMarginLeft, 0.01);
            Assert.Equal(1.5, box.ActualPaddingTop, 0.01);
            Assert.Equal(12.0, box.ActualFont.Size, 0.01);
            Assert.Equal(75.0 + 3.0, box.ActualBoxSizingWidth, 0.01);
        }

        [Fact]
        public async Task EveryNumberOfAShorthandAndEveryColorOfAFourSidedPropertyIsRead()
        {
            var (root, _) = await LayoutAsync(
                "<html><body style='margin:0'><div id='t' style='margin:0 8;padding:4 8 12 16;border:1px solid;border-color:00ff00 ff0000 0000ff 123'>x</div></body></html>", margin: 0);
            var box = FindById(root, "t")!;

            Assert.Equal(0.0, box.ActualMarginTop, 0.01);
            Assert.Equal(6.0, box.ActualMarginRight, 0.01);
            Assert.Equal(3.0, box.ActualPaddingTop, 0.01);
            Assert.Equal(6.0, box.ActualPaddingRight, 0.01);
            Assert.Equal(9.0, box.ActualPaddingBottom, 0.01);
            Assert.Equal(12.0, box.ActualPaddingLeft, 0.01);
            Assert.Equal("rgb(0, 255, 0)", box.BorderTopColor);
            Assert.Equal("rgb(255, 0, 0)", box.BorderRightColor);
            Assert.Equal("rgb(0, 0, 255)", box.BorderBottomColor);
            Assert.Equal("rgb(0, 1, 35)", box.BorderLeftColor);
        }

        [Fact]
        public async Task ABrLineKeepsItsHeightWithoutAStrutInQuirksMode()
        {
            var (root, _) = await LayoutAsync(
                "<html><body style='margin:0;font-size:14px'><div id='d' style='line-height:20px'>a<br><br>b</div></body></html>", margin: 0);

            // Three lines of 20px: an empty line made by a break is as tall as any other.
            Assert.Equal(60.0, FindById(root, "d")!.ActualBoxSizingHeight / 0.75, 0.6);
        }

        [Fact]
        public async Task AFirstLineStyleReadsAnInlineStyleWithTheQuirksOfTheDocument()
        {
            var (root, _) = await LayoutAsync(
                "<html><head><style>#d::first-line { color: rgb(1,2,3) }</style></head><body style='margin:0'><div id='d' style='font-size:20;width:200'>first line of text</div></body></html>", margin: 0);

            Assert.Equal(15.0, FindById(root, "d")!.ActualFont.Size, 0.01);
        }

        [Fact]
        public async Task LineHeightIsNotOneOfTheUnitlessLengths()
        {
            // line-height: 2 is a multiplier, not 2px, in every mode.
            var (root, _) = await LayoutAsync(Html(false, "#t { line-height: 2; font-size: 10px }", "<div id='t'>x</div>"), margin: 0);

            Assert.Equal(20.0 * 0.75, FindById(root, "t")!.ActualBoxSizingHeight, 0.3);
        }

        [Fact]
        public async Task AUserStyleSheetIsNeverReadWithTheQuirks()
        {
            // A sheet the host supplies has no document to be in a mode: `width: 100` there is as invalid as anywhere else.
            var (document, _) = await LoadScreenAsync("<html><body style='margin:0'><div id='t'>x</div></body></html>",
                new HtmlDocumentOptions { UserStyleSheet = "#t { width: 100 }" });

            Assert.Equal(DocumentMode.Quirks, document.DocumentMode);
            var t = Descendants(document.Container.Root!).First(b => b.HtmlTag is { Name: "div" });
            Assert.True(t.ActualBoxSizingWidth > 400);
        }

        [Fact]
        public async Task AStyleSheetImportedByADocumentInQuirksModeIsReadWithTheQuirks()
        {
            var (root, _) = await LayoutAsync(
                "<html><head><style>@media all { #t { width: 100 } }</style></head><body style='margin:0'><div id='t'>x</div></body></html>", margin: 0);

            Assert.Equal(75.0, FindById(root, "t")!.ActualBoxSizingWidth, 0.01);
        }

        [Fact]
        public async Task ClassAndIdSelectorsIgnoreCaseOnlyInQuirksMode()
        {
            var css = ".Card { color: rgb(1,2,3) } #MAIN { background-color: rgb(4,5,6) }";
            var body = "<div id='main' class='card'>x</div>";
            var (quirks, _) = await LayoutAsync(Html(false, css, body), margin: 0);
            var (standards, _) = await LayoutAsync(Html(true, css, body), margin: 0);

            Assert.Equal("rgb(1, 2, 3)", FindById(quirks, "main")!.Color);
            Assert.Equal("rgb(4, 5, 6)", FindById(quirks, "main")!.BackgroundColor);
            Assert.NotEqual("rgb(1, 2, 3)", FindById(standards, "main")!.Color);
            Assert.NotEqual("rgb(4, 5, 6)", FindById(standards, "main")!.BackgroundColor);
        }

        [Fact]
        public async Task TheQuirksMediaFeatureIsTrueInQuirksModeAndNowhereElse()
        {
            var css = "@media (-peachpdf-quirks-mode) { #t { color: rgb(1,2,3) } } @media not all and (-peachpdf-quirks-mode) { #u { color: rgb(4,5,6) } }";
            var body = "<div id='t'>x</div><div id='u'>y</div>";
            var (quirks, _) = await LayoutAsync(Html(false, css, body), margin: 0);
            var (standards, _) = await LayoutAsync(Html(true, css, body), margin: 0);

            Assert.Equal("rgb(1, 2, 3)", FindById(quirks, "t")!.Color);
            Assert.NotEqual("rgb(1, 2, 3)", FindById(standards, "t")!.Color);
            Assert.NotEqual("rgb(4, 5, 6)", FindById(quirks, "u")!.Color);
            Assert.Equal("rgb(4, 5, 6)", FindById(standards, "u")!.Color);
        }

        [Fact]
        public async Task OnlyTheQuirksBlockOfTheUserAgentSheetIsQuirksOnlyForStyleSharing()
        {
            // A document in standards mode must not lose the sharing of a style among siblings to the structural selectors of the quirks block.
            var items = string.Concat(Enumerable.Repeat("<p>x</p>", 5));
            var (standards, container) = await LayoutAsync($"{Doctype}<html><body>{items}</body></html>", margin: 0);
            var paragraphs = Descendants(standards).Where(b => b.HtmlTag is { Name: "p" }).ToList();
            var (quirks, quirksContainer) = await LayoutAsync($"<html><body>{items}</body></html>", margin: 0);
            var quirkyParagraph = Descendants(quirks).First(b => b.HtmlTag is { Name: "p" });

            Assert.True(container.CssData!.CanShareStyleAmongSiblings(paragraphs[1]));
            Assert.False(quirksContainer.CssData!.CanShareStyleAmongSiblings(quirkyParagraph));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task TextDecorationDoesNotPropagateIntoATableInQuirksMode(bool quirks)
        {
            var html = (quirks ? "" : Doctype)
                + "<html><body style='margin:0'><div id='d' style='text-decoration:underline'>outer<table><tr><td>inner cell text</td></tr></table></div></body></html>";
            var (root, container) = await LayoutAsync(html, margin: 0);
            var d = FindById(root, "d")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, d, g);

            var lines = g.Log.OfType<TestRecordingGraphics.DrawLineCall>().ToList();
            // The one segment of "outer" alone when the table is left out; the table's line of text adds a second in standards mode.
            Assert.Equal(quirks ? 1 : 2, lines.Count);
        }

        [Fact]
        public async Task ATableHasItsOwnDecorationInQuirksModeStill()
        {
            var html = "<html><body style='margin:0'><table style='text-decoration:underline'><tr><td>inner cell text</td></tr></table></body></html>";
            var (root, container) = await LayoutAsync(html, margin: 0);
            var table = Descendants(root).First(b => b.HtmlTag is { Name: "table" });

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, table, g);

            Assert.NotEmpty(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
        }

        private static IEnumerable<CssBox> Descendants(CssBox box)
        {
            yield return box;
            foreach (var child in box.Boxes)
                foreach (var d in Descendants(child))
                    yield return d;
        }

        private static async Task<(HtmlDocument Document, Canvas Canvas)> LoadScreenAsync(string html, HtmlDocumentOptions? options = null)
        {
            var context = new HtmlRenderContext();
            var size = new Size(800, 600);
            options ??= new HtmlDocumentOptions();
            options.MediaType = MediaType.Screen;
            options.PageWidthPoints = size.Width;
            options.PageHeightPoints = size.Height;
            var document = await HtmlDocument.LoadAsync(html, context, options);
            var canvas = PdfCanvas.CreateMeasuring(context, size);
            await document.LayoutAsync(canvas);
            return (document, canvas);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task BareHoverMatchesALinkInEitherMode(bool doctype)
        {
            var html = Html(doctype, ":hover { color: rgb(255,0,0) }", "<a id='a' href='#'>link</a>");
            var (document, canvas) = await LoadScreenAsync(html);
            var bounds = document.GetElementBounds("a")!.Value;

            await document.SetPointerStateAsync(0, bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2), false, canvas);

            Assert.Equal("rgb(255, 0, 0)", Descendants(document.Container.Root!).First(b => b.HtmlTag is { Name: "a" }).Color);
        }

        [Theory]
        [InlineData(false, ":hover", false)]
        [InlineData(true, ":hover", true)]
        [InlineData(false, ":hover:active", false)]
        [InlineData(false, "div:hover", true)]
        [InlineData(false, ".x:hover", true)]
        [InlineData(false, ":hover.x", true)]
        public async Task ABareHoverMatchesOnlyLinksInQuirksMode(bool doctype, string selector, bool matchesDiv)
        {
            var html = Html(doctype, $"{selector} {{ color: rgb(255,0,0) }}", "<div id='d' class='x' style='width:100px;height:40px'>text</div>");
            var (document, canvas) = await LoadScreenAsync(html);
            var bounds = document.GetElementBounds("d")!.Value;

            await document.SetPointerStateAsync(0, bounds.X + 5, bounds.Y + 5, doctype && selector.Contains("active"), canvas);

            var div = Descendants(document.Container.Root!).First(b => b.HtmlTag is { Name: "div" });
            Assert.Equal(matchesDiv, div.Color == "rgb(255, 0, 0)");
        }

        [Fact]
        public async Task AnOverrideCanTakeADoctypelessDocumentOutOfQuirksMode()
        {
            var html = "<html><body style='margin:0;font-size:14px'><p id='p'>x</p></body></html>";
            var (document, _) = await LoadScreenAsync(html, new HtmlDocumentOptions { DocumentMode = DocumentMode.NoQuirks });

            Assert.Equal(DocumentMode.NoQuirks, document.DocumentMode);
            var p = Descendants(document.Container.Root!).First(b => b.HtmlTag is { Name: "p" });
            Assert.Equal(10.5, p.ActualMarginTop, 0.01);
        }

        [Fact]
        public async Task AnOverrideCanPutADoctypeDocumentInQuirksMode()
        {
            var html = Doctype + "<html><body style='margin:0;font-size:14px'><p id='p'>x</p></body></html>";
            var (document, _) = await LoadScreenAsync(html, new HtmlDocumentOptions { DocumentMode = DocumentMode.Quirks });

            Assert.Equal(DocumentMode.Quirks, document.DocumentMode);
            var p = Descendants(document.Container.Root!).First(b => b.HtmlTag is { Name: "p" });
            Assert.Equal(0.0, p.ActualMarginTop, 0.01);
        }

        [Theory]
        [InlineData("<!DOCTYPE html><html><body>x", DocumentMode.NoQuirks)]
        [InlineData("<html><body>x", DocumentMode.Quirks)]
        public async Task TheModeTheDocumentReportsIsTheOneItsDoctypeGave(string html, DocumentMode expected)
        {
            var (document, _) = await LoadScreenAsync(html);

            Assert.Equal(expected, document.DocumentMode);
        }

        [Fact]
        public async Task TheSubstantialSiblingTestSkipsGeneratedContentAndComments()
        {
            var (root, _) = await LayoutAsync(
                "<html><head><style>#p::before { content: 'g' }</style></head><body style='margin:0;font-size:14px'><!-- c --> <p id='p'>x</p></body></html>", margin: 0);
            var p = FindById(root, "p")!;

            Assert.False(QuirkSiblings.HasSubstantialSibling(p, following: false));
            Assert.False(QuirkSiblings.HasSubstantialSibling(p, following: true));
            Assert.False(QuirkSiblings.IsBlank(p));
        }
    }
}
