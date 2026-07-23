namespace PeachPDF.Tests.CSS
{
    using PeachPDF.CSS;
    using System.Linq;
    using Xunit;

    /// <summary>
    /// The <c>grid</c> / <c>grid-template</c> mega-shorthands (CSS Grid §7.4 / §7.8, issue #264) parse and
    /// expand to the grid longhands at parse time, and — like the other reconstruction-excluded shorthands —
    /// are never re-collapsed when a declaration block is serialized.
    /// </summary>
    public class GridTemplateShorthandTests : CssConstructionFunctions
    {
        private static StyleDeclaration Style(string declaration) =>
            (StyleDeclaration)ParseStyleSheet($"div {{ {declaration} }}").Rules.OfType<StyleRule>().Single().Style;

        // ── grid-template ────────────────────────────────────────────────────

        [Fact]
        public void GridTemplate_None_ResetsAllThree()
        {
            var style = Style("grid-template: none;");
            Assert.Equal("none", style.GetPropertyValue("grid-template-rows"));
            Assert.Equal("none", style.GetPropertyValue("grid-template-columns"));
            Assert.Equal("none", style.GetPropertyValue("grid-template-areas"));
        }

        [Fact]
        public void GridTemplate_RowsSlashColumns_SetsBothAxesAreasNone()
        {
            var style = Style("grid-template: 1fr 2fr / 100px 200px;");
            Assert.Equal("1fr 2fr", style.GetPropertyValue("grid-template-rows"));
            Assert.Equal("100px 200px", style.GetPropertyValue("grid-template-columns"));
            // An omitted axis is reset to its initial value (via the CSS-wide `initial` keyword).
            Assert.Equal("initial", style.GetPropertyValue("grid-template-areas"));
        }

        [Fact]
        public void GridTemplate_AreasForm_SynthesizesRowsAndColumns()
        {
            var style = Style("grid-template: \"a a\" 40px \"b b\" / 1fr 1fr;");
            Assert.Equal("\"a a\" \"b b\"", style.GetPropertyValue("grid-template-areas"));
            Assert.Equal("40px auto", style.GetPropertyValue("grid-template-rows"));
            Assert.Equal("1fr 1fr", style.GetPropertyValue("grid-template-columns"));
        }

        [Fact]
        public void GridTemplate_AreasForm_WithLineNames_PreservesThem()
        {
            var style = Style("grid-template: [r1] \"a\" 10px [r2] \"b\" [r3];");
            Assert.Equal("\"a\" \"b\"", style.GetPropertyValue("grid-template-areas"));
            Assert.Equal("[r1] 10px [r2] auto [r3]", style.GetPropertyValue("grid-template-rows"));
            // No explicit column track list → columns reset to its initial value.
            Assert.Equal("initial", style.GetPropertyValue("grid-template-columns"));
        }

        [Theory]
        // `none` is a valid <grid-template-rows>/<grid-template-columns> value on either side of the slash.
        [InlineData("grid-template: none / 1fr 1fr;", "none", "1fr 1fr")]
        [InlineData("grid-template: 1fr 2fr / none;", "1fr 2fr", "none")]
        public void GridTemplate_NoneOnOneAxis_IsAccepted(string declaration, string expectedRows, string expectedColumns)
        {
            var style = Style(declaration);
            Assert.Equal(expectedRows, style.GetPropertyValue("grid-template-rows"));
            Assert.Equal(expectedColumns, style.GetPropertyValue("grid-template-columns"));
        }

        // ── grid ─────────────────────────────────────────────────────────────

        [Fact]
        public void Grid_TemplateForm_ResetsAutoProperties()
        {
            var style = Style("grid: \"a\" / 1fr;");
            Assert.Equal("\"a\"", style.GetPropertyValue("grid-template-areas"));
            Assert.Equal("auto", style.GetPropertyValue("grid-template-rows"));
            Assert.Equal("1fr", style.GetPropertyValue("grid-template-columns"));
            // The grid-auto-* longhands are reset to their initial values (via the CSS-wide `initial`).
            Assert.Equal("initial", style.GetPropertyValue("grid-auto-flow"));
            Assert.Equal("initial", style.GetPropertyValue("grid-auto-rows"));
            Assert.Equal("initial", style.GetPropertyValue("grid-auto-columns"));
        }

        [Fact]
        public void Grid_ColumnAutoFlowForm_SetsRowsFlowAndAutoColumns()
        {
            var style = Style("grid: 1fr / auto-flow 2fr;");
            Assert.Equal("1fr", style.GetPropertyValue("grid-template-rows"));
            Assert.Equal("column", style.GetPropertyValue("grid-auto-flow"));
            Assert.Equal("2fr", style.GetPropertyValue("grid-auto-columns"));
            Assert.Equal("initial", style.GetPropertyValue("grid-template-columns"));
        }

        [Fact]
        public void Grid_RowAutoFlowForm_WithDense_SetsColumnsFlowAndAutoRows()
        {
            var style = Style("grid: auto-flow dense 10px / 1fr;");
            Assert.Equal("1fr", style.GetPropertyValue("grid-template-columns"));
            Assert.Equal("row dense", style.GetPropertyValue("grid-auto-flow"));
            Assert.Equal("10px", style.GetPropertyValue("grid-auto-rows"));
        }

        [Fact]
        public void Grid_NoneRows_WithColumnAutoFlow_IsAccepted()
        {
            // The <grid-template-rows> side of the auto-flow form also accepts `none`.
            var style = Style("grid: none / auto-flow 1fr;");
            Assert.Equal("none", style.GetPropertyValue("grid-template-rows"));
            Assert.Equal("column", style.GetPropertyValue("grid-auto-flow"));
            Assert.Equal("1fr", style.GetPropertyValue("grid-auto-columns"));
        }

        // ── rejection (whole declaration dropped) ────────────────────────────

        [Theory]
        [InlineData("grid-template: 10px \"a\";")]            // track-size before the first string
        [InlineData("grid-template: \"a\" repeat(2, 1fr);")]  // repeat() is not a valid row <track-size>
        [InlineData("grid-template: \"a\" \"b\" / repeat(2, 1fr);")] // trailing columns are an <explicit-track-list> (no repeat())
        [InlineData("grid-template: \"a a\" \"b\";")]         // ragged area rows
        [InlineData("grid-template: 1fr / 2fr / 3fr;")]       // two top-level slashes
        [InlineData("grid-template: 1fr 2fr;")]               // a bare track list is not a valid grid-template
        [InlineData("grid: auto-flow / auto-flow;")]          // auto-flow on both sides
        [InlineData("grid: dense 10px / 1fr;")]               // dense without auto-flow
        public void InvalidValue_IsDropped(string declaration)
        {
            var style = Style(declaration);
            Assert.Equal(string.Empty, style.GetPropertyValue("grid-template-rows"));
            Assert.Equal(string.Empty, style.GetPropertyValue("grid-template-columns"));
        }

        // ── serialization: never reconstructed ───────────────────────────────

        [Fact]
        public void Expanded_DoesNotReconstructMegaShorthand()
        {
            var sheet = ParseStyleSheet("div { grid: auto-flow dense 10px / 1fr; }");
            var css = sheet.ToCss();

            Assert.DoesNotContain("grid:", css);
            Assert.DoesNotContain("grid-template:", css);
            Assert.Contains("grid-auto-flow", css);
            Assert.Contains("grid-template-columns", css);
        }

        // ── var() is kept whole and deferred to cascade time ─────────────────

        [Fact]
        public void GridTemplate_WithVar_IsNotExpandedAtParseTime()
        {
            var style = Style("grid-template: var(--t);");
            // The shorthand stays whole (var() can't be sliced at parse time), so the longhands are not set.
            Assert.Equal(string.Empty, style.GetPropertyValue("grid-template-rows"));
        }
    }
}
