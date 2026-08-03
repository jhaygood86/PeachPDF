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
            var supports = (SupportsRule)sheet.Rules[0];
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
            var supports = (SupportsRule)sheet.Rules[0];
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
            var supports = (SupportsRule)sheet.Rules[0];
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
            var supports = (SupportsRule)sheet.Rules[0];
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
            var supports = (SupportsRule)sheet.Rules[0];
            Assert.Equal("((background-transparency: zero))", supports.ConditionText);
            Assert.False(supports.Condition.Check());
        }

        [Fact]
        public void SupportsBackgroundRedWithImportantRule()
        {
            // "background" is a shorthand - never authored into css-properties.json directly (Layer B
            // is longhand-only) - so this exercises DeclarationCondition.Check()'s shorthand fallback:
            // every longhand background expands to (background-color/-image/-position/-size/-repeat/
            // -origin/-clip/-attachment) is one Layer B genuinely dispatches.
            var source = @"@supports (background: red !important) { }";
            var sheet = ParseStyleSheet(source);
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<SupportsRule>(sheet.Rules[0]);
            var supports = (SupportsRule)sheet.Rules[0];
            Assert.Equal("(background: red !important)", supports.ConditionText);
            Assert.True(supports.Condition.Check());
        }

        [Fact]
        public void SupportsBackgroundInheritRule()
        {
            // A CSS-wide keyword is valid for a recognized shorthand too, taking the shorthand
            // fallback's own keyword short-circuit rather than the per-longhand check.
            var source = @"@supports (background: inherit) { }";
            var sheet = ParseStyleSheet(source);
            var supports = (SupportsRule)sheet.Rules[0];
            Assert.True(supports.Condition.Check());
        }

        // The old oracle (Property.TrySetValue) is a proven mismatch in both directions:
        // animation-name parses fine in the CSS-OM but Layer B never dispatches it (false positive);
        // fill isn't registered in the CSS-OM's PropertyFactory at all despite full SVG support
        // (false negative). These two prove DeclarationCondition.Check() now gets both right.
        [Fact]
        public void SupportsAnimationNameRule_NoLongerFalsePositive()
        {
            var source = @"@supports (animation-name: spin) { }";
            var sheet = ParseStyleSheet(source);
            var supports = (SupportsRule)sheet.Rules[0];
            Assert.False(supports.Condition.Check());
        }

        [Fact]
        public void SupportsSvgFillRule_NoLongerFalseNegative()
        {
            var source = @"@supports (fill: red) { }";
            var sheet = ParseStyleSheet(source);
            var supports = (SupportsRule)sheet.Rules[0];
            Assert.True(supports.Condition.Check());
        }

        [Theory]
        [InlineData("inherit")]
        [InlineData("initial")]
        [InlineData("unset")]
        [InlineData("revert")]
        [InlineData("revert-layer")]
        public void SupportsCssWideKeywordRule_AlwaysSupportedForARecognizedProperty(string keyword)
        {
            // Every property accepts the CSS-wide keywords (CSS Cascade & Inheritance 4 §7.3) - even
            // "color: unset" wouldn't otherwise pass CssPropertyRegistry.SupportsDeclaration's own
            // color grammar check.
            var source = $@"@supports (color: {keyword}) {{ }}";
            var sheet = ParseStyleSheet(source);
            var supports = (SupportsRule)sheet.Rules[0];
            Assert.True(supports.Condition.Check());
        }

        [Fact]
        public void SupportsCssWideKeywordRule_StillFailsForAnUnrecognizedProperty()
        {
            // A CSS-wide keyword is valid for any REAL property, but doesn't make up for the property
            // itself not existing - background-transparency isn't a property PeachPDF (or any CSS
            // implementation) recognizes under any value.
            var source = @"@supports (background-transparency: inherit) { }";
            var sheet = ParseStyleSheet(source);
            var supports = (SupportsRule)sheet.Rules[0];
            Assert.False(supports.Condition.Check());
        }

        // transform's cssDataType is 'cssom' (permissive - matches real dispatch, which still applies a
        // transform-list's recognized functions even when one is unimplemented) but its supportsDataType
        // override ('transform', backed by CssValueParser.IsValidTransformValue) is stricter: perspective()
        // parses as valid CSS but paint silently drops it, so @supports must say no even though the
        // declaration itself would still be accepted and stored by real cascade dispatch.
        [Fact]
        public void SupportsTransformRotateRule()
        {
            var source = @"@supports (transform: rotate(10deg)) { }";
            var sheet = ParseStyleSheet(source);
            var supports = (SupportsRule)sheet.Rules[0];
            Assert.True(supports.Condition.Check());
        }

        [Fact]
        public void SupportsTransformPerspectiveRule_NotGenuinelyRendered()
        {
            var source = @"@supports (transform: perspective(300px)) { }";
            var sheet = ParseStyleSheet(source);
            var supports = (SupportsRule)sheet.Rules[0];
            Assert.False(supports.Condition.Check());
        }

        // break-before's cssDataType includes region/avoid-region (real dispatch stores any css-break-4
        // keyword), but its supportsDataType override excludes them - BreakValues.cs's fragmentation logic
        // never acts on them (no FragmentationContext.Region), so @supports must say no.
        [Fact]
        public void SupportsBreakBeforeRegionRule_NotGenuinelyEnforced()
        {
            var source = @"@supports (break-before: region) { }";
            var sheet = ParseStyleSheet(source);
            var supports = (SupportsRule)sheet.Rules[0];
            Assert.False(supports.Condition.Check());
        }

        [Fact]
        public void SupportsBreakBeforePageRule()
        {
            var source = @"@supports (break-before: page) { }";
            var sheet = ParseStyleSheet(source);
            var supports = (SupportsRule)sheet.Rules[0];
            Assert.True(supports.Condition.Check());
        }

        // column-span's cssDataType includes 'all' (real dispatch/ColumnSpanProperty stores it), but its
        // supportsDataType override excludes it - CssLayoutEngineColumns.cs never reads ColumnSpan, so
        // 'column-span: all' has no layout effect and @supports must say no. Same shape as break-before's
        // region/avoid-region split above.
        [Fact]
        public void SupportsColumnSpanAllRule_NotGenuinelyEnforced()
        {
            var source = @"@supports (column-span: all) { }";
            var sheet = ParseStyleSheet(source);
            var supports = (SupportsRule)sheet.Rules[0];
            Assert.False(supports.Condition.Check());
        }

        [Fact]
        public void SupportsColumnSpanNoneRule()
        {
            var source = @"@supports (column-span: none) { }";
            var sheet = ParseStyleSheet(source);
            var supports = (SupportsRule)sheet.Rules[0];
            Assert.True(supports.Condition.Check());
        }

        // word-break's cssDataType includes 'keep-all' (Layer A's own WordBreakConverter/Map.WordBreaks
        // parses and stores it), but its supportsDataType override excludes it - CssBox.cs's line-breaking
        // logic only special-cases break-all, so 'keep-all' has no distinct effect and @supports must say
        // no. Same shape as break-before/column-span above.
        [Fact]
        public void SupportsWordBreakKeepAllRule_NotGenuinelyEnforced()
        {
            var source = @"@supports (word-break: keep-all) { }";
            var sheet = ParseStyleSheet(source);
            var supports = (SupportsRule)sheet.Rules[0];
            Assert.False(supports.Condition.Check());
        }

        [Fact]
        public void SupportsWordBreakAllRule()
        {
            var source = @"@supports (word-break: break-all) { }";
            var sheet = ParseStyleSheet(source);
            var supports = (SupportsRule)sheet.Rules[0];
            Assert.True(supports.Condition.Check());
        }

        // direction's enum-keyword validator now checks Map.DirectionModes' own keys instead of
        // unconditionally returning true - a value outside {ltr, rtl} must fail.
        [Fact]
        public void SupportsDirectionRule()
        {
            var source = @"@supports (direction: rtl) { }";
            var sheet = ParseStyleSheet(source);
            var supports = (SupportsRule)sheet.Rules[0];
            Assert.True(supports.Condition.Check());
        }

        [Fact]
        public void SupportsDirectionGarbageValueRule_NoLongerAlwaysTrue()
        {
            var source = @"@supports (direction: sideways) { }";
            var sheet = ParseStyleSheet(source);
            var supports = (SupportsRule)sheet.Rules[0];
            Assert.False(supports.Condition.Check());
        }

        // display's cssDataType is a real keyword list (not 'cssom') - a syntactically-plausible but
        // unimplemented value like 'contents' must report unsupported.
        [Fact]
        public void SupportsDisplayContentsRule_NotImplemented()
        {
            var source = @"@supports (display: contents) { }";
            var sheet = ParseStyleSheet(source);
            var supports = (SupportsRule)sheet.Rules[0];
            Assert.False(supports.Condition.Check());
        }

        [Fact]
        public void SupportsPaddingTopOrPaddingLeftRule()
        {
            var source = @"@supports ((padding-TOP :  0) or (padding-left : 0)) { }";
            var sheet = ParseStyleSheet(source);
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<SupportsRule>(sheet.Rules[0]);
            var supports = (SupportsRule)sheet.Rules[0];
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
            var supports = (SupportsRule)sheet.Rules[0];
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
            var supports = (SupportsRule)sheet.Rules[0];
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
            var supports = (SupportsRule)sheet.Rules[0];
            Assert.Equal("((display: flex))", supports.ConditionText);
            Assert.True(supports.Condition.Check());
        }

        // PeachPDF's CSS-OM parses transition-property/animation-name fine (it understands the
        // grammar), but the render engine never dispatches either - they aren't authored into
        // css-properties.json (see CLAUDE.md's generator section), so both branches of the OR are
        // false and the AND short-circuits false regardless of `transform` (which genuinely is
        // supported). This is the exact false-positive the old oracle (CSS-OM grammar validity
        // alone) got wrong; asserting False here is what proves the fix, not a regression.
        [Fact]
        public void SupportsTransitionOrAnimationNameAndTransformFrontBracketRule()
        {
            var source = @"@supports ((transition-property: color) or
           (animation-name: foo)) and
          (transform: rotate(10deg)) { }";
            var sheet = ParseStyleSheet(source);
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<SupportsRule>(sheet.Rules[0]);
            var supports = (SupportsRule)sheet.Rules[0];
            Assert.Equal("((transition-property: color) or (animation-name: foo)) and (transform: rotate(10deg))", supports.ConditionText);
            Assert.False(supports.Condition.Check());
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
            var supports = (SupportsRule)sheet.Rules[0];
            Assert.Equal("(transition-property: color) or ((animation-name: foo) and (transform: rotate(10deg)))", supports.ConditionText);
            Assert.False(supports.Condition.Check());
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
            var supports = (SupportsRule)sheet.Rules[0];
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
            var supports = (SupportsRule)sheet.Rules[0];
            Assert.Equal(3, supports.Rules.Length);
            Assert.Equal("not (display: flex)", supports.ConditionText);
            Assert.False(supports.Condition.Check());
        }
    }
}







