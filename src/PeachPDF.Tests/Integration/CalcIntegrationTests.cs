using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Integration tests for calc()/min()/max()/clamp() at the cascade/layout level: real numeric
    /// resolution against containing-block width, font size, and transform geometry. CSS-object-model
    /// parsing/type-checking/canonicalization is tested separately in CSS/PropertyTests/CalcPropertyTests.cs.
    /// </summary>
    public class CalcIntegrationTests
    {
        [Fact]
        public async Task Width_CalcPercentMinusPx_ResolvesAgainstContainingBlockWidth()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="parent" style="width: 400px">
                  <div id="child" style="width: calc(100% - 50px)"></div>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var child = FindById(root, "child");

            Assert.NotNull(child);
            Assert.Equal(350, child!.ActualWidth, 2);
        }

        [Fact]
        public async Task MarginLeft_CalcPercent_ResolvesAgainstContainingBlockWidth()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="parent" style="width: 400px">
                  <div id="child" style="margin-left: calc(50% - 10px)"></div>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var child = FindById(root, "child");

            Assert.NotNull(child);
            Assert.Equal(190, child!.ActualMarginLeft, 2);
        }

        [Fact]
        public async Task BorderTopLeftRadius_CalcTwoValueForm_ResolvesXAndYIndependently()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="el" style="width: 200px; height: 100px; border-top-left-radius: calc(10px + 10px) 5px"></div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal(20, el!.ActualBorderTopLeftRadiusX, 2);
            Assert.Equal(5, el.ActualBorderTopLeftRadiusY, 2);
        }

        [Fact]
        public async Task FontSize_CalcEmExpression_ResolvesAgainstParentFontSize()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="parent" style="font-size: 20px">
                  <div id="child" style="font-size: calc(1em + 4px)"></div>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var parent = FindById(root, "parent");
            var child = FindById(root, "child");

            Assert.NotNull(parent);
            Assert.NotNull(child);
            // CssBoxProperties.ActualFont.Size is the resolved font size further divided by
            // PixelsPerPoint (a pre-existing, calc()-unrelated font-metric convention), and that same
            // already-divided parent.ActualFont.Size is exactly what calc()'s "em" unit multiplies
            // against here - matching plain (non-calc) em resolution's own emFactor at this call site.
            // Deriving the expected value from the actually-measured parent size (rather than a
            // hardcoded constant) keeps this test correct regardless of that convention, while still
            // proving both the em and px terms resolve correctly.
            var expected = (parent!.ActualFont.Size + 4 * (72.0 / 96.0)) / 72.0;
            Assert.Equal(expected, child!.ActualFont.Size, 8);
        }

        [Fact]
        public async Task FontSize_CalcRemExpression_ResolvesAgainstRootFontSize()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="child" style="font-size: calc(1rem + 4px)"></div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var child = FindById(root, "child");

            Assert.NotNull(child);
            // GetRemHeight() walks up to the outermost box (the container's root, not <html>) and reads
            // its ActualFont.Height - that's what calc()'s "rem" unit multiplies against.
            var expected = (root.ActualFont.Height + 4 * (72.0 / 96.0)) / 72.0;
            Assert.Equal(expected, child!.ActualFont.Size, 8);
        }

        [Fact]
        public async Task Transform_TranslateXCalc_SetsExpectedOffset()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="el" style="width: 100px; height: 50px; transform: translateX(calc(50% + 10px))"></div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.True(el!.IsTransformed);
            Assert.Equal(60, el.ActualTransformMatrix.OffsetX, 3);
        }

        [Fact]
        public async Task Transform_ScaleCalc_SetsExpectedLinearPart()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="el" style="width: 100px; height: 50px; transform: scale(calc(1 + 1))"></div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            var m = el!.ActualTransformMatrix;
            Assert.Equal(2, m.M11, 3);
            Assert.Equal(2, m.M22, 3);
        }

        [Fact]
        public async Task Width_Min_PicksSmaller()
        {
            var root = await BuildBoxTree(WidthHtml("min(150px, 100px)"));
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal(100, el!.ActualWidth, 2);
        }

        [Fact]
        public async Task Width_Max_PicksLarger()
        {
            var root = await BuildBoxTree(WidthHtml("max(150px, 100px)"));
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal(150, el!.ActualWidth, 2);
        }

        [Fact]
        public async Task Width_Clamp_ClampsToRange()
        {
            var root = await BuildBoxTree(WidthHtml("clamp(50px, 300px, 150px)"));
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal(150, el!.ActualWidth, 2);
        }

        [Fact]
        public async Task Width_Clamp_ValueBelowMin_ClampsUpToMin()
        {
            var root = await BuildBoxTree(WidthHtml("clamp(50px, 10px, 150px)"));
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal(50, el!.ActualWidth, 2);
        }

        [Fact]
        public async Task Width_Clamp_MinGreaterThanMax_ReturnsMax()
        {
            var root = await BuildBoxTree(WidthHtml("clamp(100px, 50px, 20px)"));
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal(20, el!.ActualWidth, 2);
        }

        [Fact]
        public async Task Width_NestedCalcAndParens_ResolvesCorrectly()
        {
            var root = await BuildBoxTree(WidthHtml("calc(calc(10px + 10px) * 2)"));
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal(40, el!.ActualWidth, 2);
        }

        [Fact]
        public async Task Width_CalcWithVar_ResolvesAfterSubstitution()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="parent" style="width: 400px">
                  <div id="child" style="--gap: 20px; width: calc(100% - var(--gap))"></div>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var child = FindById(root, "child");

            Assert.NotNull(child);
            Assert.Equal(380, child!.ActualWidth, 2);
        }

        [Fact]
        public async Task Width_CalcWithVar_InvalidCategoryMismatch_IsRejectedRatherThanAppliedUninspected()
        {
            // --gap resolves to a bare number ("5"), not a length - calc(var(--gap) + 10px) is therefore
            // a Number+Length category mismatch. Since ApplyResolvedPropertyValue re-validates
            // var()-substituted text through the real width converter, this must be rejected the same way
            // a literal "width: calc(5 + 10px)" already is (see CalcPropertyTests), leaving width at its
            // CSS initial value ("auto") rather than applying a nonsensical number.
            var html = """
                <!DOCTYPE html><html><body>
                <div id="el" style="--gap: 5; width: calc(var(--gap) + 10px)"></div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("auto", el!.Width);
        }

        [Fact]
        public async Task Width_CalcNegativeResult_ComputesExactNegativeValue()
        {
            // PeachPDF doesn't clamp a negative used width to zero for a plain negative length either
            // (verified separately) - calc() is consistent with that existing behavior, not a special case.
            var root = await BuildBoxTree(WidthHtml("calc(50px - 100px)"));
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal(-50, el!.ActualWidth, 2);
        }

        [Fact]
        public async Task Gap_CalcFirstValue_FoldsAndSplitsCorrectly()
        {
            // "gap" is a Layer A shorthand (GapProperty), so its own token-based Periodic() splitting
            // (not the naive-string CssUtils.SetFlexGapShorthand fallback) handles this correctly even
            // before the paren-aware-whitespace-split fix - this test locks in that calc() inside a gap
            // shorthand resolves (and folds) correctly end-to-end.
            var html = """
                <!DOCTYPE html><html><body>
                <div id="el" style="display: flex; gap: calc(10px + 5px) 20px"></div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("15px", el!.FlexRowGap);
            Assert.Equal("20px", el.FlexColumnGap);
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private static string WidthHtml(string widthValue) => $"""
            <!DOCTYPE html><html><body>
            <div id="el" style="width: {widthValue}"></div>
            </body></html>
            """;

        private static async Task<CssBox> BuildBoxTree(string html)
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return container.Root!;
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            if (box.HtmlTag?.Attributes?.TryGetValue("id", out var boxId) == true
                && string.Equals(boxId, id, StringComparison.OrdinalIgnoreCase))
                return box;

            foreach (var child in box.Boxes)
            {
                var found = FindById(child, id);
                if (found is not null) return found;
            }
            return null;
        }
    }
}
