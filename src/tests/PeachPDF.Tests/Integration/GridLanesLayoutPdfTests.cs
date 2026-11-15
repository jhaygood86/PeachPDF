using System.Globalization;
using System.Text.RegularExpressions;

using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Grid lanes (and the grid features that came with them) written to a PDF and read back from the fills they paint: every item has its own
    /// color, so the rectangle each one was drawn at - measured from the top left of the page, in points - is its laid-out box.
    /// </summary>
    public class GridLanesLayoutPdfTests
    {
        private record struct Box(double X, double Y, double W, double H);

        private static readonly Regex FillPattern = new(
            @"(?<r>\S+) (?<g>\S+) (?<b>\S+) rg\s+(?:/\S+ gs\s+)?(?<x>\S+) (?<y>\S+) (?<w>\S+) (?<h>\S+) re\s+f", RegexOptions.CultureInvariant);

        private const double PageHeight = 841.89;

        private static double N(string s) => double.Parse(s, CultureInfo.InvariantCulture);

        /// <summary>Renders <paramref name="body"/> on an A4 page with no margins and returns each filled rectangle by its color, <c>#0000NN</c> being item NN.</summary>
        private static async Task<Dictionary<int, Box>> Render(string body, string extraCss = "")
        {
            var html = "<!DOCTYPE html><html><head><style>@page { margin: 0 } body { margin: 0 } " + extraCss + "</style></head><body>" + body + "</body></html>";
            var file = (await new PdfGenerator().GeneratePdf(html, PageSize.A4)).Inspect();
            var result = new Dictionary<int, Box>();
            foreach (Match m in FillPattern.Matches(file.ContentText(file.Pages[0])))
            {
                var blue = (int)Math.Round(N(m.Groups["b"].Value) * 255);
                if (N(m.Groups["r"].Value) > 0.01 || N(m.Groups["g"].Value) > 0.01) continue;
                var h = N(m.Groups["h"].Value);
                result[blue] = new Box(N(m.Groups["x"].Value), PageHeight - N(m.Groups["y"].Value) - h, N(m.Groups["w"].Value), h);
            }

            return result;
        }

        private static string Item(int n, string style) => $"<div style='background:#0000{n:x2};{style}'></div>";

        private static void At(Dictionary<int, Box> boxes, int n, double x, double y, double? w = null, double? h = null)
        {
            Assert.True(boxes.ContainsKey(n), $"item {n} was not painted; painted: {string.Join(",", boxes.Keys)}");
            var box = boxes[n];
            Assert.True(Math.Abs(box.X - x) < 1.5 && Math.Abs(box.Y - y) < 1.5, $"item {n} at ({box.X:F1},{box.Y:F1}), expected ({x},{y})");
            if (w is { } width) Assert.True(Math.Abs(box.W - width) < 1.5, $"item {n} width {box.W:F1}, expected {width}");
            if (h is { } height) Assert.True(Math.Abs(box.H - height) < 1.5, $"item {n} height {box.H:F1}, expected {height}");
        }

        private static string Lanes(string style, params string[] items) =>
            $"<div style='display:grid-lanes; fit-tolerance:0; {style}'>" + string.Concat(items.Select((s, i) => Item(i + 1, s))) + "</div>";

        // ── Column lanes ───────────────────────────────────────────────────────

        [Fact]
        public async Task ColumnLanes_AutoFillOfIntrinsicTracks_CountsTheLanesTheItemsGive()
        {
            var b = await Render(Lanes("width:300pt; grid-template-columns:repeat(auto-fill, auto)",
                "width:100pt; height:50pt", "width:100pt; height:50pt", "width:100pt; height:50pt", "width:100pt; height:50pt"));
            At(b, 1, 0, 0); At(b, 2, 100, 0); At(b, 3, 200, 0); At(b, 4, 0, 50);
        }

        [Fact]
        public async Task ColumnLanes_AutoFitCollapsesTheLanesNoItemWentInto_AndTheOthersWiden()
        {
            var b = await Render(Lanes("width:300pt; grid-template-columns:repeat(auto-fit, auto)",
                "width:100pt; height:50pt", "width:100pt; height:50pt; grid-column:3", "width:100pt; height:50pt; grid-column:1"));
            At(b, 1, 0, 0); At(b, 3, 0, 50); At(b, 2, 150, 0);
        }

        [Fact]
        public async Task ColumnLanes_AnAutoPlacedSpanner_ContributesAtEveryStartLine()
        {
            var b = await Render(Lanes("width:400pt; grid-template-columns:50pt repeat(2, auto)",
                "width:200pt; height:20pt; grid-column:span 2", "width:30pt; height:20pt", "width:30pt; height:20pt"));
            Assert.Equal(3, b.Count);
        }

        [Fact]
        public async Task ColumnLanes_PercentageWidths_AreOfTheLaneNotOfTheLaneSizing()
        {
            var b = await Render(Lanes("width:360pt; grid-template-columns:repeat(auto-fill, auto)",
                "width:25%; height:100pt", "width:25%; height:100pt", "width:25%; height:100pt"));
            Assert.Equal(3, b.Count);
        }

        [Fact]
        public async Task ColumnLanes_DenseBackfill_MovesADefiniteItemUpWithinItsLane()
        {
            var b = await Render(Lanes("width:300pt; grid-template-columns:100pt 100pt 100pt; grid-lanes-pack:dense",
                "height:60pt", "height:20pt", "height:20pt", "height:15pt; grid-column:span 2", "height:10pt; grid-column:2"));
            At(b, 1, 0, 0); At(b, 2, 100, 0); At(b, 3, 200, 0);
            Assert.True(b.ContainsKey(5));
        }

        [Fact]
        public async Task ColumnLanes_FillReverse_StacksFromTheEnd()
        {
            var b = await Render(Lanes("width:200pt; height:200pt; grid-template-columns:100pt 100pt; grid-lanes-direction:column fill-reverse",
                "height:40pt", "height:60pt", "height:30pt"));
            At(b, 1, 0, 160); At(b, 2, 100, 140);
        }

        [Fact]
        public async Task ColumnLanes_TrackReverse_NumbersTheLanesFromTheEnd()
        {
            var b = await Render(Lanes("width:200pt; grid-template-columns:100pt 100pt; grid-lanes-direction:column track-reverse",
                "height:40pt", "height:60pt"));
            At(b, 1, 100, 0); At(b, 2, 0, 0);
        }

        [Fact]
        public async Task ColumnLanes_AlignSelfAndJustifySelf_PlaceItemsInTheirLaneAndTheRoomBelow()
        {
            var b = await Render(Lanes("width:200pt; height:200pt; grid-template-columns:100pt 100pt; align-content:start",
                "height:100pt", "height:40pt; width:50pt; justify-self:end; align-self:stretch", "height:30pt; align-self:center"));
            At(b, 1, 0, 0); At(b, 2, 150, 0);
            Assert.True(b.ContainsKey(3));
        }

        [Fact]
        public async Task ColumnLanes_Baselines_ShareTheLanesBaselineInTheGridAxis()
        {
            var b = await Render(
                "<div style='display:grid-lanes; width:300pt; grid-template-columns:100pt 100pt; justify-items:baseline; fit-tolerance:0; font:10pt Arial'>" +
                "<div style='background:#000001; width:40pt; height:20pt'>A</div><div style='background:#000002; width:40pt; height:20pt'>B</div></div>");
            Assert.Equal(2, b.Count);
        }

        [Fact]
        public async Task ColumnLanes_ASubgridItem_AdoptsTheLanesItSpans()
        {
            var b = await Render(
                "<div style='display:grid-lanes; width:400pt; grid-template-columns:100pt 200pt; fit-tolerance:0; gap:10pt'>" +
                "<div style='grid-column:span 2; display:grid; grid-template-columns:subgrid; column-gap:30pt'>" + Item(1, "height:10pt") + Item(2, "height:10pt") + "</div></div>");
            // The subgrid's own 30pt gap is 20pt wider than the lanes' 10pt, so each side of the line takes half the difference as margin.
            At(b, 1, 0, 0); At(b, 2, 120, 0);
        }

        [Fact]
        public async Task ColumnLanes_AFixedColumnWidthThatFitsOneRepeat_StillCountsDefinitely()
        {
            var b = await Render(Lanes("width:210pt; grid-template-columns:repeat(auto-fill, 100pt); gap:10pt",
                "height:10pt", "height:10pt", "height:10pt"));
            At(b, 1, 0, 0); At(b, 2, 110, 0); At(b, 3, 0, 20);
        }

        // ── Row lanes ──────────────────────────────────────────────────────────

        [Fact]
        public async Task RowLanes_AutoRowsAreAsTallAsTheTallestAutoPlacedItem()
        {
            var b = await Render(Lanes("width:300pt; height:300pt; align-content:start; grid-template-rows:repeat(3, auto)",
                "width:100pt; height:20pt", "width:100pt; height:40pt"));
            At(b, 1, 0, 0); At(b, 2, 0, 40);
        }

        [Fact]
        public async Task RowLanes_AutoFillOfIntrinsicRows_IsCountedFromTheItemHeights()
        {
            var b = await Render(Lanes("width:300pt; height:300pt; grid-template-rows:repeat(auto-fill, min-content); grid-lanes-direction:row",
                "width:100pt; height:100pt", "width:100pt; height:100pt", "width:100pt; height:100pt", "width:100pt; height:100pt"));
            At(b, 1, 0, 0); At(b, 2, 0, 100); At(b, 3, 0, 200); At(b, 4, 100, 0);
        }

        [Fact]
        public async Task RowLanes_AutoFitCollapsesEmptyRows()
        {
            var b = await Render(Lanes("width:300pt; height:300pt; grid-template-rows:repeat(auto-fit, auto); grid-lanes-direction:row; align-content:start",
                "width:100pt; height:50pt; grid-row:1", "width:100pt; height:50pt; grid-row:3"));
            Assert.Equal(2, b.Count);
        }

        [Fact]
        public async Task RowLanes_ASpanningAutoPlacedItem_SharesItsHeightOverTheLanesItMayCover()
        {
            var b = await Render(Lanes("width:300pt; height:300pt; grid-template-rows:repeat(3, auto); grid-lanes-direction:row; align-content:start; gap:0",
                "width:100pt; height:90pt; grid-row:span 2", "width:100pt; height:10pt"));
            Assert.Equal(2, b.Count);
        }

        [Fact]
        public async Task RowLanes_DefiniteDenseBackfill_AndTrackReverse()
        {
            var b = await Render(Lanes("width:300pt; height:200pt; grid-template-rows:50pt 50pt; grid-lanes-direction:row track-reverse; grid-lanes-pack:dense",
                "width:100pt", "width:60pt; grid-row:1"));
            Assert.Equal(2, b.Count);
        }

        [Fact]
        public async Task RowLanes_RowSubgrid_AdoptsTheLanes()
        {
            var b = await Render(
                "<div style='display:grid-lanes; grid-lanes-direction:row; width:300pt; height:200pt; grid-template-rows:60pt 80pt; fit-tolerance:0; gap:10pt'>" +
                "<div style='grid-row:span 2; width:100pt; display:grid; grid-template-rows:subgrid'>" + Item(1, "") + Item(2, "") + "</div></div>");
            At(b, 1, 0, 0, null, 60); At(b, 2, 0, 70, null, 80);
        }

        // ── Plain grid ─────────────────────────────────────────────────────────

        [Fact]
        public async Task Grid_AutoFitTracksWithAnAutoMaximum_StretchOnceTheEmptyOnesCollapse()
        {
            var b = await Render(
                "<div style='display:grid; width:200pt; height:200pt; gap:10pt; grid-template-columns:repeat(auto-fit,minmax(25pt,auto)); grid-template-rows:repeat(auto-fit,minmax(25pt,auto))'>" +
                Item(1, "grid-row:2; grid-column:3") + Item(2, "grid-row:3; grid-column:4") + "</div>");
            At(b, 1, 0, 0, 95, 95); At(b, 2, 105, 105, 95, 95);
        }

        [Fact]
        public async Task Grid_StaticPosition_IsAlignedInTheContentBoxNotInTheNamedArea()
        {
            var b = await Render(
                "<div style='display:grid; width:100pt; height:100pt; grid:20pt 40pt 20pt / 20pt 40pt 20pt; align-items:center; justify-items:end'>" +
                Item(1, "position:absolute; grid-area:2/2/3/3; width:10pt; height:10pt") + "</div>");
            At(b, 1, 90, 45);
        }

        [Fact]
        public async Task Grid_StaticPosition_InAVerticalGrid_UsesTheBlockAxisAcrossThePage()
        {
            var b = await Render(
                "<div style='display:grid; writing-mode:vertical-rl; width:100pt; height:100pt; align-items:start; justify-items:end'>" +
                Item(1, "position:absolute; width:10pt; height:10pt") + "</div>");
            At(b, 1, 90, 90);
        }

        [Fact]
        public async Task Grid_ContainingArea_OfAVerticalGrid_IsTheNamedLines()
        {
            var b = await Render(
                "<div style='display:grid; position:relative; writing-mode:vertical-rl; width:60pt; height:60pt; grid:10pt 20pt 10pt / 10pt 20pt 10pt'>" +
                Item(1, "position:absolute; grid-area:2/2/3/3; top:0; left:0; width:20pt; height:20pt") + "</div>");
            At(b, 1, 30, 10, 20, 20);
        }

        [Fact]
        public async Task GridLanes_StaticPositionAndAbsolutelyPositionedChildren()
        {
            var b = await Render(
                "<div style='display:grid-lanes; position:relative; width:200pt; height:100pt; grid-template-columns:100pt 100pt'>" +
                Item(1, "height:20pt") + Item(2, "position:absolute; width:10pt; height:10pt; right:0; bottom:0") + "</div>");
            At(b, 2, 190, 90);
        }

        // ── Properties that name lanes ─────────────────────────────────────────

        [Fact]
        public async Task GridLanesShorthand_AndTwoKeywordDisplay_LayOutLanes()
        {
            var b = await Render(
                "<div style='display:inline grid-lanes; grid-lanes: 100pt 100pt; fit-tolerance:0'>" + Item(1, "height:30pt") + Item(2, "height:10pt") + Item(3, "height:10pt") + "</div>");
            At(b, 1, 0, 0); At(b, 2, 100, 0); At(b, 3, 100, 10);
        }
    }
}
