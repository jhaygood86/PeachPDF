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
            // px is now spec-correct (1px = 0.75pt, i.e. 96dpi): 1in = 96px, 2cm = 2*96/2.54 ~= 75.59px,
            // folding to ~171.59px. (Internally that's 1in = 72pt + 2cm = 56.69pt = 128.69pt, serialized
            // back to canonical px as 128.69 / 0.75.)
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

        // ── angle calc() (rotate/skew, gradient direction, hsl hue) ──────────────────────

        [Fact]
        public void Transform_RotateCalcSameUnit_FoldsToDegrees()
        {
            var property = ParseDeclaration("transform: rotate(calc(45deg + 10deg))");
            Assert.IsType<TransformProperty>(property);
            var concrete = (TransformProperty)property;
            Assert.True(concrete.HasValue);
            Assert.Equal("rotate(55deg)", concrete.Value);
        }

        [Fact]
        public void Transform_SkewXCalcMixedAngleUnits_FoldsToDegrees()
        {
            // 1turn / 4 = 90deg - division by a plain number is legal for an angle numerator.
            var property = ParseDeclaration("transform: skewX(calc(1turn / 4))");
            Assert.IsType<TransformProperty>(property);
            var concrete = (TransformProperty)property;
            Assert.True(concrete.HasValue);
            Assert.Equal("skewX(90deg)", concrete.Value);
        }

        [Fact]
        public void BackgroundImage_LinearGradientAngleCalc_FoldsToDegrees()
        {
            var property = ParseDeclaration("background-image: linear-gradient(calc(45deg + 45deg), red, blue)");
            Assert.True(property.HasValue);
            Assert.StartsWith("linear-gradient(90deg,", property.Value);
        }

        [Fact]
        public void Color_HslHueCalcPlainNumber_FoldsToPlainNumber()
        {
            // hue accepts either an angle or a bare number (implicit degrees) - calc() should too.
            var property = ParseDeclaration("color: hsl(calc(100 + 20), 50%, 50%)");
            Assert.True(property.HasValue);
            Assert.Equal("hsl(120, 50%, 50%)", property.Value);
        }

        [Fact]
        public void Transform_RotateCalcAngleAndLength_IsInvalid()
        {
            var property = ParseDeclaration("transform: rotate(calc(45deg + 10px))");
            Assert.False(property.HasValue);
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

        // ── length-only (no-percentage) converters: border-width, border-spacing ────────

        [Fact]
        public void BorderTopWidth_Calc_Legal()
        {
            // border-top-width goes through Converters.LineWidthConverter, a separate field from
            // LengthOrPercentConverter (border-width doesn't accept percentages) that also needs calc().
            var property = ParseDeclaration("border-top-width: calc(2px + 1px)");
            Assert.IsType<BorderTopWidthProperty>(property);
            Assert.True(property.HasValue);
            Assert.Equal("3px", property.Value);
        }

        [Fact]
        public void BorderTopWidth_CalcPercent_IsInvalid()
        {
            // border-width doesn't accept percentages, calc() or not.
            var property = ParseDeclaration("border-top-width: calc(50% - 2px)");
            Assert.False(property.HasValue);
        }

        [Fact]
        public void BorderSpacing_CalcTwoValueForm_Legal()
        {
            // border-spacing goes through Converters.LengthConverter (also percentage-free), a separate
            // field from LengthOrPercentConverter.
            var property = ParseDeclaration("border-spacing: calc(5px + 5px) 20px");
            Assert.True(property.HasValue);
            Assert.Equal("10px 20px", property.Value);
        }
    }
}
