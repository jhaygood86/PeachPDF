namespace PeachPDF.Tests.CSS
{
    using PeachPDF.CSS;
    using System.Linq;
    using Xunit;

    /// <summary>
    /// Logical box-model shorthands parse and expand to the physical longhands, but must NOT be used to
    /// reconstruct a shorthand when a declaration block is serialized — they alias the same physical longhands
    /// as their physical counterparts, so collapsing e.g. margin-top+margin-bottom into `margin-block` would
    /// change existing output. (Guards the PropertyFactory `_logicalShorthands` exclusion in GetShorthands.)
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
        public void LogicalShorthand_ParsesAndExpandsToPhysicalLonghands()
        {
            var sheet = ParseStyleSheet("div { margin-block: 3pt 4pt; }");
            var style = sheet.Rules.OfType<StyleRule>().Single().Style;

            // Expanded at parse time to the physical block edges.
            Assert.Equal("3pt", style.GetPropertyValue("margin-top"));
            Assert.Equal("4pt", style.GetPropertyValue("margin-bottom"));
            // And the serialized form uses the physical longhands, never `margin-block`.
            Assert.DoesNotContain("margin-block", sheet.ToCss());
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
