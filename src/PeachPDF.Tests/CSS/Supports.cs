namespace PeachPDF.Tests.CSS
{
    using PeachPDF.CSS;
    using System;
    using Xunit;

    public class CssSupportsTests : CssConstructionFunctions
    {
        [Fact]
        public void SupportsEmptyRule()
        {
            var source = @"@supports () { }";
            var sheet = ParseStyleSheet(source);
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<SupportsRule>(sheet.Rules[0]);
            var supports = sheet.Rules[0] as SupportsRule;
            Assert.Equal(String.Empty, supports.ConditionText);
            Assert.True(supports.Condition.Check());
        }

        [Fact]
        public void SupportsBackgroundColorRedRule()
        {
            var source = @"@supports (background-color: red) { }";
            var sheet = ParseStyleSheet(source);
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<SupportsRule>(sheet.Rules[0]);
            var supports = sheet.Rules[0] as SupportsRule;
            Assert.Equal("(background-color: red)", supports.ConditionText);
            Assert.True(supports.Condition.Check());
        }

        [Fact]
        public void SupportsBackgroundColorRedAndColorBlueRule()
        {
            var source = @"@supports ((background-color: red) and (color: blue)) { }";
            var sheet = ParseStyleSheet(source);
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<SupportsRule>(sheet.Rules[0]);
            var supports = sheet.Rules[0] as SupportsRule;
            Assert.Equal("((background-color: red) and (color: blue))", supports.ConditionText);
            Assert.True(supports.Condition.Check());
        }

        [Fact]
        public void SupportsNotUnsupportedDeclarationRule()
        {
            var source = @"@supports (not (background-transparency: half)) { }";
            var sheet = ParseStyleSheet(source);
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<SupportsRule>(sheet.Rules[0]);
            var supports = sheet.Rules[0] as SupportsRule;
            Assert.Equal("(not (background-transparency: half))", supports.ConditionText);
            Assert.True(supports.Condition.Check());
        }

        [Fact]
        public void SupportsUnsupportedDeclarationRule()
        {
            var source = @"@supports ((background-transparency: zero)) { }";
            var sheet = ParseStyleSheet(source);
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<SupportsRule>(sheet.Rules[0]);
            var supports = sheet.Rules[0] as SupportsRule;
            Assert.Equal("((background-transparency: zero))", supports.ConditionText);
            Assert.False(supports.Condition.Check());
        }

        [Fact]
        public void SupportsBackgroundRedWithImportantRule()
        {
            var source = @"@supports (background: red !important) { }";
            var sheet = ParseStyleSheet(source);
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<SupportsRule>(sheet.Rules[0]);
            var supports = sheet.Rules[0] as SupportsRule;
            Assert.Equal("(background: red !important)", supports.ConditionText);
            Assert.True(supports.Condition.Check());
        }

        [Fact]
        public void SupportsPaddingTopOrPaddingLeftRule()
        {
            var source = @"@supports ((padding-TOP :  0) or (padding-left : 0)) { }";
            var sheet = ParseStyleSheet(source);
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<SupportsRule>(sheet.Rules[0]);
            var supports = sheet.Rules[0] as SupportsRule;
            Assert.Equal("((padding-top: 0) or (padding-left: 0))", supports.ConditionText);
            Assert.True(supports.Condition.Check());
        }

        [Fact]
        public void SupportsPaddingTopOrPaddingLeftAndPaddingBottomOrPaddingRightRule()
        {
            var source = @"@supports (((padding-top: 0)  or  (padding-left: 0))  and  ((padding-bottom:  0)  or  (padding-right: 0))) { }";
            var sheet = ParseStyleSheet(source);
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<SupportsRule>(sheet.Rules[0]);
            var supports = sheet.Rules[0] as SupportsRule;
            Assert.Equal("(((padding-top: 0) or (padding-left: 0)) and ((padding-bottom: 0) or (padding-right: 0)))", supports.ConditionText);
            Assert.True(supports.Condition.Check());
        }

        [Fact]
        public void SupportsDisplayFlexWithImportantRule()
        {
            var source = @"@supports (display: flex !important) { }";
            var sheet = ParseStyleSheet(source);
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<SupportsRule>(sheet.Rules[0]);
            var supports = sheet.Rules[0] as SupportsRule;
            Assert.Equal("(display: flex !important)", supports.ConditionText);
            Assert.True(supports.Condition.Check());
        }

        [Fact]
        public void SupportsBareDisplayFlexRule()
        {
            var source = @"@supports display: flex { }";
            var sheet = ParseStyleSheet(source);
            Assert.Equal(0, sheet.Rules.Length);
        }

        [Fact]
        public void SupportsDisplayFlexMultipleBracketsRule()
        {
            var source = @"@supports ((display: flex)) { }";
            var sheet = ParseStyleSheet(source);
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<SupportsRule>(sheet.Rules[0]);
            var supports = sheet.Rules[0] as SupportsRule;
            Assert.Equal("((display: flex))", supports.ConditionText);
            Assert.True(supports.Condition.Check());
        }

        [Fact]
        public void SupportsTransitionOrAnimationNameAndTransformFrontBracketRule()
        {
            var source = @"@supports ((transition-property: color) or
           (animation-name: foo)) and
          (transform: rotate(10deg)) { }";
            var sheet = ParseStyleSheet(source);
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<SupportsRule>(sheet.Rules[0]);
            var supports = sheet.Rules[0] as SupportsRule;
            Assert.Equal("((transition-property: color) or (animation-name: foo)) and (transform: rotate(10deg))", supports.ConditionText);
            Assert.True(supports.Condition.Check());
        }

        [Fact]
        public void SupportsTransitionOrAnimationNameAndTransformBackBracketRule()
        {
            var source = @"@supports (transition-property: color) or
           ((animation-name: foo) and
          (transform: rotate(10deg))) { }";
            var sheet = ParseStyleSheet(source);
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<SupportsRule>(sheet.Rules[0]);
            var supports = sheet.Rules[0] as SupportsRule;
            Assert.Equal("(transition-property: color) or ((animation-name: foo) and (transform: rotate(10deg)))", supports.ConditionText);
            Assert.True(supports.Condition.Check());
        }

        [Fact]
        public void SupportsShadowVendorPrefixesRule()
        {
            var source = @"@supports ( box-shadow: 0 0 2px black ) or
          ( -moz-box-shadow: 0 0 2px black ) or
          ( -webkit-box-shadow: 0 0 2px black ) or
          ( -o-box-shadow: 0 0 2px black ) { }";
            var sheet = ParseStyleSheet(source);
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<SupportsRule>(sheet.Rules[0]);
            var supports = sheet.Rules[0] as SupportsRule;
            Assert.Equal("(box-shadow: 0 0 2px black) or (-moz-box-shadow: 0 0 2px black) or (-webkit-box-shadow: 0 0 2px black) or (-o-box-shadow: 0 0 2px black)", supports.ConditionText);
            Assert.True(supports.Condition.Check());
        }

        [Fact]
        public void SupportsNegatedDisplayFlexRuleWithDeclarations()
        {
            var source = @"@supports not ( display: flex ) {
  body { width: 100%; height: 100%; background: white; color: black; }
  #navigation { width: 25%; }
  #article { width: 75%; }
}";
            var sheet = ParseStyleSheet(source);
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<SupportsRule>(sheet.Rules[0]);
            var supports = sheet.Rules[0] as SupportsRule;
            Assert.Equal(3, supports.Rules.Length);
            Assert.Equal("not (display: flex)", supports.ConditionText);
            Assert.False(supports.Condition.Check());
        }
    }
}







