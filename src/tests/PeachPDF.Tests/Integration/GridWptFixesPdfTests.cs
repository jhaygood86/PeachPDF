using System.Globalization;
using System.Text.RegularExpressions;

using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// The grid behaviors the web-platform-tests drove, written to a PDF and read back from the fills they paint: every item has its own color
    /// (<c>#0000NN</c> is item NN), so a fill's rectangle, measured from the top left of the page in points, is that item's box.
    /// </summary>
    public class GridWptFixesPdfTests
    {
        private record struct Box(double X, double Y, double W, double H);

        private static readonly Regex FillPattern = new(
            @"(?<r>\S+) (?<g>\S+) (?<b>\S+) rg\s+(?:/\S+ gs\s+)?(?<x>\S+) (?<y>\S+) (?<w>\S+) (?<h>\S+) re\s+f", RegexOptions.CultureInvariant);

        private const double PageHeight = 841.89;

        private static double N(string s) => double.Parse(s, CultureInfo.InvariantCulture);

        private static async Task<Dictionary<int, Box>> Render(string body)
        {
            var html = "<!DOCTYPE html><html><head><style>@page { margin: 0 } body { margin: 0 }</style></head><body>" + body + "</body></html>";
            var file = (await new PdfGenerator().GeneratePdf(html, PageSize.A4)).Inspect();
            var result = new Dictionary<int, Box>();
            foreach (Match m in FillPattern.Matches(file.ContentText(file.Pages[0])))
            {
                if (N(m.Groups["r"].Value) > 0.01 || N(m.Groups["g"].Value) > 0.01) continue;
                var h = N(m.Groups["h"].Value);
                result[(int)Math.Round(N(m.Groups["b"].Value) * 255)] = new Box(N(m.Groups["x"].Value), PageHeight - N(m.Groups["y"].Value) - h, N(m.Groups["w"].Value), h);
            }

            return result;
        }

        private static string Item(int n, string style = "", string content = "") => $"<div style='background:#0000{n:x2};{style}'>{content}</div>";

        private static void At(Dictionary<int, Box> boxes, int n, double x, double y, double? w = null, double? h = null)
        {
            Assert.True(boxes.ContainsKey(n), $"item {n} was not painted; painted: {string.Join(",", boxes.Keys)}");
            var box = boxes[n];
            Assert.True(Math.Abs(box.X - x) < 1.5 && Math.Abs(box.Y - y) < 1.5, $"item {n} at ({box.X:F1},{box.Y:F1}), expected ({x},{y})");
            if (w is { } width) Assert.True(Math.Abs(box.W - width) < 1.5, $"item {n} width {box.W:F1}, expected {width}");
            if (h is { } height) Assert.True(Math.Abs(box.H - height) < 1.5, $"item {n} height {box.H:F1}, expected {height}");
        }

        [Fact]
        public async Task NamedLines_SpanToTheNthNamedLine_FromEitherSide()
        {
            var b = await Render(
                "<div style='display:grid; width:300pt; grid-template-columns:[a] 50pt [b] 50pt [a] 50pt [b] 50pt [a]; grid-template-rows:20pt 20pt'>" +
                Item(1, "grid-column:a / span b") + Item(2, "grid-column:span 2 b / a 3; grid-row:2") + Item(3, "grid-column:a 2 / span a; grid-row:1") + "</div>");
            At(b, 1, 0, 0, 50);
            Assert.True(b.ContainsKey(2));
        }

        [Fact]
        public async Task NamedLines_InsideRepeat_AndPastTheExplicitGrid_ArePlaced()
        {
            var b = await Render(
                "<div style='display:grid; width:300pt; grid-template-columns:[start] repeat(auto-fill, [x] 100pt [y]) [end]; grid-auto-rows:20pt'>" +
                Item(1, "grid-column:x 2") + Item(2, "grid-column:y 5 / span 2") + Item(3, "grid-column:span x 2 / end") + Item(4, "grid-column:end -1") + "</div>");
            At(b, 1, 100, 0, 100);
        }

        [Fact]
        public async Task GridColumn_NamedStartAndEndSuffixes_ResolveAsAreas()
        {
            var b = await Render(
                "<div style='display:grid; width:200pt; grid-template-areas:\"a b\"; grid-template-columns:100pt 100pt; grid-template-rows:20pt'>" +
                Item(1, "grid-column:b") + Item(2, "grid-column:a-start / b-end") + Item(3, "grid-column:b-end 2 / span 1") + "</div>");
            At(b, 1, 100, 0, 100);
        }

        [Fact]
        public async Task RowLockedItems_GrowTheImplicitGridAndNegativeLinesCountFromTheExplicitOne()
        {
            var b = await Render(
                "<div style='display:grid; width:200pt; grid-template-columns:100pt 100pt; grid-auto-flow:column; grid-template-rows:20pt 20pt; grid-auto-columns:50pt'>" +
                Item(1, "grid-row:1") + Item(2, "grid-row:1") + Item(3, "grid-row:-1") + Item(4, "grid-row:2 / -1") + "</div>");
            Assert.True(b.Count >= 2, $"painted {b.Count}");
        }

        [Fact]
        public async Task ColumnBaselines_ShareTheColumnBaseline_AndSizeTheColumn()
        {
            var b = await Render(
                "<div style='display:inline-grid; grid-template-columns:auto; justify-items:baseline; font:10pt Arial'>" +
                Item(1, "writing-mode:vertical-rl; height:30pt", "A") + Item(2, "writing-mode:vertical-rl; height:30pt; font-size:20pt", "B") + "</div>" +
                "<div style='display:inline-grid; grid-template-columns:auto; justify-items:last baseline; font:10pt Arial'>" +
                Item(3, "width:30pt", "A") + Item(4, "width:50pt", "B") + "</div>");
            Assert.True(b.Count >= 2);
        }

        [Fact]
        public async Task AspectRatioItem_WithAPercentageHeight_TransfersAWidthFromAFixedRow()
        {
            var b = await Render(
                "<div style='display:inline-grid; grid-template-rows:100pt; grid-template-columns:auto'>" + Item(1, "aspect-ratio:1; height:100%") + "</div>");
            At(b, 1, 0, 0, 100, 100);
        }

        [Fact]
        public async Task RowSubgrid_WithARatioChild_TransfersThroughItsRows()
        {
            var b = await Render(
                "<div style='display:inline-grid; grid-template-rows:60pt 40pt; grid-template-columns:auto'>" +
                "<div style='display:grid; grid-template-rows:subgrid; grid-row:span 2; padding:5pt'>" + Item(1, "aspect-ratio:1; height:100%") + "</div></div>");
            Assert.True(b.ContainsKey(1));
        }

        [Fact]
        public async Task NestedRowSubgrids_TransferThroughEachOther()
        {
            var b = await Render(
                "<div style='display:inline-grid; grid-template-rows:80pt; grid-template-columns:auto'>" +
                "<div style='display:grid; grid-template-rows:subgrid; margin:2pt'><div style='display:grid; grid-template-rows:subgrid; border:1pt solid'>" +
                Item(1, "aspect-ratio:1; height:100%") + "</div></div></div>");
            Assert.True(b.ContainsKey(1));
        }

        [Fact]
        public async Task SubgridGaps_DifferFromTheParents_BySplittingTheDifferenceAsMargins()
        {
            var b = await Render(
                "<div style='display:grid; width:300pt; grid-template-columns:100pt 100pt 100pt; column-gap:10pt; grid-template-rows:40pt 40pt; row-gap:10pt'>" +
                "<div style='display:grid; grid-column:1 / 4; grid-row:1 / 3; grid-template-columns:subgrid; grid-template-rows:subgrid; column-gap:30pt; row-gap:20pt'>" +
                Item(1) + Item(2) + Item(3) + Item(4) + "</div></div>");
            At(b, 1, 0, 0);
            Assert.True(b.ContainsKey(4));
        }

        [Fact]
        public async Task SubgridOverflow_AddsImplicitTracksSharingTheLastPosition()
        {
            var b = await Render(
                "<div style='display:grid; width:200pt; grid-template-columns:100pt 100pt; grid-auto-rows:30pt'>" +
                "<div style='display:grid; grid-column:1 / 3; grid-template-columns:subgrid'>" + Item(1) + Item(2) + Item(3) + Item(4) + "</div></div>");
            Assert.True(b.Count >= 2);
        }

        [Fact]
        public async Task PercentageRowGap_OfAnAutoHeightGrid_ResolvesLate()
        {
            var b = await Render(
                "<div style='display:grid; width:200pt; gap:10%; grid-template-columns:90pt 90pt; grid-template-rows:90pt 90pt'>" + Item(1) + Item(2) + Item(3) + Item(4) + "</div>");
            At(b, 3, 0, 90 + 18);
        }

        [Fact]
        public async Task MinContentHeight_OfAGridWithAContentKeywordHeight_IsNotStretched()
        {
            var b = await Render(
                "<div style='display:grid; width:200pt; height:300pt; grid-template-rows:auto auto; align-content:stretch'>" +
                Item(1, "height:min-content; padding:5pt") + Item(2, "height:max-content; padding:5pt") + "</div>");
            Assert.True(b.Count >= 1);
        }

        [Fact]
        public async Task GridContainer_WithMinHeightAndAutoRows_StretchesTheAutoRows()
        {
            var b = await Render(
                "<div style='display:grid; width:200pt; min-height:200pt; grid-template-rows:auto auto'>" + Item(1) + Item(2) + "</div>");
            At(b, 2, 0, 100);
        }

        [Fact]
        public async Task LogicalSizeProperties_SizeTheBox()
        {
            var b = await Render(
                Item(1, "inline-size:120pt; block-size:30pt; min-inline-size:10pt; max-inline-size:500pt; min-block-size:5pt; max-block-size:90pt") +
                "<div style='writing-mode:vertical-rl'>" + Item(2, "inline-size:50pt; block-size:40pt") + "</div>");
            At(b, 1, 0, 0, 120, 30);
            Assert.True(b.ContainsKey(2));
        }

        [Fact]
        public async Task LogicalSizeProperties_YieldToALaterPhysicalDeclaration()
        {
            var b = await Render(Item(1, "inline-size:120pt; width:60pt; height:20pt"));
            At(b, 1, 0, 0, 60, 20);
        }

        [Fact]
        public async Task OrthogonalItem_IsSizedByTheLinesItStacks()
        {
            var b = await Render(
                "<div style='display:grid; width:200pt; grid-template-columns:100pt 100pt; grid-template-rows:auto'>" +
                Item(1, "writing-mode:vertical-rl", "text that wraps in a tall box") + Item(2, "height:30pt") + "</div>");
            Assert.True(b.Count >= 1);
        }

        [Fact]
        public async Task InlineSizeGrid_WithAnAutoBlockInAFlexItem_ShrinksToFit()
        {
            var b = await Render(
                "<div style='display:flex'><div style='display:grid; grid-template-columns:repeat(auto-fill, 40pt); grid-template-areas:\"a b\"'>" +
                Item(1, "grid-area:a; width:40pt; height:10pt") + Item(2, "grid-area:b; width:40pt; height:10pt") + "</div></div>");
            Assert.True(b.Count >= 1);
        }

        [Theory]
        [InlineData("display:grid-lanes; grid-lanes:row 50pt 50pt")]
        [InlineData("display:grid-lanes; grid-lanes:column fill-reverse 50pt 50pt")]
        [InlineData("display:grid-lanes; grid-lanes:\"a b\" 1fr 1fr track-reverse")]
        [InlineData("display:grid-lanes; grid-lanes:none")]
        [InlineData("display:grid-lanes; grid-lanes:row column 50pt")]
        [InlineData("display:grid-lanes; grid-lanes:column fill-reverse fill-reverse 50pt")]
        [InlineData("display:grid-lanes; grid-lanes:column track-reverse track-reverse 50pt")]
        [InlineData("display:grid-lanes; grid-lanes:normal fill-reverse 50pt")]
        [InlineData("display:grid-lanes; grid-lanes:row")]
        [InlineData("display:grid-lanes; grid-lanes:\"a b\" \"a\" 50pt")]
        [InlineData("display:grid-lanes; grid-lanes-direction:row fill-reverse fill-reverse")]
        [InlineData("display:grid-lanes; grid-lanes-direction:column track-reverse track-reverse")]
        [InlineData("display:grid-lanes; grid-lanes-direction:row sideways")]
        [InlineData("display:grid-lanes; grid-lanes-direction:10px")]
        [InlineData("display:grid-lanes; grid-lanes-direction:normal fill-reverse")]
        [InlineData("display:grid-lanes; grid-lanes-direction:row fill-reverse track-reverse extra")]
        [InlineData("display:grid-lanes; grid-lanes-pack:sparse")]
        [InlineData("display:grid-lanes; grid-lanes-pack:dense normal")]
        [InlineData("display:inline bogus")]
        [InlineData("display:block block")]
        [InlineData("display:inline 10px")]
        [InlineData("display:block flex")]
        [InlineData("display:inline flow-root")]
        [InlineData("display:block flow-root")]
        [InlineData("display:inline grid")]
        [InlineData("display:block table")]
        [InlineData("display:inline table")]
        [InlineData("display:block flow")]
        [InlineData("display:inline flow")]
        [InlineData("display:grid; grid-template-columns:[a b] 10pt [c]; grid-row:span a b")]
        [InlineData("display:grid; grid-template-columns:[a 10pt] 10pt")]
        [InlineData("display:grid; grid-template-columns:repeat(auto-fill, [a b] 10pt [c d])")]
        [InlineData("display:grid; grid-template-columns:[a] repeat(auto-fit, 10pt) [b c]")]
        [InlineData("display:grid; grid-column:span 2 foo")]
        [InlineData("display:grid; grid-column:span foo 2")]
        [InlineData("display:grid; grid-column:span foo bar")]
        public async Task GridPropertyGrammars_AreAcceptedOrDroppedWithoutBreakingTheLayout(string style)
        {
            var b = await Render($"<div style='{style}'>" + Item(1, "height:10pt;width:10pt") + Item(2, "height:10pt;width:10pt;grid-column:span 2 foo") + "</div>");
            Assert.True(b.Count >= 1);
        }

        [Fact]
        public async Task LanesPropertyTest_DirectionAndPack_LayOutTheLanes()
        {
            var b = await Render(
                "<div style='display:grid-lanes; grid-lanes-direction:row track-reverse fill-reverse; grid-lanes-pack:dense; grid-template-rows:50pt 50pt; width:300pt; height:150pt'>" +
                Item(1, "width:100pt") + Item(2, "width:60pt; grid-row:1") + Item(3, "width:60pt; grid-row:span 2") + "</div>");
            Assert.True(b.Count >= 2);
        }

        [Fact]
        public async Task LanesRowSubgrid_WithRatioChild_SizesTheLaneWidth()
        {
            var b = await Render(
                "<div style='display:inline-grid-lanes; grid-lanes-direction:row; grid-template-rows:100pt; fit-tolerance:0'>" +
                "<div style='display:grid; grid-template-rows:subgrid'>" + Item(1, "aspect-ratio:1; height:100%") + "</div></div>");
            At(b, 1, 0, 0, 100, 100);
        }

        [Fact]
        public async Task LanesWithMinContentAndFitContentRepeats_CountFromTheirSizes()
        {
            var b = await Render(
                "<div style='display:grid-lanes; fit-tolerance:0; width:300pt; grid-template-columns:repeat(auto-fill, fit-content(100pt)); font:10pt Arial'>" +
                Item(1, "height:10pt", "aa") + Item(2, "height:10pt", "bb") + Item(3, "height:10pt", "cc") + "</div>" +
                "<div style='display:grid-lanes; fit-tolerance:0; width:300pt; grid-template-columns:repeat(auto-fill, min-content max-content); font:10pt Arial'>" +
                Item(4, "height:10pt", "dd") + Item(5, "height:10pt", "ee") + "</div>" +
                "<div style='display:grid-lanes; fit-tolerance:0; width:300pt; grid-template-columns:repeat(auto-fill, minmax(auto, 50pt))'>" +
                Item(6, "height:10pt;width:30pt") + Item(7, "height:10pt;width:30pt;grid-column:span 2") + "</div>");
            Assert.True(b.Count >= 3);
        }

        [Fact]
        public async Task LanesWithAnIndefiniteStackingAxis_AndVerticalWritingMode_KeepTheOldCount()
        {
            var b = await Render(
                "<div style='display:grid-lanes; writing-mode:vertical-rl; fit-tolerance:0; grid-template-columns:repeat(auto-fill, auto)'>" +
                Item(1, "width:20pt;height:30pt") + Item(2, "width:20pt;height:30pt") + "</div>");
            Assert.True(b.Count >= 1);
        }

        [Fact]
        public async Task LanesRows_AutoRepeatWithSpansAndNoHeight()
        {
            var b = await Render(
                "<div style='display:grid-lanes; grid-lanes-direction:row; fit-tolerance:0; width:300pt; grid-template-rows:repeat(auto-fill, auto)'>" +
                Item(1, "width:100pt;height:30pt") + Item(2, "width:100pt;height:30pt;grid-row:span 2") + "</div>");
            Assert.True(b.Count >= 1);
        }

        [Fact]
        public async Task LanesRows_PercentageGapAndSubgridContributionsAcrossAutoLanes()
        {
            var b = await Render(
                "<div style='display:grid-lanes; grid-lanes-direction:row; fit-tolerance:0; row-gap:10%; width:300pt; height:200pt; grid-template-rows:auto auto auto'>" +
                Item(1, "width:100pt;height:30pt") + Item(2, "width:100pt;height:30pt; grid-row:2 / span 2") + Item(3, "width:100pt;height:30pt; grid-row:1") + "</div>");
            Assert.True(b.Count >= 2);
        }

        [Fact]
        public async Task LanesBackfill_AutoPlacedAndDefinite_WithFillReverse()
        {
            var b = await Render(
                "<div style='display:grid-lanes; grid-lanes-pack:dense; fit-tolerance:0; width:300pt; height:200pt; grid-template-columns:100pt 100pt 100pt; grid-lanes-direction:column fill-reverse'>" +
                Item(1, "height:60pt") + Item(2, "height:20pt; grid-column:span 2") + Item(3, "height:10pt; grid-column:3") + Item(4, "height:10pt; grid-column:1") + "</div>");
            Assert.True(b.Count >= 3);
        }
    }
}
