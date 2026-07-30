namespace PeachPDF.Tests.CSS
{
    using PeachPDF.CSS;
    using System.Linq;
    using Xunit;

    /// <summary>
    /// Logical box-model shorthands parse and expand to their own logical longhands (not the physical
    /// ones - see CssBox.LogicalProperties.cs/LogicalPropertyResolver for where those get resolved to a
    /// physical edge, against the box's own direction/writing-mode, later in the cascade), and must NOT
    /// be used to reconstruct a shorthand when a declaration block is serialized: collapsing e.g.
    /// margin-block-start+margin-block-end into `margin-block` would change existing output for a
    /// document that set the logical longhands directly. (Guards the PropertyFactory
    /// `_logicalShorthands` exclusion in GetShorthands.)
    /// </summary>
    public class LogicalPropertySerializationTests : CssConstructionFunctions
    {
        [Fact]
        public void PhysicalLonghands_DoNotSerializeAsLogicalShorthand()
        {
            var sheet = ParseStyleSheet("div { margin-top: 5pt; margin-bottom: 5pt; }");
            var css = sheet.ToCss();

            Assert.DoesNotContain("margin-block", css);
            Assert.Contains("margin-top", css);
            Assert.Contains("margin-bottom", css);
        }

        [Fact]
        public void LogicalShorthand_ParsesAndExpandsToLogicalLonghands()
        {
            var sheet = ParseStyleSheet("div { margin-block: 3pt 4pt; }");
            var style = sheet.Rules.OfType<StyleRule>().Single().Style;

            // Expanded at parse time to the box's own logical block edges, not the physical ones -
            // margin-block-start/-end resolve to a physical edge only once the cascade knows this box's
            // own direction/writing-mode (CssBox.ResolveLogicalProperties), which a bare StyleDeclaration
            // parse (no box, no cascade) never reaches.
            Assert.Equal("3pt", style.GetPropertyValue("margin-block-start"));
            Assert.Equal("4pt", style.GetPropertyValue("margin-block-end"));
            Assert.Equal("", style.GetPropertyValue("margin-top"));
            Assert.Equal("", style.GetPropertyValue("margin-bottom"));
            // And the serialized form uses the logical longhands, never `margin-block`.
            Assert.DoesNotContain("margin-block:", sheet.ToCss());
        }

        [Fact]
        public void PhysicalBorderStillReconstructs()
        {
            // The physical `border` shorthand must still reconstruct — the logical exclusion is scoped to the
            // logical shorthands only.
            var sheet = ParseStyleSheet(
                "div { border-top: 1pt solid red; border-right: 1pt solid red; border-bottom: 1pt solid red; border-left: 1pt solid red; }");
            var css = sheet.ToCss();

            Assert.Contains("border:", css);
        }
    }
}
