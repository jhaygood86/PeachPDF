namespace PeachPDF.Tests.CSS.PropertyTests
{
    using PeachPDF.CSS;
    using Xunit;

    /// <summary>
    /// CSS-object-model-level unit tests for calc()/min()/max()/clamp(): parsing, type-checking, and
    /// canonical text. Cascade/layout-level numeric resolution is tested separately in
    /// Integration/CalcIntegrationTests.cs.
    /// </summary>
    public class CalcPropertyTests : CssConstructionFunctions
    {
        // ── basic arithmetic (fully numeric/absolute -> folds to a plain value) ─────────────

        [Fact]
        public void Width_CalcAddition_FoldsToPixelLength()
        {
            var property = ParseDeclaration("width: calc(100px + 20px)");
            Assert.Equal("width", property.Name);
            Assert.IsType<WidthProperty>(property);
            var concrete = (WidthProperty)property;
            Assert.True(concrete.HasValue);
            Assert.Equal("120px", concrete.Value);
        }

        [Fact]
        public void Width_CalcSubtraction_FoldsToPixelLength()
        {
            var property = ParseDeclaration("width: calc(200px - 60px)");
            Assert.True(property.HasValue);
            Assert.Equal("140px", property.Value);
        }

        [Fact]
        public void Width_CalcMultiplication_FoldsToPixelLength()
        {
            var property = ParseDeclaration("width: calc(20px * 4)");
            Assert.True(property.HasValue);
            Assert.Equal("80px", property.Value);
        }

        [Fact]
        public void Width_CalcMultiplicationNumberFirst_FoldsToPixelLength()
        {
            var property = ParseDeclaration("width: calc(4 * 20px)");
            Assert.True(property.HasValue);
            Assert.Equal("80px", property.Value);
        }

        [Fact]
        public void Width_CalcDivision_FoldsToPixelLength()
        {
            var property = ParseDeclaration("width: calc(100px / 4)");
            Assert.True(property.HasValue);
            Assert.Equal("25px", property.Value);
        }

        [Fact]
        public void Width_CalcMixedAbsoluteUnits_FoldsToPixelLength()
        {
            // 1in = 96px, 2cm ~= 75.59px
            var property = ParseDeclaration("width: calc(1in + 2cm)");
            Assert.True(property.HasValue);
            Assert.StartsWith("171.", property.Value);
            Assert.EndsWith("px", property.Value);
        }

        // ── mixed relative units (can't fold until layout - canonical calc() text preserved) ─

        [Fact]
        public void Width_CalcMixedEmPx_PreservesCalcExpression()
        {
            var property = ParseDeclaration("width: calc(1em + 5px)");
            Assert.True(property.HasValue);
            Assert.Equal("calc(1em + 5px)", property.Value);
        }

        [Fact]
        public void Width_CalcPercentMinusPx_PreservesCalcExpression()
        {
            var property = ParseDeclaration("width: calc(100% - 20px)");
            Assert.True(property.HasValue);
            Assert.Equal("calc(100% - 20px)", property.Value);
        }

        // ── nesting ───────────────────────────────────────────────────────────────────────

        [Fact]
        public void Width_NestedCalc_FoldsToPixelLength()
        {
            var property = ParseDeclaration("width: calc(calc(10px + 10px) * 2)");
            Assert.True(property.HasValue);
            Assert.Equal("40px", property.Value);
        }

        [Fact]
        public void Width_ParenGrouping_FoldsToPixelLength()
        {
            var property = ParseDeclaration("width: calc((10px + 10px) * 2)");
            Assert.True(property.HasValue);
            Assert.Equal("40px", property.Value);
        }

        [Fact]
        public void Width_ParenGroupingMixedUnits_PreservesGroupingInCanonicalText()
        {
            // Without the parens this would mean "1em + (5px * 2)" = a different expression;
            // the canonical text must keep the grouping to stay semantically equivalent.
            var property = ParseDeclaration("width: calc((1em + 5px) * 2)");
            Assert.True(property.HasValue);
            Assert.Equal("calc((1em + 5px) * 2)", property.Value);
        }

        // ── min() / max() / clamp() ──────────────────────────────────────────────────────

        [Fact]
        public void Width_Min_FoldsToSmallerPixelLength()
        {
            var property = ParseDeclaration("width: min(150px, 100px)");
            Assert.True(property.HasValue);
            Assert.Equal("100px", property.Value);
        }

        [Fact]
        public void Width_Max_FoldsToLargerPixelLength()
        {
            var property = ParseDeclaration("width: max(150px, 100px)");
            Assert.True(property.HasValue);
            Assert.Equal("150px", property.Value);
        }

        [Fact]
        public void Width_MinMixedUnits_PreservesCanonicalText()
        {
            var property = ParseDeclaration("width: min(10px, 1em)");
            Assert.True(property.HasValue);
            Assert.Equal("min(10px, 1em)", property.Value);
        }

        [Fact]
        public void Width_ClampWithPercent_PreservesCanonicalText()
        {
            var property = ParseDeclaration("width: clamp(10px, 50%, 200px)");
            Assert.True(property.HasValue);
            Assert.Equal("clamp(10px, 50%, 200px)", property.Value);
        }

        [Fact]
        public void Width_ClampAllAbsolute_FoldsToPixelLength()
        {
            var property = ParseDeclaration("width: clamp(10px, 300px, 150px)");
            Assert.True(property.HasValue);
            Assert.Equal("150px", property.Value);
        }

        [Fact]
        public void Width_ClampWrongArgCount_IsInvalid()
        {
            var property = ParseDeclaration("width: clamp(10px, 20px)");
            Assert.False(property.HasValue);
        }

        [Fact]
        public void Width_MinNoArgs_IsInvalid()
        {
            var property = ParseDeclaration("width: min()");
            Assert.False(property.HasValue);
        }

        // ── plain-number category (NumberConverter-based properties) ────────────────────

        [Fact]
        public void FlexGrow_CalcNumberArithmetic_FoldsToPlainNumber()
        {
            var property = ParseDeclaration("flex-grow: calc(1 + 1)");
            Assert.True(property.HasValue);
            Assert.Equal("2", property.Value);
        }

        [Fact]
        public void FlexGrow_CalcWithLength_IsInvalid()
        {
            var property = ParseDeclaration("flex-grow: calc(1px + 2px)");
            Assert.False(property.HasValue);
        }

        // ── invalid / degenerate expressions ─────────────────────────────────────────────

        [Fact]
        public void Width_CalcDivideByZero_IsInvalid()
        {
            var property = ParseDeclaration("width: calc(10px / 0)");
            Assert.False(property.HasValue);
        }

        [Fact]
        public void Width_CalcAddNumberToLength_IsInvalid()
        {
            var property = ParseDeclaration("width: calc(10px + 5)");
            Assert.False(property.HasValue);
        }

        [Fact]
        public void Width_CalcMultiplyTwoLengths_IsInvalid()
        {
            var property = ParseDeclaration("width: calc(10px * 5px)");
            Assert.False(property.HasValue);
        }

        [Fact]
        public void Width_CalcDivideByLength_IsInvalid()
        {
            var property = ParseDeclaration("width: calc(10px / 5px)");
            Assert.False(property.HasValue);
        }

        [Fact]
        public void Width_CalcNoWhitespaceAroundPlus_IsInvalid()
        {
            var property = ParseDeclaration("width: calc(10px+5px)");
            Assert.False(property.HasValue);
        }

        [Fact]
        public void Width_CalcWhitespaceOnOneSideOfMinus_IsInvalid()
        {
            var property = ParseDeclaration("width: calc(10px -5px)");
            Assert.False(property.HasValue);
        }

        [Fact]
        public void Width_CalcUnbalancedParens_IsInvalid()
        {
            var property = ParseDeclaration("width: calc(10px + (5px)");
            Assert.False(property.HasValue);
        }

        [Fact]
        public void Width_CalcAngleUnit_IsInvalid()
        {
            var property = ParseDeclaration("width: calc(10deg + 5deg)");
            Assert.False(property.HasValue);
        }

        [Fact]
        public void Width_CalcEmpty_IsInvalid()
        {
            var property = ParseDeclaration("width: calc()");
            Assert.False(property.HasValue);
        }

        // ── var() interaction ─────────────────────────────────────────────────────────────

        [Fact]
        public void Width_CalcWithVar_StoredOpaquely()
        {
            // var() bypasses the strict per-property converter entirely (Property.TrySetValue routes
            // any var()-containing value to Converters.Any) - the raw text round-trips unresolved here;
            // DomParser resolves and re-validates it through the real converter at cascade time
            // (see Integration/CalcIntegrationTests.cs).
            var property = ParseDeclaration("width: calc(var(--x) + 10px)");
            Assert.True(property.HasValue);
            Assert.Contains("var(", property.Value);
            Assert.Contains("calc(", property.Value);
        }

        // ── transform function arguments ─────────────────────────────────────────────────

        [Fact]
        public void Transform_TranslateXCalc_Legal()
        {
            var property = ParseDeclaration("transform: translateX(calc(10px + 5px))");
            Assert.IsType<TransformProperty>(property);
            var concrete = (TransformProperty)property;
            Assert.True(concrete.HasValue);
            Assert.Equal("translateX(15px)", concrete.Value);
        }

        [Fact]
        public void Transform_ScaleCalc_Legal()
        {
            var property = ParseDeclaration("transform: scale(calc(1 + 1))");
            Assert.IsType<TransformProperty>(property);
            var concrete = (TransformProperty)property;
            Assert.True(concrete.HasValue);
            Assert.Equal("scale(2)", concrete.Value);
        }

        // ── border-radius two-value form ─────────────────────────────────────────────────

        [Fact]
        public void BorderTopLeftRadius_CalcFirstValue_Legal()
        {
            var property = ParseDeclaration("border-top-left-radius: calc(10px + 2px) 5px");
            Assert.IsType<BorderTopLeftRadiusProperty>(property);
            Assert.True(property.HasValue);
            Assert.Equal("12px 5px", property.Value);
        }
    }
}
