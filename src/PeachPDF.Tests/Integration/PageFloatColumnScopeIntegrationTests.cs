using PeachPDF.Html.Core.Dom;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using static PeachPDF.Tests.TestSupport.LayoutHarness;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <c>float-reference: column</c> on a page float (<c>float: top</c>/<c>bottom</c>/<c>top-bottom</c>/<c>snap</c>) places it
    /// at the block-start or block-end edge of the *column* its anchor falls in, and reserves room there for that
    /// column's content alone, rather than at the edge of the page (css-page-floats-3 section 3.1).
    /// </summary>
    /// <remarks>
    /// Fixture geometry: a 300pt-wide, 200pt-tall page with no margin; a 40pt heading; then a two-column container
    /// (140pt columns, 20pt gap) whose columns run from Y 40 to Y 200 - sixteen 10pt paragraphs each. The page float
    /// is 30pt tall.
    /// </remarks>
    public class PageFloatColumnScopeIntegrationTests
    {
        private const double ColumnBandTop = 40, PageBottom = 200, FloatHeight = 30, SecondColumnLeft = 160;

        private static string Document(string floatStyle, int linesBefore, int linesAfter = 30, string before = "", string after = "") =>
            "<style>body { margin: 0 } h1 { margin: 0; height: 40pt } " +
            "#mc { column-count: 2; column-gap: 20pt; column-fill: auto } " +
            "#mc p { margin: 0; height: 10pt; line-height: 10pt; font-size: 8pt }" +
            "#mc .fl { width: 50pt; height: 30pt; background: red }</style>" +
            "<h1>Heading</h1>" + before + "<div id='mc'>" +
            string.Concat(Enumerable.Range(1, linesBefore).Select(i => $"<p id='b{i}'>Before {i}</p>")) +
            $"<div id='f' class='fl' style='{floatStyle}'></div>" +
            string.Concat(Enumerable.Range(1, linesAfter).Select(i => $"<p id='a{i}'>After {i}</p>")) +
            "</div>" + after;

        private static Task<(CssBox Root, PeachPDF.Html.Core.HtmlContainerInt Container)> Lay(string html) =>
            LayoutAsync(html, pageWidth: 300, pageHeight: PageBottom, margin: 0);

        [Fact]
        public async Task ColumnTopFloat_SitsAtTheTopOfTheColumnItsAnchorIsIn()
        {
            var (root, _) = await Lay(Document("float: top; float-reference: column", linesBefore: 20));
            var f = FindById(root, "f")!;

            Assert.Equal(SecondColumnLeft, f.Location.X, 1);
            Assert.Equal(ColumnBandTop, f.Location.Y, 1);
            Assert.Equal(ColumnBandTop + FloatHeight, f.ActualBottom, 1);
        }

        [Fact]
        public async Task ColumnTopFloat_PushesOnlyItsOwnColumnsContentDown()
        {
            var (root, _) = await Lay(Document("float: top; float-reference: column", linesBefore: 20));

            // Column one is untouched: its first paragraph is at the top of the band.
            Assert.Equal(ColumnBandTop, FindById(root, "b1")!.Location.Y, 1);

            // Column two starts below the float. b17 is its first paragraph (sixteen fit in column one).
            var b17 = FindById(root, "b17")!;
            Assert.Equal(SecondColumnLeft, b17.Location.X, 1);
            Assert.Equal(ColumnBandTop + FloatHeight, b17.Location.Y, 1);
        }

        [Fact]
        public async Task PageReferencedTopFloat_StillPushesTheWholeContainerBelowThePagesTopEdge()
        {
            // The contrast: with the initial reference the strip is the page's, so the float is at the
            // page's top edge, above the heading, and the container starts below it.
            var (root, _) = await Lay(Document("float: top", linesBefore: 20));

            Assert.Equal(0, FindById(root, "f")!.Location.Y, 1);
            Assert.Equal(ColumnBandTop + FloatHeight, FindById(root, "b1")!.Location.Y, 1);
        }

        [Fact]
        public async Task ColumnBottomFloat_SitsAtTheBottomOfTheColumnAndStopsItsContentAboveIt()
        {
            var (root, _) = await Lay(Document("float: bottom; float-reference: column", linesBefore: 20));
            var f = FindById(root, "f")!;

            Assert.Equal(SecondColumnLeft, f.Location.X, 1);
            Assert.Equal(PageBottom - FloatHeight, f.Location.Y, 1);

            // Column two's paragraphs on this page all end above the float; column one's still use the full band.
            var inSecondColumn = Enumerable.Range(1, 20).Select(i => FindById(root, $"b{i}")!)
                .Where(p => p.Location.X > 100 && p.Location.Y < PageBottom).ToList();
            Assert.NotEmpty(inSecondColumn);
            Assert.All(inSecondColumn, p => Assert.True(p.ActualBottom <= PageBottom - FloatHeight + 0.5, $"{p.HtmlTag} {p.Location} overlaps the float"));

            var b16 = FindById(root, "b16")!;
            Assert.Equal(PageBottom, b16.ActualBottom, 1);
        }

        [Fact]
        public async Task EachColumnHasItsOwnStrip()
        {
            // One float anchored in each column: the first pushes column one's flow down, the second column two's.
            var (root, _) = await Lay(
                "<style>body { margin: 0 } h1 { margin: 0; height: 40pt } #mc { column-count: 2; column-gap: 20pt; column-fill: auto } " +
                "#mc p { margin: 0; height: 10pt; line-height: 10pt; font-size: 8pt } .fl { float: top; float-reference: column; width: 50pt; height: 30pt }</style>" +
                "<h1>Heading</h1><div id='mc'><div id='f1' class='fl'></div>" +
                string.Concat(Enumerable.Range(1, 20).Select(i => $"<p id='b{i}'>Before {i}</p>")) +
                "<div id='f2' class='fl'></div>" +
                string.Concat(Enumerable.Range(1, 30).Select(i => $"<p id='a{i}'>After {i}</p>")) + "</div>");

            var f1 = FindById(root, "f1")!;
            var f2 = FindById(root, "f2")!;

            Assert.Equal(0, f1.Location.X, 1);
            Assert.Equal(ColumnBandTop, f1.Location.Y, 1);
            Assert.Equal(SecondColumnLeft, f2.Location.X, 1);
            Assert.Equal(ColumnBandTop, f2.Location.Y, 1);

            // Column one lost 30pt, so it holds thirteen paragraphs; b14 is column two's first, again below its float.
            Assert.Equal(ColumnBandTop + FloatHeight, FindById(root, "b1")!.Location.Y, 1);
            var b14 = FindById(root, "b14")!;
            Assert.Equal(SecondColumnLeft, b14.Location.X, 1);
            Assert.Equal(ColumnBandTop + FloatHeight, b14.Location.Y, 1);
        }

        [Fact]
        public async Task ColumnReference_OutsideAMulticolContainer_FallsBackToThePage()
        {
            var (root, container) = await Lay(
                "<style>body { margin: 0 } p { margin: 0; height: 10pt } .fl { float: top; float-reference: column; width: 50pt; height: 30pt }</style>" +
                "<p>Before</p><div id='f' class='fl'></div><p id='after'>After</p>");

            Assert.Equal(0, FindById(root, "f")!.Location.Y, 1);
            Assert.Empty(container.TopFloatAreaHeightsByColumn);
            Assert.Equal(FloatHeight, container.TopFloatAreaHeightsBySlot[0], 1);

            // The strip is the page's, so the flow starts below it: the first paragraph, then the second.
            Assert.Equal(FloatHeight + 10, FindById(root, "after")!.Location.Y, 1);
        }

        [Fact]
        public async Task PageBottomFloat_StopsAMulticolContainersColumnsAboveIt()
        {
            // A page-referenced float's strip is the page's: the columns' own band ends above it, so none of
            // their content runs under the float.
            var (root, _) = await Lay(Document("float: bottom", linesBefore: 20));
            var f = FindById(root, "f")!;

            Assert.Equal(PageBottom - FloatHeight, f.Location.Y, 1);

            var onFirstPage = Enumerable.Range(1, 20).Select(i => FindById(root, $"b{i}")!).Where(p => p.Location.Y < PageBottom).ToList();
            Assert.All(onFirstPage, p => Assert.True(p.ActualBottom <= PageBottom - FloatHeight + 0.5, $"{p.Location} overlaps the float"));
        }

        /// <summary>
        /// Balanced columns of wrapping text with a bottom float first and a top float where the flow crosses
        /// into the second column - the shape a real document has, where the float holds text of its own.
        /// </summary>
        private const string TextDocument =
            "<style>body { margin: 0; font-family: sans-serif } h1 { margin: 0; height: 40pt } " +
            "#mc { column-count: 2; column-gap: 20pt } #mc p { margin: 0 0 6pt; font-size: 9pt; line-height: 12pt } " +
            ".fl { border: 1pt solid blue; padding: 6pt; font-size: 9pt; line-height: 12pt }</style>" +
            "<h1>Heading</h1><div id='mc'>" +
            "<div id='fb' class='fl' style='float: bottom; float-reference: column'>Bottom callout with text of its own that wraps.</div>" +
            "<p id='p1'>One. Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt.</p>" +
            "<p id='p2'>Two. Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt.</p>" +
            "<p id='p3'>Three. Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt.</p>" +
            "<p id='p4'>Four. Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt.</p>" +
            "<div id='ft' class='fl' style='float: top; float-reference: column'>Top figure with text of its own that wraps.</div>" +
            "<p id='p5'>Five. Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt.</p>" +
            "<p id='p6'>Six. Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt.</p>" +
            "<p id='p7'>Seven. Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt.</p>" +
            "<p id='p8'>Eight. Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt.</p></div>";

        [Fact]
        public async Task AFloatHoldingText_DoesNotPushTheFlowIntoTheNextColumn()
        {
            // The float's own words sit inside the strip it reserves. Measured against the column's band they
            // would all straddle it and break into the next column, taking the whole flow with them, and the
            // first column would be left empty.
            var (root, _) = await Lay(TextDocument);
            var fb = FindById(root, "fb")!;

            Assert.Equal(0, FindById(root, "p1")!.Location.X, 1);
            Assert.Equal(0, fb.Location.X, 1);
            Assert.True(fb.ActualBottom > fb.Location.Y + 20, "the callout keeps its text");
        }

        [Fact]
        public async Task ATopFloat_HasEveryBoxOfItsColumnStartBelowIt_IncludingAParagraphContinuingIntoTheColumn()
        {
            var (root, container) = await Lay(TextDocument);
            var ft = FindById(root, "ft")!;

            var inItsColumn = Enumerable.Range(1, 8).Select(i => FindById(root, $"p{i}")!)
                .Where(p => Math.Abs(p.Location.X - ft.Location.X) < 1 && p.Location.Y < PageBottom).ToList();

            Assert.NotEmpty(inItsColumn);
            Assert.All(inItsColumn, p => Assert.True(p.Location.Y >= ft.ActualBottom - 0.5,
                $"{p.Location} begins inside the strip that ends at {ft.ActualBottom}"));

            // The strip that moves the paragraph before its float between columns settles rather than
            // running the convergence loop to its cap.
            Assert.True(container.FootnoteLoopSettled);
        }

        [Fact]
        public async Task ABlockContinuingIntoAColumnWithATopFloat_BeginsBelowTheFloat()
        {
            // One paragraph of twenty-four 10pt lines: sixteen fill column one, the rest continue into column
            // two - where the float that follows it in the source is pinned to the top. The continuation is a
            // box that resumes there rather than one laid out afresh, and has to start below the strip too.
            var (root, _) = await Lay(
                "<style>body { margin: 0 } h1 { margin: 0; height: 40pt } #mc { column-count: 2; column-gap: 20pt; column-fill: auto } " +
                "#mc p { margin: 0; font-size: 8pt; line-height: 10pt } .fl { float: top; float-reference: column; width: 50pt; height: 30pt }</style>" +
                "<h1>Heading</h1><div id='mc'><p id='long'>" +
                string.Concat(Enumerable.Range(1, 24).Select(i => $"Line {i}<br>")) +
                "</p><div id='f' class='fl'></div><p id='after'>After</p></div>");

            var f = FindById(root, "f")!;
            Assert.Equal(SecondColumnLeft, f.Location.X, 1);
            Assert.Equal(ColumnBandTop, f.Location.Y, 1);

            var words = FindById(root, "long")!.Words.Concat(FindById(root, "long")!.Boxes.SelectMany(b => b.Words)).ToList();
            var inSecondColumn = words.Where(w => w.Left > 100).ToList();
            Assert.NotEmpty(inSecondColumn);
            Assert.All(inSecondColumn, w => Assert.True(w.Top >= ColumnBandTop + FloatHeight - 0.5,
                $"'{w.Text}' at Y {w.Top} is inside the strip"));
        }

        [Fact]
        public async Task ColumnTopBottomFloat_TakesTheBottomEdgeOnceTheColumnsTopIsFull()
        {
            // top-bottom: try the top; fall back to the bottom once the top strip has no room left. Two 30pt
            // floats in a 160pt column band fit at the top, so both are there.
            var (root, container) = await Lay(
                "<style>body { margin: 0 } h1 { margin: 0; height: 40pt } #mc { column-count: 2; column-gap: 20pt; column-fill: auto } " +
                "#mc p { margin: 0; height: 10pt; line-height: 10pt; font-size: 8pt } .fl { float: top-bottom; float-reference: column; width: 50pt; height: 30pt }</style>" +
                "<h1>Heading</h1><div id='mc'><div id='f1' class='fl'></div><div id='f2' class='fl'></div>" +
                string.Concat(Enumerable.Range(1, 20).Select(i => $"<p>Line {i}</p>")) + "</div>");

            Assert.Equal(ColumnBandTop, FindById(root, "f1")!.Location.Y, 1);
            Assert.Equal(ColumnBandTop + FloatHeight, FindById(root, "f2")!.Location.Y, 1);
            Assert.Equal(2 * FloatHeight, container.TopFloatAreaHeightsByColumn.Values.Single(), 1);
        }
    }
}
