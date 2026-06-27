using PeachPDF.CSS;

namespace PeachPDF.Tests.CSS.PropertyTests
{
    /// <summary>
    /// Unit tests verifying that the CSS module correctly parses all five global keywords
    /// (inherit, initial, unset, revert, revert-layer) for a representative set of properties.
    /// Cascade-level resolution is tested separately in Integration/GlobalKeywordCascadeTests.cs.
    /// </summary>
    public class GlobalKeywordPropertyTests : CssConstructionFunctions
    {
        // ── inherit ────────────────────────────────────────────────────────────

        [Fact]
        public void Color_Inherit_IsMarkedAsInherited()
        {
            var property = ParseDeclaration("color: inherit");
            Assert.Equal("color", property.Name);
            Assert.True(property.HasValue);
            Assert.True(property.IsInherited);
        }

        [Fact]
        public void MarginTop_Inherit_IsMarkedAsInherited()
        {
            var property = ParseDeclaration("margin-top: inherit");
            Assert.Equal("margin-top", property.Name);
            Assert.True(property.HasValue);
            Assert.True(property.IsInherited);
        }

        [Fact]
        public void FontSize_Inherit_IsMarkedAsInherited()
        {
            var property = ParseDeclaration("font-size: inherit");
            Assert.Equal("font-size", property.Name);
            Assert.True(property.HasValue);
            Assert.True(property.IsInherited);
        }

        [Fact]
        public void Display_Inherit_IsMarkedAsInherited()
        {
            var property = ParseDeclaration("display: inherit");
            Assert.Equal("display", property.Name);
            Assert.True(property.HasValue);
            Assert.True(property.IsInherited);
        }

        // ── initial ───────────────────────────────────────────────────────────

        [Fact]
        public void Color_Initial_IsMarkedAsInitial()
        {
            var property = ParseDeclaration("color: initial");
            Assert.Equal("color", property.Name);
            Assert.True(property.HasValue);
            Assert.True(property.IsInitial);
        }

        [Fact]
        public void MarginTop_Initial_IsMarkedAsInitial()
        {
            var property = ParseDeclaration("margin-top: initial");
            Assert.Equal("margin-top", property.Name);
            Assert.True(property.HasValue);
            Assert.True(property.IsInitial);
        }

        [Fact]
        public void FontSize_Initial_IsMarkedAsInitial()
        {
            var property = ParseDeclaration("font-size: initial");
            Assert.Equal("font-size", property.Name);
            Assert.True(property.HasValue);
            Assert.True(property.IsInitial);
        }

        // ── unset ─────────────────────────────────────────────────────────────

        [Fact]
        public void Color_Unset_HasUnsetValue()
        {
            var property = ParseDeclaration("color: unset");
            Assert.Equal("color", property.Name);
            Assert.True(property.HasValue);
            Assert.Equal("unset", property.Value);
        }

        [Fact]
        public void MarginTop_Unset_HasUnsetValue()
        {
            var property = ParseDeclaration("margin-top: unset");
            Assert.Equal("margin-top", property.Name);
            Assert.True(property.HasValue);
            Assert.Equal("unset", property.Value);
        }

        [Fact]
        public void FontFamily_Unset_HasUnsetValue()
        {
            var property = ParseDeclaration("font-family: unset");
            Assert.Equal("font-family", property.Name);
            Assert.True(property.HasValue);
            Assert.Equal("unset", property.Value);
        }

        // ── revert ────────────────────────────────────────────────────────────

        [Fact]
        public void Color_Revert_HasRevertValue()
        {
            var property = ParseDeclaration("color: revert");
            Assert.Equal("color", property.Name);
            Assert.True(property.HasValue);
            Assert.Equal("revert", property.Value);
        }

        [Fact]
        public void FontSize_Revert_HasRevertValue()
        {
            var property = ParseDeclaration("font-size: revert");
            Assert.Equal("font-size", property.Name);
            Assert.True(property.HasValue);
            Assert.Equal("revert", property.Value);
        }

        [Fact]
        public void MarginTop_Revert_HasRevertValue()
        {
            var property = ParseDeclaration("margin-top: revert");
            Assert.Equal("margin-top", property.Name);
            Assert.True(property.HasValue);
            Assert.Equal("revert", property.Value);
        }

        // ── revert-layer ──────────────────────────────────────────────────────

        [Fact]
        public void Color_RevertLayer_HasRevertLayerValue()
        {
            var property = ParseDeclaration("color: revert-layer");
            Assert.Equal("color", property.Name);
            Assert.True(property.HasValue);
            Assert.Equal("revert-layer", property.Value);
        }

        [Fact]
        public void MarginTop_RevertLayer_HasRevertLayerValue()
        {
            var property = ParseDeclaration("margin-top: revert-layer");
            Assert.Equal("margin-top", property.Name);
            Assert.True(property.HasValue);
            Assert.Equal("revert-layer", property.Value);
        }

        [Fact]
        public void Display_RevertLayer_HasRevertLayerValue()
        {
            var property = ParseDeclaration("display: revert-layer");
            Assert.Equal("display", property.Name);
            Assert.True(property.HasValue);
            Assert.Equal("revert-layer", property.Value);
        }

        // ── !important is preserved alongside global keywords ─────────────────

        [Fact]
        public void Color_InheritImportant_IsImportantAndInherited()
        {
            var property = ParseDeclaration("color: inherit !important");
            Assert.Equal("color", property.Name);
            Assert.True(property.IsImportant);
            Assert.True(property.IsInherited);
        }

        [Fact]
        public void MarginTop_UnsetImportant_IsImportantWithUnsetValue()
        {
            var property = ParseDeclaration("margin-top: unset !important");
            Assert.Equal("margin-top", property.Name);
            Assert.True(property.IsImportant);
            Assert.Equal("unset", property.Value);
        }

        [Fact]
        public void FontSize_RevertImportant_IsImportantWithRevertValue()
        {
            var property = ParseDeclaration("font-size: revert !important");
            Assert.Equal("font-size", property.Name);
            Assert.True(property.IsImportant);
            Assert.Equal("revert", property.Value);
        }
    }
}
