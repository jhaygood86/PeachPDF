using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.PdfSharpCore.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    public class MulticolLayoutIntegrationTests
    {
        // ─── Establishing a multicol context ───────────────────────────────────────

        [Fact]
        public async Task ColumnCount_EstablishesMultiColumnContext()
        {
            var box = await FindByIdAsync("<div id='mc' style='column-count:2'></div>", "mc");
            Assert.True(box.EstablishesMultiColumnContext);
        }

        [Fact]
        public async Task ColumnWidth_EstablishesMultiColumnContext()
        {
            var box = await FindByIdAsync("<div id='mc' style='column-width:100px'></div>", "mc");
            Assert.True(box.EstablishesMultiColumnContext);
        }

        [Fact]
        public async Task NoColumnProperties_DoesNotEstablishMultiColumnContext()
        {
            var box = await FindByIdAsync("<div id='mc'></div>", "mc");
            Assert.False(box.EstablishesMultiColumnContext);
        }

        [Fact]
        public async Task NoChildren_ActualBottomEqualsLocationPlusBoxSizeIncludedHeight()
        {
            // A genuinely childless box (Boxes.Count == 0) never reaches CssLayoutEngineColumns at all -
            // the CssBox.PerformLayoutImp dispatch gate itself requires Boxes.Count > 0. To reach
            // Layout's own internal "no substantive children" branch, the box needs a child that passes
            // the outer dispatch gate but gets filtered out by Layout's own Display/IsOutOfFlow/
            // IsSpaceOrEmpty check - a display:none child does exactly that.
            var box = await FindByIdAsync(
                "<div id='mc' style='columns:2; padding:5px'><span style='display:none'>hidden</span></div>", "mc");

            Assert.Equal(box.Location.Y + box.ActualBoxSizeIncludedHeight, box.ActualBottom);
        }

        [Fact]
        public async Task ImageWithBlockSibling_InMulticolContainer_ActuallyPaints()
        {
            // CssLayoutEngineColumns.Layout uses the exact same (b.HtmlTag != null || !b.IsSpaceOrEmpty)
            // item filter as CssLayoutEngineFlex - a replaced element sharing a multicol container with a
            // block-level sibling gets wrapped in an anonymous box (DomParser.CorrectInlineBoxesParent,
            // per CSS2.1 §9.2.1.1's "block container either contains only block-level boxes, or
            // establishes an inline formatting context") and was dropped by the same shallow
            // IsSpaceOrEmpty check the flex fix addressed - see FlexReplacedElementIntegrationTests for
            // the full root-cause writeup.
            const string pngDataUri =
                "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR42mP4/58BAAT/Af9jgNErAAAAAElFTkSuQmCC";
            var html = Wrap($"""
                <div id='mc' style='columns:2; width:200px'>
                    <img id='i' src='{pngDataUri}' width='40' height='30'>
                    <div class='title'>Title</div>
                </div>
                """);
            var (root, container) = await BuildAndLayout(html, pageHeight: 1000);

            var g = new PeachPDF.Tests.TestSupport.TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, root, g);

            var call = Assert.Single(g.DrawImageCalls);
            // HTML width/height attributes are px lengths (TranslateAttributes -> "40px"/"30px"),
            // and 1px = 0.75pt, so the painted destination rect is 40*0.75 x 30*0.75 layout units.
            Assert.Equal(30, call.DestRect.Width, 1);
            Assert.Equal(22.5, call.DestRect.Height, 1);
        }

        [Fact]
        public async Task ColumnCount1_DegeneratesToOrdinaryBlockFlow()
        {
            var html = Wrap(@"
                <div id='mc' style='columns:1; width:200px'>
                    <div class='item' style='height:20px'></div>
                    <div class='item' style='height:20px'></div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 1000);
            var items = FindAllByClass(root, "item");
            var mc = FindById(root, "mc")!;

            Assert.Equal(2, items.Count);
            // Ordinary block flow: both items stack vertically at the same X, second directly below first.
            Assert.Equal(items[0].Location.X, items[1].Location.X);
            Assert.True(items[1].Location.Y > items[0].Location.Y);
            Assert.Equal(mc.ActualBottom, items[1].ActualBottom);
        }

        [Fact]
        public async Task ColumnCountAndWidthBothSpecified_CountActsAsMaximum()
        {
            // column-count is a maximum: never more columns than fit at >= column-width, so a
            // wide column-width here caps the actual column count below the requested count.
            var html = Wrap(@"
                <div id='mc' style='column-count:5; column-width:80px; column-gap:0; width:200px'>
                    <div class='item' style='height:20px'></div>
                    <div class='item' style='height:20px'></div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 1000);
            var items = FindAllByClass(root, "item");

            // 200px / 80px => at most 2 columns fit, even though column-count asked for 5.
            var distinctX = items.Select(i => System.Math.Round(i.Location.X)).Distinct().ToList();
            Assert.True(distinctX.Count <= 2);
        }

        // ─── Basic column geometry ──────────────────────────────────────────────────

        [Fact]
        public async Task ColumnCount2_SplitsChildrenAcrossTwoXPositions()
        {
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:10px; width:200px'>
                    <div class='item' style='height:400px'></div>
                    <div class='item' style='height:20px'></div>
                    <div class='item' style='height:20px'></div>
                    <div class='item' style='height:20px'></div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 1000);
            var items = FindAllByClass(root, "item");
            var mc = FindById(root, "mc")!;

            Assert.Equal(4, items.Count);
            // First item fills column 1 alone (too tall for anything else to join it there);
            // remaining items must land in column 2, at a greater X than column 1.
            var distinctX = items.Select(i => System.Math.Round(i.Location.X)).Distinct().ToList();
            Assert.Equal(2, distinctX.Count);
            Assert.Contains(items, i => System.Math.Abs(i.Location.X - mc.ClientLeft) < 0.5);
            Assert.Contains(items, i => i.Location.X > mc.ClientLeft + 50);
        }

        [Fact]
        public async Task ColumnWidth_ProducesMultipleColumnsAutomatically()
        {
            var html = Wrap(@"
                <div id='mc' style='column-width:100px; column-gap:0; width:320px'>
                    <div class='item' style='height:20px'></div>
                    <div class='item' style='height:20px'></div>
                    <div class='item' style='height:20px'></div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 1000);
            var items = FindAllByClass(root, "item");

            // 320px / 100px column-width ≈ 3 columns fit, so each item should land in its own column
            var distinctX = items.Select(i => System.Math.Round(i.Location.X)).Distinct().ToList();
            Assert.True(distinctX.Count >= 2, "expected column-width to produce more than one column");
        }

        [Fact]
        public async Task ColumnRule_ProducesOneSegmentPerGap()
        {
            var html = Wrap(@"
                <div id='mc' style='columns:3; column-rule: 2px solid black; width:300px'>
                    <div class='item' style='height:20px'></div>
                    <div class='item' style='height:20px'></div>
                    <div class='item' style='height:20px'></div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 1000);
            var mc = FindById(root, "mc")!;

            Assert.NotNull(mc.ColumnRuleSegments);
            // 3 columns -> 2 internal gaps, one row used -> 2 segments
            Assert.Equal(2, mc.ColumnRuleSegments!.Count);
        }

        [Fact]
        public async Task ColumnRuleNone_HasZeroActualWidth()
        {
            // Column-rule geometry is still computed (so painting logic has segments to skip based on
            // width), but the default column-rule-style is "none", which must resolve to zero actual
            // width - the same convention CssBox already uses for border-*-style: none.
            var html = Wrap(@"
                <div id='mc' style='columns:2; width:200px'>
                    <div class='item' style='height:20px'></div>
                    <div class='item' style='height:20px'></div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 1000);
            var mc = FindById(root, "mc")!;

            Assert.Equal(0, mc.ActualColumnRuleWidth);
        }

        // ─── column-fill: balance ───────────────────────────────────────────────────

        [Fact]
        public async Task ShortContent_BalancesAcrossAllColumns()
        {
            // Regression: content short enough to fit a single column on one page must still be
            // spread across every column (column-fill defaults to "balance"), not left piled into
            // column 1 with the rest sitting empty.
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:0; width:200px'>
                    <div class='item' style='height:20px'></div>
                    <div class='item' style='height:20px'></div>
                    <div class='item' style='height:20px'></div>
                    <div class='item' style='height:20px'></div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 1000);
            var items = FindAllByClass(root, "item");
            var mc = FindById(root, "mc")!;

            Assert.Contains(items, i => i.Location.X > mc.ClientLeft + 50);
        }

        // ─── Fragmentation correctness (no overlap) ─────────────────────────────────

        [Fact]
        public async Task OversizedForcedChild_DoesNotOverlapSubsequentContent()
        {
            // Regression for a real bug: when the first child dropped into a column is taller than
            // the column's remaining page budget, it's still placed in full (children are never
            // split) - but every subsequent child must be pushed at or past that child's actual
            // bottom, not the column's nominal page boundary, or the two visibly overlap.
            var html = Wrap(@"
                <div style='height:70px'></div>
                <div id='mc' style='columns:2; column-gap:0; width:200px'>
                    <div class='item' style='height:80px'></div>
                    <div class='item' style='height:10px'></div>
                    <div class='item' style='height:10px'></div>
                    <div class='item' style='height:10px'></div>
                    <div class='item' style='height:10px'></div>
                </div>");
            // Page content height of 100 leaves only ~30px on page 0 for the .mc container to start
            // in - far less than the first item's 80px height.
            var (root, container) = await BuildAndLayout(html, pageHeight: 100);
            var items = FindAllByClass(root, "item");

            Assert.Equal(5, items.Count);
            AssertNoOverlaps(items);
        }

        [Fact]
        public async Task OversizedForcedChild_ColumnRuleDoesNotOverlapContent()
        {
            var html = Wrap(@"
                <div style='height:70px'></div>
                <div id='mc' style='columns:2; column-rule:1px solid black; column-gap:0; width:200px'>
                    <div class='item' style='height:80px'></div>
                    <div class='item' style='height:10px'></div>
                    <div class='item' style='height:10px'></div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 100);
            var mc = FindById(root, "mc")!;
            var items = FindAllByClass(root, "item");

            Assert.NotNull(mc.ColumnRuleSegments);
            foreach (var (_, top, bottom) in mc.ColumnRuleSegments!)
            {
                Assert.True(bottom >= top);
            }
            AssertNoOverlaps(items);
        }

        [Fact]
        public async Task ColumnRule_ActuallyPainted_DrawsALine()
        {
            // Regression coverage per this repo's painting-test convention: a passing layout-level
            // assertion on ColumnRuleSegments alone doesn't prove PaintColumnRules ever runs or issues a
            // real draw call - drive the real Paint() pipeline and record what actually reached RGraphics.
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-rule:1px solid black; column-gap:0; width:200px'>
                    <div class='item' style='height:20px'></div>
                    <div class='item' style='height:20px'></div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 1000);
            var mc = FindById(root, "mc")!;

            var spy = new DrawLineSpyGraphics();
            FragmentPaintHarness.PaintBox(container, mc, spy);

            Assert.True(spy.DrawLineCallCount > 0);
        }

        [Theory]
        [InlineData("dashed")]
        [InlineData("dotted")]
        public async Task ColumnRule_DashedOrDotted_StillPaintsALine(string style)
        {
            var html = Wrap($@"
                <div id='mc' style='columns:2; column-rule:1px {style} black; column-gap:0; width:200px'>
                    <div class='item' style='height:20px'></div>
                    <div class='item' style='height:20px'></div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 1000);
            var mc = FindById(root, "mc")!;

            var spy = new DrawLineSpyGraphics();
            FragmentPaintHarness.PaintBox(container, mc, spy);

            Assert.True(spy.DrawLineCallCount > 0);
        }

        // ─── column-fill: balance precision (binary-search solver) ─────────────────

        [Fact]
        public async Task ColumnFillBalance_FindsTighterHeightThanNaiveEvenSplitEstimate()
        {
            // 6 items [50,50,50,40,40,40] (total 270) into 3 columns. The old single-formula estimate
            // (total/columnCount = 90) is provably too short: sequential first-fit at height 90 can only
            // place 4 of the 6 items (items 5/6 don't fit in any of the 3 columns at that height), forcing
            // the remaining 2 into a synthetic next "row" - which, since row height here is the page
            // height (1000), means they'd land ~1000 units below the rest. The true minimum height that
            // fits all 6 in 3 columns is 100 (verified by hand: col0=[50,50], col1=[50,40], col2=[40,40]).
            // The improved binary-search solver must find that and keep everything on the first row.
            var html = Wrap(@"
                <div id='mc' style='columns:3; column-gap:0; width:300pt'>
                    <div class='item' style='height:50pt'></div>
                    <div class='item' style='height:50pt'></div>
                    <div class='item' style='height:50pt'></div>
                    <div class='item' style='height:40pt'></div>
                    <div class='item' style='height:40pt'></div>
                    <div class='item' style='height:40pt'></div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 1000);
            var items = FindAllByClass(root, "item");

            Assert.Equal(6, items.Count);
            // None of the 6 items should have been pushed onto a synthetic next "row" (which, at this
            // page height, would put them ~1000 units below the first row) - the old naive estimate
            // would fail this assertion for items 5 and 6.
            Assert.All(items, i => Assert.True(i.ActualBottom < 200,
                $"expected all items to stay within the first balanced row, but one ended at Y={i.ActualBottom}"));
        }

        [Fact]
        public async Task ColumnFillBalance_StillNeverSplitsAWholeChild_Regression()
        {
            // The binary-search solver must preserve the existing "whole child, never split" model -
            // every item keeps its full natural height intact regardless of which column it lands in.
            var html = Wrap(@"
                <div id='mc' style='columns:3; column-gap:0; width:300pt'>
                    <div class='item' style='height:50pt'></div>
                    <div class='item' style='height:50pt'></div>
                    <div class='item' style='height:50pt'></div>
                    <div class='item' style='height:40pt'></div>
                    <div class='item' style='height:40pt'></div>
                    <div class='item' style='height:40pt'></div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 1000);
            var items = FindAllByClass(root, "item");

            var expectedHeights = new[] { 50.0, 50.0, 50.0, 40.0, 40.0, 40.0 };
            for (var i = 0; i < items.Count; i++)
            {
                Assert.Equal(expectedHeights[i], items[i].ActualBottom - items[i].Location.Y, 1);
            }
            AssertNoOverlaps(items);
        }

        // ─── Phase-1/Phase-2 side-effect tracking (regression for stale Y after re-banding) ─

        [Fact]
        public async Task NamedString_Y_TracksTheColumnItLandsIn()
        {
            // Same re-banding shape as OversizedForcedChild_DoesNotOverlapSubsequentContent: item i1
            // alone claims column 1 (too tall to share), forcing i2/i3 into column 2 - a real move away
            // from their Phase-1 virtual (single-tall-column) position.
            var html = Wrap(@"
                <style>.item { string-set: entry content(text); }</style>
                <div id='mc' style='columns:2; column-gap:0; width:200px'>
                    <div class='item' id='i1' style='height:80px'>one</div>
                    <div class='item' id='i2' style='height:10px'>two</div>
                    <div class='item' id='i3' style='height:10px'>three</div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 100);
            var mc = FindById(root, "mc")!;
            var i2 = FindById(root, "i2")!;

            // Confirm the fixture actually forces re-banding before trusting the Y assertion below.
            Assert.True(i2.Location.X > mc.ClientLeft + 50, "expected i2 to be re-banded into column 2");

            var namedString = Assert.Single(i2.NamedStrings.Values);
            Assert.Equal(i2.Location.Y, namedString.Y, 1);

            var documentEntry = container.NamedStrings.Single(ns => ns.Name == "entry" && ns.Value == "two");
            Assert.Equal(i2.Location.Y, documentEntry.Y, 1);
        }

        [Fact]
        public async Task NamedPageElement_RegistersOnceForTheColumnItLandsIn()
        {
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:0; width:200px'>
                    <div class='item' style='height:80px'></div>
                    <div class='item' id='i2' style='height:10px; page:chapter'></div>
                    <div class='item' style='height:10px'></div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 100);
            var mc = FindById(root, "mc")!;
            var i2 = FindById(root, "i2")!;

            Assert.True(i2.Location.X > mc.ClientLeft + 50, "expected i2 to be placed in column 2");

            // Exactly one entry is the regression guard. The box is laid out twice - once by the
            // measurement pass that sizes the fill, once for real - and once more again if a column
            // break re-places it, and each placement registers. Its Y is the top of the pagination slot
            // the box lands on, which is the attribution NamedPageRegistrationY defines rather than the
            // box's own coordinate.
            var registered = Assert.Single(container.NamedPageElements, e => e.Name == "chapter");
            Assert.Equal(
                container.PageTopOf(container.PageIndexOf(i2.Location.Y + HtmlContainerInt.PageBoundaryEpsilon)),
                registered.Y, 1);
        }

        [Fact]
        public async Task ActualSize_MatchesRealFinalGeometry_NotInflatedPhase1Height()
        {
            // 4 items of 80px each into 2 columns at a 100px page height. Phase 1's un-banded virtual
            // pass would stack all 4 directly (4*80=320); the real, re-banded/paginated result is far
            // shorter. Since this multicol container is the only content in the document, nothing
            // subsequent can paper over an inflated Phase-1 contribution to ActualSize the way later
            // real content does for a non-last chapter in the real dictionary document.
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:0; width:200px'>
                    <div class='item' style='height:80px'></div>
                    <div class='item' style='height:80px'></div>
                    <div class='item' style='height:80px'></div>
                    <div class='item' style='height:80px'></div>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 100);
            var mc = FindById(root, "mc")!;

            // ActualSize must cover the real content (mc's own re-banded/paginated bottom, plus whatever
            // legitimate margin collapse the surrounding body contributes) but must NOT be inflated all
            // the way up toward Phase 1's un-banded virtual height (stacking all 4 80px items directly
            // would give 320) - that's the actual defect under test here.
            Assert.True(container.ActualSize.Height >= mc.ActualBottom - root.Location.Y,
                $"expected ActualSize.Height to cover real content ({mc.ActualBottom - root.Location.Y}), got {container.ActualSize.Height}");
            Assert.True(container.ActualSize.Height < 250,
                $"expected ActualSize.Height well under Phase 1's un-banded 320, got {container.ActualSize.Height}");
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        // Minimal RGraphics spy recording only DrawLine calls - per this repo's painting-test
        // convention (see SpyGraphics in TransformIntegrationTests.cs), used to prove PaintColumnRules
        // actually issues a real draw call rather than trusting a layout-level assertion alone.
        private sealed class DrawLineSpyGraphics : RGraphics
        {
            public int DrawLineCallCount { get; private set; }

            public DrawLineSpyGraphics() : base(new PdfSharpAdapter(), new RRect(0, 0, double.MaxValue, double.MaxValue)) { }

            public override void DrawLine(RPen pen, double x1, double y1, double x2, double y2) => DrawLineCallCount++;

            public override void PushTransform(RMatrix matrix) { }
            public override void PopTransform() { }
            public override void PushClip(RRect rect) => _clipStack.Push(rect);
            public override void PushClip(RGraphicsPath path) => _clipStack.Push(_clipStack.Peek());
            public override void PopClip() { if (_clipStack.Count > 1) _clipStack.Pop(); }
            public override void PushClipExclude(RRect rect) { }
            public override object SetAntiAliasSmoothingMode() => new object();
            public override void ReturnPreviousSmoothingMode(object? prevMode) { }
            public override RGraphicsPath GetGraphicsPath() => null!;

            public override RGraphicsPath? GetTextOutline(string str, RFont font, RPoint baselineOrigin, double letterSpacing = 0) => null;
            public override (RGraphics Graphics, RImage Image)? CreateTile(double width, double height) => null;
            public override void DrawImageMasked(RImage image, RImage maskImage, RRect destRect) { }
            public override void DrawImageWithOpacity(RImage image, RRect destRect, double opacity) { }
            public override void BeginMarkedContent(string structureType, int mcid) { }
            public override void EndMarkedContent() { }
            public override void BeginArtifact() { }
            public override RSize MeasureString(string str, RFont font) => new(0, 12);
            public override void MeasureString(string str, RFont font, double maxWidth, out int charFit, out double charFitWidth)
            {
                charFit = str?.Length ?? 0;
                charFitWidth = 0;
            }
            public override void DrawString(string str, RFont font, RColor color, RPoint point, RSize size, bool rtl, double letterSpacing = 0, RFontPalette? fontPalette = null) { }
            public override void DrawRectangle(RPen pen, double x, double y, double width, double height) { }
            public override void DrawRectangle(RBrush brush, double x, double y, double width, double height) { }
            public override void DrawImage(RImage image, RRect destRect, RRect srcRect) { }
            public override void DrawImage(RImage image, RRect destRect) { }
            public override void DrawPath(RPen pen, RGraphicsPath path) { }
            public override void DrawPath(RBrush brush, RGraphicsPath path) { }
            public override void DrawPolygon(RBrush brush, RPoint[] points) { }
            public override void Dispose() { }
        }

        private static string Wrap(string body) =>
            $"<!DOCTYPE html><html><head></head><body>{body}</body></html>";

        private async Task<CssBox> FindByIdAsync(string fragment, string id)
        {
            var (root, container) = await BuildAndLayout(Wrap(fragment), pageHeight: 1000);
            return FindById(root, id)!;
        }

        private static async Task<(CssBox root, HtmlContainerInt container)> BuildAndLayout(string html, double pageHeight)
        {
            var adapter = new PdfSharpAdapter();
            adapter.PixelsPerPoint = 1.0;
            var container = new HtmlContainerInt(adapter);
            container.MarginTop = 0;
            container.MarginLeft = 0;
            container.MarginRight = 0;
            container.MarginBottom = 0;
            await container.SetHtml(html, null);

            var size = new XSize(400, pageHeight);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize  = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return (container.Root!, container);
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            var val = box.HtmlTag?.TryGetAttribute("id", "");
            if (val != null && val.Equals(id, System.StringComparison.OrdinalIgnoreCase))
                return box;
            foreach (var child in box.Boxes)
            {
                var found = FindById(child, id);
                if (found != null) return found;
            }
            return null;
        }

        private static List<CssBox> FindAllByClass(CssBox box, string className)
        {
            var results = new List<CssBox>();
            FindAllByClassRecursive(box, className, results);
            return results;
        }

        private static void FindAllByClassRecursive(CssBox box, string className, List<CssBox> results)
        {
            var val = box.HtmlTag?.TryGetAttribute("class", "");
            if (val != null && val.Split(' ').Contains(className, System.StringComparer.OrdinalIgnoreCase))
                results.Add(box);
            foreach (var child in box.Boxes)
                FindAllByClassRecursive(child, className, results);
        }

        // ─── A column as a real fragmentainer (css-break-3 §2) ─────────────────────

        // The whole point of the change: a column is a fragmentainer, so the break machinery inside one
        // asks the column rather than the page. Without that, break-inside had nothing to avoid a break
        // *in* - a column boundary was not a break at all - and the box was placed wherever the packing
        // loop put it.
        [Theory]
        [InlineData("break-inside: avoid")]
        [InlineData("break-inside: avoid-page")]
        public async Task BreakInsideAvoid_IsHonouredAtAColumnBoundary(string declaration)
        {
            var html = Wrap($@"
                <div id='mc' style='columns:2; column-gap:0; width:200px'>
                    <div class='item' style='height:60px'></div>
                    <div class='item' id='keep' style='height:60px; {declaration}'>
                        <div style='height:30px'></div>
                        <div style='height:30px'></div>
                    </div>
                </div>");
            var (root, _) = await BuildAndLayout(html, pageHeight: 120);

            var mc = FindById(root, "mc")!;
            var keep = FindById(root, "keep")!;

            // It could not stay in column 1 without straddling its bottom, so it starts column 2 whole.
            Assert.True(keep.Location.X > mc.ClientLeft + 50,
                $"expected the avoid box to start the next column, it is at x={keep.Location.X}");
        }

        // §2 monolithic content - a scroll container - may not be broken by any user agent, and a column
        // boundary is a break like any other.
        [Fact]
        public async Task MonolithicContent_MovesWholeToTheNextColumn()
        {
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:0; width:200px'>
                    <div class='item' style='height:60px'></div>
                    <div class='item' id='card' style='height:60px; overflow:hidden'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html, pageHeight: 120);

            var mc = FindById(root, "mc")!;
            var card = FindById(root, "card")!;

            Assert.True(card.Location.X > mc.ClientLeft + 50,
                $"expected the scroll container to move whole to the next column, it is at x={card.Location.X}");
        }

        // Every column shares one band, so a child starting a later column starts at the same top the
        // first column did rather than continuing below where the previous column ended.
        [Fact]
        public async Task AChildStartingALaterColumn_StartsAtTheColumnTop()
        {
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:0; width:200px'>
                    <div class='item' id='a' style='height:60px'></div>
                    <div class='item' id='b' style='height:60px'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html, pageHeight: 120);

            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;

            Assert.Equal(a.Location.Y, b.Location.Y, 1);
            Assert.True(b.Location.X > a.Location.X, "expected the second child in the next column");
        }

        // What the last column cannot hold is not dropped: it travels up the ordinary chain and the page
        // driver opens the next page, where the container resumes rather than starting over.
        [Fact]
        public async Task ContentBeyondTheLastColumn_ResumesOnTheNextPage()
        {
            // Real text in each, so every page they land on carries printable content and is
            // materialized - an empty box makes its page content-empty and CSS Paged Media 3 §3.2 drops
            // it, which says nothing about whether the column driver placed it there.
            var items = string.Concat(Enumerable.Range(1, 12)
                .Select(i => $"<div class='item' id='i{i}' style='height:40px'>Item {i}</div>"));
            var html = Wrap($"<div id='mc' style='columns:2; column-gap:0; width:200px'>{items}</div>");

            var (root, container) = await BuildAndLayout(html, pageHeight: 120);

            var placed = FindAllByClass(root, "item");
            Assert.Equal(12, placed.Count);
            AssertNoOverlaps(placed);

            // More than one page, and every item still somewhere in the tree with real geometry.
            Assert.True(container.FragmentTree!.Fragmentainers.Count > 1,
                "expected the container to span more than one page");
            Assert.True(container.FragmentainerPasses > 1, "expected it to have resumed rather than overflowed");

            // Two columns on every page it occupies, not one tall column on a later one.
            var xs = placed.Select(b => System.Math.Round(b.Location.X)).Distinct().ToList();
            Assert.Equal(2, xs.Count);
            Assert.All(placed, b => Assert.True(b.ActualBottom > b.Location.Y, "every item keeps a height"));
        }

        // ─── A balance retry inside a resumed pass (#374) ──────────────────────────

        // The fill is attempted more than once whenever `column-fill: balance` has to grow or re-derive
        // its target, and between attempts everything the abandoned one placed has to be undone. Skipping
        // that wholesale on a resumed pass - which is what "return if resumed" did, for the real reason
        // that children below the resume point hold earlier pages' geometry - also skipped the children at
        // and above it, which this pass is laying out for the first time. So the retry re-finalized the
        // abandoned attempt's own line boxes and CssLineBox.AssignRectanglesToBoxes threw
        // "An item with the same key has already been added".
        //
        // Ordinary auto-height paragraphs, because the shape needs the balance search to want a second
        // attempt on a *resumed* fragment, which is a function of the content's own heights - fixed-height
        // children pack too predictably to reach it.
        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        public async Task BalanceRetryOnAResumedFragment_LaysOutRatherThanThrowing(int columnCount)
        {
            var (root, container) = await BuildAndLayout(ResumedBalanceRetryFixture(columnCount), pageHeight: 100);

            var items = FindAllByClass(root, "item");

            Assert.Equal(12, items.Count);
            Assert.True(container.FragmentainerPasses > 1, "expected the container to have resumed");
            Assert.All(items, item => Assert.True(item.ActualBottom > item.Location.Y,
                "every paragraph keeps a height of its own"));
            AssertNoOverlaps(items);
        }

        // The structural companion: whatever the undo does, it has to leave the box tree self-consistent.
        // A per-line rectangle outlives its line box unless the two are discarded together, and one that
        // does describes a fill that no longer exists - which the emitter reads as readily as the real
        // thing.
        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        public async Task BalanceRetryOnAResumedFragment_LeavesNoRectangleKeyedByADiscardedLineBox(int columnCount)
        {
            var (root, _) = await BuildAndLayout(ResumedBalanceRetryFixture(columnCount), pageHeight: 100);

            foreach (var box in LayoutHarness.Descendants(root))
            {
                foreach (var line in box.Rectangles.Keys)
                {
                    Assert.True(line.OwnerBox.LineBoxes.Contains(line),
                        $"'{box}' still carries a rectangle for the discarded line '{line}'");
                }
            }
        }

        // The workhorse, because a word says both things at once. Claimed twice means the undo left a
        // word where the abandoned attempt put it and a later attempt that did not reach it again could
        // not say so - which is what marks every word of a discarded fill as awaiting placement again.
        // Claimed not at all means the undo went too far: the child at the resume point is *continuing*,
        // so discarding its earlier fragmentainers' lines along with this attempt's blanks a fragment an
        // earlier page already emitted. Each of the three guards fails one half or the other.
        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        public async Task BalanceRetryOnAResumedFragment_KeepsEveryWordExactlyOnce(int columnCount)
        {
            var (root, container) = await BuildAndLayout(ResumedBalanceRetryFixture(columnCount), pageHeight: 100);

            var claimed = container.FragmentTree!.Fragmentainers
                .SelectMany(f => FlattenFragments(f.Root))
                .SelectMany(f => f.Words)
                .Select(w => w.Word)
                .ToList();

            Assert.Equal(claimed.Count, claimed.Distinct().Count());

            var authored = FindAllByClass(root, "item")
                .SelectMany(LayoutHarness.Descendants)
                .SelectMany(b => b.Words)
                .Where(w => !w.IsSpaces)
                .ToList();

            Assert.All(authored, word => Assert.Contains(word, claimed));
        }

        /// <summary>
        /// Twelve auto-height paragraphs against a page too short to hold them, so the container spans
        /// several pages and resumes — the shape from
        /// <see href="https://github.com/jhaygood86/PeachPDF/issues/374">#374</see>.
        /// </summary>
        private static string ResumedBalanceRetryFixture(int columnCount)
        {
            var items = string.Concat(Enumerable.Range(1, 12)
                .Select(i => $"<div class='item' id='i{i}'>Item {i} with a little text of its own</div>"));

            return Wrap($"<div id='mc' style='columns:{columnCount}; column-gap:0; width:200px'>{items}</div>");
        }

        private static IEnumerable<BoxFragment> FlattenFragments(BoxFragment fragment)
        {
            yield return fragment;

            foreach (var child in fragment.Children)
            {
                foreach (var descendant in FlattenFragments(child))
                {
                    yield return descendant;
                }
            }
        }

        // The invariant this was drafted as, and characterized as a boundary until a fragment carried
        // geometry of its own: a child continues into the next column rather than moving to it whole. What
        // used to make that impossible is that a box carries one Location, and columns sit side by side
        // inside a single page band - so both halves had the same document Y *and* the same X, with the
        // continuation's lines laid out over the ones already there.
        [Fact]
        public async Task AChildSplitsAcrossColumns_ItsLinesLandingInEach()
        {
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:0; width:300px; column-fill:auto'>
                    <p id='long'>One two three four five six seven eight nine ten eleven twelve thirteen
                    fourteen fifteen sixteen seventeen eighteen nineteen twenty twenty-one twenty-two
                    twenty-three twenty-four twenty-five twenty-six twenty-seven twenty-eight twenty-nine
                    thirty thirty-one thirty-two thirty-three thirty-four thirty-five thirty-six.</p>
                </div>");
            var (root, _) = await BuildAndLayout(html, pageHeight: 100);

            var mc = FindById(root, "mc")!;
            var longBox = FindById(root, "long")!;
            var words = LayoutHarness.Descendants(longBox).SelectMany(b => b.Words).ToList();

            var inFirstColumn = words.Where(w => w.Left < mc.ClientLeft + 100).ToList();
            var inSecondColumn = words.Where(w => w.Left >= mc.ClientLeft + 100).ToList();

            Assert.NotEmpty(inFirstColumn);
            Assert.NotEmpty(inSecondColumn);

            // The continuation starts at the top of its column rather than below where the first fragment
            // ended - each column is a fragmentainer, and a resumed fragment begins at its content edge.
            Assert.True(inSecondColumn.Min(w => w.Top) < inFirstColumn.Max(w => w.Top),
                "expected the continuation to start above the point the first column reached");
        }

        // The other half of splitting: the box itself moves to the column it continues in, and keeps the
        // inline size it was measured at (css-break-3 §2 gives a box one inline size across its fragments).
        [Fact]
        public async Task AChildContinuingIntoTheNextColumn_IsPlacedAtThatColumnsOrigin()
        {
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:20px; width:300px; column-fill:auto'>
                    <p id='long'>One two three four five six seven eight nine ten eleven twelve thirteen
                    fourteen fifteen sixteen seventeen eighteen nineteen twenty twenty-one twenty-two.</p>
                </div>");
            var (root, _) = await BuildAndLayout(html, pageHeight: 100);

            var mc = FindById(root, "mc")!;
            var longBox = FindById(root, "long")!;

            // columns:2 over a 225pt content box with a 15pt gap: 105pt columns, the second at +120pt.
            Assert.Equal(mc.ClientLeft + 120, longBox.Location.X, 1);
            Assert.Equal(105, longBox.ActualWidth, 1);
            Assert.Equal(mc.ClientTop, longBox.Location.Y, 1);
        }

        // A container that spans pages keeps the top it was placed at on the page it began, so its own
        // content edge names a coordinate the columns of a later page do not reach. Read as the destination
        // for a box continuing into the next column, it sent that continuation back to the container's very
        // first column - landing it on top of whatever legitimately sits there, which is what makes this an
        // overlap rather than a merely-misplaced box.
        [Fact]
        public async Task AChildContinuingIntoTheNextColumn_OnAPageAfterTheContainersFirst_StaysInThatPagesBand()
        {
            var items = string.Concat(Enumerable.Range(1, 12)
                .Select(i => $"<div class='item' id='i{i}'>Item {i} with a little text of its own</div>"));
            var html = Wrap($"<div id='mc' style='columns:2; column-gap:0; width:200px'>{items}</div>");

            var (root, container) = await BuildAndLayout(html, pageHeight: 100);

            var placed = FindAllByClass(root, "item");
            Assert.Equal(12, placed.Count);
            AssertNoOverlaps(placed);

            // Every item sits inside the band of the page it was laid out on - the check the overlap above
            // is a symptom of, stated directly, so a future regression names the cause rather than a
            // coincidence of which two boxes happened to collide.
            Assert.All(placed, box =>
            {
                var slot = container.PageIndexOf(box.Location.Y);
                Assert.InRange(box.Location.Y,
                    container.PageTopOf(slot) - 0.5, container.PageBottomOf(slot) + 0.5);
            });
        }

        // column-fill: auto fills each column before starting the next, so the first column runs to the
        // page budget rather than to an even share - the branch that skips the balance estimate entirely.
        [Fact]
        public async Task ColumnFillAuto_FillsTheFirstColumnBeforeStartingTheNext()
        {
            var items = string.Concat(Enumerable.Range(1, 6)
                .Select(i => $"<div class='item' id='a{i}' style='height:40px'>Item {i}</div>"));
            var html = Wrap($"<div id='mc' style='columns:2; column-gap:0; width:200px; column-fill:auto'>{items}</div>");

            var (root, _) = await BuildAndLayout(html, pageHeight: 200);
            var placed = FindAllByClass(root, "item");
            var mc = FindById(root, "mc")!;

            // Filling means column 1 takes as many as its budget allows, so it holds strictly more than
            // an even split of six would.
            var inFirstColumn = placed.Count(b => System.Math.Abs(b.Location.X - mc.ClientLeft) < 0.5);
            Assert.True(inFirstColumn > 3, $"expected the first column to be filled, it holds {inFirstColumn}");
        }

        // The estimator searches for the tightest height that still packs as many children as the full
        // budget would. With unevenly-sized children an even split of the total is not that height, so
        // this pins that the search runs rather than a closed form.
        [Fact]
        public async Task ColumnFillBalance_WithUnevenChildren_UsesNoMoreColumnsThanTheBudgetWould()
        {
            var heights = new[] { 70, 70, 70, 30, 30, 30 };
            var items = string.Concat(heights.Select((h, i) =>
                $"<div class='item' id='u{i}' style='height:{h}px'>Item {i}</div>"));
            var html = Wrap($"<div id='mc' style='columns:3; column-gap:0; width:300px'>{items}</div>");

            var (root, _) = await BuildAndLayout(html, pageHeight: 400);
            var placed = FindAllByClass(root, "item");

            AssertNoOverlaps(placed);
            Assert.Equal(6, placed.Count);

            // Balanced across all three columns rather than poured into the first.
            var xs = placed.Select(b => System.Math.Round(b.Location.X)).Distinct().ToList();
            Assert.Equal(3, xs.Count);
        }

        // A child taller than any column claims one on its own and overflows it, rather than being split
        // - the case the estimator's monotonicity argument turns on.
        [Fact]
        public async Task AChildTallerThanItsColumn_ClaimsOneAndOverflows()
        {
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:0; width:200px'>
                    <div class='item' id='tall' style='height:300px'>Tall</div>
                    <div class='item' id='after' style='height:20px'>After</div>
                </div>");
            var (root, _) = await BuildAndLayout(html, pageHeight: 200);

            var mc = FindById(root, "mc")!;
            var tall = FindById(root, "tall")!;
            var after = FindById(root, "after")!;

            // It is not split: one box, its full height, in the first column.
            Assert.Equal(225, tall.ActualBottom - tall.Location.Y, 1);
            Assert.Equal(mc.ClientLeft, tall.Location.X, 1);
            Assert.True(after.Location.X > mc.ClientLeft + 50, "the next child starts the next column");
        }

        // The multicol dispatch no longer routes through LayoutMonolithicContent, which used to be what
        // ran the out-of-flow children of every engine container. The columns engine lays out its own, so
        // an absolutely-positioned child must still be placed and keep its content.
        [Fact]
        public async Task OutOfFlowChild_IsStillLaidOut()
        {
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:0; width:200px; position:relative'>
                    <div class='item' style='height:40px'>One</div>
                    <div class='item' style='height:40px'>Two</div>
                    <div id='abs' style='position:absolute; top:5px; left:5px'>Absolute</div>
                </div>");
            var (root, _) = await BuildAndLayout(html, pageHeight: 400);

            var abs = FindById(root, "abs")!;
            Assert.True(abs.ActualBottom > abs.Location.Y,
                $"expected the absolutely-positioned child to have been laid out, it is {abs.Location.Y}..{abs.ActualBottom}");
            Assert.NotEmpty(LayoutHarness.Descendants(abs).SelectMany(b => b.Words));
        }

        // A multi-column container inside another engine's container is being *measured*, not paginated -
        // that engine will translate it afterwards. Establishing a fragmentation context here anyway
        // would let the column driver record a resumption record the enclosing engine never reads, and
        // the content that record names is simply dropped. Measured at five of twelve items lost.
        [Theory]
        [InlineData("display:flex")]
        [InlineData("display:grid")]
        [InlineData("display:table")]
        public async Task InsideAnotherEngine_NoContentIsDropped(string outerStyle)
        {
            var items = string.Concat(Enumerable.Range(1, 12)
                .Select(i => $"<div class='item' id='k{i}' style='height:40px'>Item {i}</div>"));
            var html = Wrap($"<div style='{outerStyle}'><div id='mc' style='columns:2; width:200px'>{items}</div></div>");

            var (root, _) = await BuildAndLayout(html, pageHeight: 120);

            var placed = FindAllByClass(root, "item");
            Assert.Equal(12, placed.Count);
            AssertNoOverlaps(placed);
            Assert.All(placed, b => Assert.True(b.ActualBottom > b.Location.Y, "every item keeps a height"));
        }

        // The container is laid out once per page fragment, so a rule list that is *assigned* rather than
        // accumulated keeps only the last fragment's - and the first page draws no rule at all.
        [Fact]
        public async Task ColumnRules_AreKeptForEveryPageTheContainerSpans()
        {
            var items = string.Concat(Enumerable.Range(1, 12)
                .Select(i => $"<div class='item' style='height:40px'>Item {i}</div>"));
            var html = Wrap($"<div id='mc' style='columns:2; column-rule:1px solid #000; width:200px'>{items}</div>");

            var (root, container) = await BuildAndLayout(html, pageHeight: 120);
            var mc = FindById(root, "mc")!;

            Assert.NotNull(mc.ColumnRuleSegments);

            // One per page the container occupies, each spanning only that page's own content.
            var slots = mc.ColumnRuleSegments!
                .Select(seg => container.PageIndexOf(seg.Top + HtmlContainerInt.PageBoundaryEpsilon))
                .Distinct()
                .ToList();

            Assert.True(slots.Count > 1,
                $"expected a rule on every page the container spans, got segments on slot(s) {string.Join(",", slots)}");
        }

        // An out-of-flow child's containing block is the multi-column container, not a column - and the
        // column loop narrows the container to one column at a time while filling it.
        [Fact]
        public async Task OutOfFlowChild_ResolvesAgainstTheContainerNotAColumn()
        {
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:0; width:200px; position:relative'>
                    <div class='item' style='height:40px'>One</div>
                    <div class='item' style='height:40px'>Two</div>
                    <div id='abs' style='position:absolute; top:0; left:0; width:100%'>Absolute</div>
                </div>");
            var (root, _) = await BuildAndLayout(html, pageHeight: 400);

            var mc = FindById(root, "mc")!;
            var abs = FindById(root, "abs")!;

            // 200px = 150pt of container, not 75pt of column.
            Assert.Equal(mc.ClientRight - mc.ClientLeft, abs.ActualRight - abs.Location.X, 1);
        }

        // A known boundary, pre-existing and characterized here rather than left silent. CssBox's own
        // content dispatch tests ContainsInlinesOnly *before* EstablishesMultiColumnContext, and
        // ContainsInlinesOnly is "every child is inline" - vacuously true of a box whose children are all
        // text. So a multi-column container holding nothing but text takes the inline branch and never
        // reaches this engine at all. Columnizing it needs its inline content wrapped in an anonymous
        // block first, which is a box-generation question rather than a fragmentation one.
        [Fact]
        public async Task InlineOnlyContent_DoesNotColumnize_KnownBoundary()
        {
            var html = Wrap("<div id='mc' style='columns:2; column-gap:0; width:200px'>"
                            + "Plain text with no block child of its own, long enough that two columns "
                            + "would be visibly different from one.</div>");
            var (root, _) = await BuildAndLayout(html, pageHeight: 400);

            var mc = FindById(root, "mc")!;
            var xs = LayoutHarness.Descendants(mc).SelectMany(b => b.Words)
                .Select(w => System.Math.Round(w.Left)).DefaultIfEmpty(0).ToList();

            // The text uses the container's whole width rather than a column's - two columns of a 150pt
            // container would each be ~75pt wide. And no rule is drawn, because no column was made.
            Assert.True(xs.Max() - xs.Min() > 100,
                $"expected the text to span the whole container rather than one column, got X {xs.Min()}..{xs.Max()}");
            Assert.Null(mc.ColumnRuleSegments);
        }

        // ─── Break values in the column context (css-break-3 §3.1 `column`, §3.2 `avoid-column`) ──────

        /// <summary>
        /// The column an author asks for. A forced column break cannot be realized the way a page break
        /// is — by placing the box lower — because every column of a container shares one block-axis
        /// band, so it is a break decision instead.
        /// </summary>
        [Theory]
        [InlineData("break-before: column", "")]
        [InlineData("", "break-after: column")]
        public async Task ForcedColumnBreak_StartsTheNextColumn(string onSecond, string onFirst)
        {
            var html = Wrap($@"
                <div id='mc' style='columns:2; column-gap:0; width:200px; column-fill:auto'>
                    <div id='a' style='height:20px; {onFirst}'>A</div>
                    <div id='b' style='height:20px; {onSecond}'>B</div>
                </div>");
            var (root, _) = await BuildAndLayout(html, pageHeight: 200);

            var mc = FindById(root, "mc")!;
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;

            Assert.True(a.Location.X < mc.ClientLeft + 50,
                $"the first box must stay in column 1, it is at x={a.Location.X}");
            Assert.True(b.Location.X > mc.ClientLeft + 50,
                $"expected the forced break to start the next column, the box is at x={b.Location.X}");

            // Every column shares one band, so the box begins where the first column began rather than
            // below where its content ended.
            Assert.Equal(a.Location.Y, b.Location.Y, 1);
        }

        /// <summary>
        /// Both boxes fit column 1 with room to spare, so without the break there is no boundary at all —
        /// which is what makes the assertion above about the break rather than about the packing.
        /// </summary>
        [Fact]
        public async Task WithoutTheForcedBreak_BothBoxesShareOneColumn()
        {
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:0; width:200px; column-fill:auto'>
                    <div id='a' style='height:20px'>A</div>
                    <div id='b' style='height:20px'>B</div>
                </div>");
            var (root, _) = await BuildAndLayout(html, pageHeight: 200);

            var mc = FindById(root, "mc")!;

            Assert.True(FindById(root, "b")!.Location.X < mc.ClientLeft + 50);
        }

        /// <summary>
        /// <c>column</c> names a fragmentation context, and outside one there is no column boundary for it
        /// to force. Honouring it as a page break would be a hint about columns silently changing
        /// pagination — the defect the context-aware predicate exists to prevent.
        /// </summary>
        [Theory]
        [InlineData("break-before: column")]
        [InlineData("break-inside: avoid-column")]
        public async Task ColumnBreakValues_OutsideAMulticolContainer_ChangeNothing(string declaration)
        {
            var html = Wrap($@"
                <div id='a' style='height:20px'>A</div>
                <div id='b' style='height:20px; {declaration}'>B</div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 200);

            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;

            Assert.Equal(container.PageIndexOf(a.Location.Y), container.PageIndexOf(b.Location.Y));
            Assert.True(b.Location.Y > a.Location.Y, "the second box must still follow the first");
        }

        /// <summary>
        /// A value that forces a <i>page</i> break is left to the page vehicle rather than converted here.
        /// Inside a multi-column container that vehicle degrades to a column break — which column the
        /// content lands in is a question the container answers — so the box does start the next column;
        /// what this pins is that it gets there once, by one decision.
        /// </summary>
        [Fact]
        public async Task AForcedPageBreak_InsideAColumn_StartsTheNextColumn()
        {
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:0; width:200px; column-fill:auto'>
                    <div id='a' style='height:20px'>A</div>
                    <div id='b' style='height:20px; break-before:page'>B</div>
                </div>");
            var (root, _) = await BuildAndLayout(html, pageHeight: 200);

            var mc = FindById(root, "mc")!;
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;

            Assert.True(a.Location.X < mc.ClientLeft + 50);
            Assert.True(b.Location.X > mc.ClientLeft + 50,
                $"expected the box to start the next column, it is at x={b.Location.X}");
        }

        /// <summary>
        /// §3.1's break point before a container's first in-flow child <i>is</i> the break point before the
        /// container, so a <c>break-before: column</c> there moves the container — the same propagation the
        /// page vehicle applies, asked in the column context.
        /// </summary>
        [Fact]
        public async Task ForcedColumnBreak_OnAContainersFirstChild_MovesTheContainer()
        {
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:0; width:200px; column-fill:auto'>
                    <div id='a' style='height:20px'>A</div>
                    <div id='wrap'><div id='b' style='height:20px; break-before:column'>B</div></div>
                </div>");
            var (root, _) = await BuildAndLayout(html, pageHeight: 200);

            var mc = FindById(root, "mc")!;
            var wrap = FindById(root, "wrap")!;
            var b = FindById(root, "b")!;

            Assert.True(wrap.Location.X > mc.ClientLeft + 50,
                $"the container must travel with the break, it is at x={wrap.Location.X}");
            Assert.True(b.Location.X > mc.ClientLeft + 50);
        }

        /// <summary>
        /// §3.2 in the column context. Only observable at all because a child can genuinely split across a
        /// column: until it could, "do not break me by a column" named nothing that happened.
        /// </summary>
        [Theory]
        [InlineData("avoid-column")]
        [InlineData("avoid")]
        public async Task AvoidColumnBreak_MovesTheWholeBoxToTheNextColumn(string value)
        {
            var (root, mc, longBox, _) = await SplittingParagraphAsync($"break-inside:{value}");

            var words = LayoutHarness.Descendants(longBox).SelectMany(b => b.Words).ToList();

            Assert.NotEmpty(words);
            Assert.All(words, w => Assert.True(w.Left >= mc.ClientLeft + 100,
                $"every word must be in the second column, one is at x={w.Left}"));
            Assert.NotNull(FindById(root, "top"));
        }

        /// <summary>
        /// The control, and the point of the context: <c>avoid-page</c> names the page context and must not
        /// suppress a <i>column</i> break, so the same paragraph still splits.
        /// </summary>
        [Theory]
        [InlineData("break-inside:avoid-page")]
        [InlineData("break-inside:auto")]
        public async Task APageContextAvoidValue_DoesNotSuppressAColumnBreak(string declaration)
        {
            var (_, mc, longBox, _) = await SplittingParagraphAsync(declaration);

            var words = LayoutHarness.Descendants(longBox).SelectMany(b => b.Words).ToList();

            Assert.Contains(words, w => w.Left < mc.ClientLeft + 100);
            Assert.Contains(words, w => w.Left >= mc.ClientLeft + 100);
        }

        /// <summary>
        /// §4.3's fourth tier: a box that asks not to be broken and fits no column is split rather than
        /// walked from column to column. The <c>i &gt; start</c> guard is what expresses it — a box moved to
        /// the next column becomes the child that column's fill starts at, so it is not asked again.
        /// </summary>
        [Fact]
        public async Task ABoxThatAvoidsButFitsNoColumn_IsStillSplit()
        {
            var html = Wrap(@"
                <div id='mc' style='columns:2; column-gap:0; width:300px; column-fill:auto'>
                    <div id='top' style='height:40pt'>Top</div>
                    <p id='long' style='break-inside:avoid-column; margin:0; font-size:10pt; line-height:20pt'>
                    L1<br>L2<br>L3<br>L4<br>L5<br>L6</p>
                </div>");
            var (root, _) = await BuildAndLayout(html, pageHeight: 100);

            var mc = FindById(root, "mc")!;
            var placed = LayoutHarness.Descendants(FindById(root, "long")!).SelectMany(b => b.Words).ToList();

            Assert.Contains(placed, w => w.Left < mc.ClientLeft + 100);
            Assert.Contains(placed, w => w.Left >= mc.ClientLeft + 100);
        }

        /// <summary>
        /// The workhorse invariant for anything that relocates content between fragmentainers: every word
        /// the document authored is claimed by exactly one fragment. It fails one way if the abandoned
        /// attempt leaves a ghost behind in the column the box left, and the other way if moving the box
        /// drops what it had already placed.
        /// </summary>
        [Fact]
        public async Task AvoidColumnBreak_KeepsEveryWordExactlyOnce()
        {
            var (_, _, _, container) = await SplittingParagraphAsync("break-inside:avoid-column");

            var claimed = container.FragmentTree!.Fragmentainers
                .SelectMany(f => FlattenFragments(f.Root))
                .SelectMany(f => f.Words)
                .Select(w => w.Word)
                .ToList();

            Assert.NotEmpty(claimed);
            Assert.Equal(claimed.Count, claimed.Distinct().Count());
        }

        private static async Task<(CssBox Root, CssBox Mc, CssBox LongBox, HtmlContainerInt Container)>
            SplittingParagraphAsync(string declaration)
        {
            // 40pt of content above a paragraph that is 80pt tall, in a 100pt column: it cannot stay
            // where it is, and it fits a column of its own exactly - so which of the two happens says
            // which break value was honoured, with no dependence on font metrics.
            var html = Wrap($@"
                <div id='mc' style='columns:2; column-gap:0; width:300px; column-fill:auto'>
                    <div id='top' style='height:40pt'>Top</div>
                    <p id='long' style='{declaration}; margin:0; font-size:10pt; line-height:20pt'>
                    L1<br>L2<br>L3<br>L4</p>
                </div>");
            var (root, container) = await BuildAndLayout(html, pageHeight: 100);

            return (root, FindById(root, "mc")!, FindById(root, "long")!, container);
        }

        /// <summary>
        /// Asserts no two boxes' bounding rectangles overlap — the structural invariant a
        /// two-axis (page, column) fragmentation engine must never violate, since an overlap
        /// means two unrelated pieces of content paint on top of each other.
        /// </summary>
        private static void AssertNoOverlaps(IReadOnlyList<CssBox> boxes)
        {
            for (var i = 0; i < boxes.Count; i++)
            {
                for (var j = i + 1; j < boxes.Count; j++)
                {
                    var a = boxes[i];
                    var b = boxes[j];

                    var xOverlap = a.Location.X < b.ActualRight - 0.5 && b.Location.X < a.ActualRight - 0.5;
                    var yOverlap = a.Location.Y < b.ActualBottom - 0.5 && b.Location.Y < a.ActualBottom - 0.5;

                    Assert.False(xOverlap && yOverlap,
                        $"Boxes overlap: [{a.Location.X},{a.Location.Y},{a.ActualRight},{a.ActualBottom}] vs [{b.Location.X},{b.Location.Y},{b.ActualRight},{b.ActualBottom}]");
                }
            }
        }
    }
}
