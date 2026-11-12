using PeachBrackets.Css;

namespace PeachBrackets.Engine.Tests.CSS
{
    public class Flexbox : CssConstructionFunctions
    {
        [Theory]
        [MemberData(nameof(FlexDirectionTestDataValues))]
        public void FlexDirectionLegalValues(string value)
            => TestForLegalValue<FlexDirectionProperty>(PropertyNames.FlexDirection, value);

        [Theory]
        [MemberData(nameof(FlexWrapTestDataValues))]
        public void FlexWrapLegalValues(string value)
            => TestForLegalValue<FlexWrapProperty>(PropertyNames.FlexWrap, value);

        [Theory]
        [MemberData(nameof(OrderTestDataValues))]
        public void OrderLegalValues(string value)
            => TestForLegalValue<OrderProperty>(PropertyNames.Order, value);

        [Theory]
        [MemberData(nameof(FlexBasisTestDataValues))]
        public void FlexBasisLegalValues(string value)
            => TestForLegalValue<FlexBasisProperty>(PropertyNames.FlexBasis, value);

        [Theory]
        [MemberData(nameof(FlexGrowShrinkTestDataValues))]
        public void FlexGrowLegalValues(string value)
            => TestForLegalValue<FlexGrowProperty>(PropertyNames.FlexGrow, value);

        [Theory]
        [MemberData(nameof(FlexGrowShrinkTestDataValues))]
        public void FlexShrinkLegalValues(string value)
            => TestForLegalValue<FlexShrinkProperty>(PropertyNames.FlexShrink, value);

        [Theory]
        [MemberData(nameof(AlignContentTestDataValues))]
        public void AlignContentLegalValues(string value)
            => TestForLegalValue<AlignContentProperty>(PropertyNames.AlignContent, value);

        [Theory]
        [MemberData(nameof(AlignItemsTestDataValues))]
        public void AlignItemsLegalValues(string value)
            => TestForLegalValue<AlignItemsProperty>(PropertyNames.AlignItems, value);

        [Theory]
        [MemberData(nameof(AlignSelfTestDataValues))]
        public void AlignSelfLegalValues(string value)
            => TestForLegalValue<AlignSelfProperty>(PropertyNames.AlignSelf, value);

        [Theory]
        [MemberData(nameof(JustifyContentTestDataValues))]
        public void JustifyContentLegalValues(string value)
            => TestForLegalValue<JustifyContentProperty>(PropertyNames.JustifyContent, value);

        [Theory]
        [MemberData(nameof(FlexFlowTestDataValues))]
        public void FlexFlowLegalValues(string value)
            => TestForLegalValue<FlexFlowProperty>(PropertyNames.FlexFlow, value);

        [Theory]
        [MemberData(nameof(FlexTestDataValues))]
        public void FlexLegalValues(string value)
            => TestForLegalValue<FlexProperty>(PropertyNames.Flex, value);

        [Theory]
        [MemberData(nameof(AlignContentInvalidPrefixTestDataValues))]
        public void AlignContentIllegalPrefixValues(string value)
        {
            var snippet = $"align-content: {value}";
            var property = ParseDeclaration(snippet);
            Assert.Equal("align-content", property.Name);
            Assert.False(property.HasValue);
        }

        [Theory]
        [MemberData(nameof(AlignItemsInvalidPrefixTestDataValues))]
        public void AlignItemsIllegalPrefixValues(string value)
        {
            var snippet = $"align-items: {value}";
            var property = ParseDeclaration(snippet);
            Assert.Equal("align-items", property.Name);
            Assert.False(property.HasValue);
        }

        [Theory]
        [MemberData(nameof(JustifyContentInvalidPrefixTestDataValues))]
        public void JustifyContentIllegalPrefixValues(string value)
        {
            var snippet = $"align-items: {value}";
            var property = ParseDeclaration(snippet);
            Assert.Equal("align-items", property.Name);
            Assert.False(property.HasValue);
        }

        [Theory]
        [MemberData(nameof(FlexFlowExpandedTestValues))]
        public void FlexFlowShorthandValueExpanded(string propertyValue, string expectedDirection, string expectedWrap)
        {
            var source = $".test {{ flex-flow: {propertyValue}; }}";
            var styleSheet = ParseStyleSheet(source);
            var rule = (StyleRule)styleSheet.StyleRules.First();

            Assert.Equal(expectedDirection, rule.Style.FlexDirection);
            Assert.Equal(expectedWrap, rule.Style.FlexWrap);
        }

        [Theory]
        [InlineData("1")]
        [InlineData("2")]
        public void FlexShorthandOneValueExpanded(string propertyValue)
        {
            var source = $".test {{ flex: {propertyValue}; }}";
            var styleSheet = ParseStyleSheet(source);
            var rule = (StyleRule)styleSheet.StyleRules.First();

            Assert.Equal(propertyValue, rule.Style.FlexGrow);
            Assert.Equal("1", rule.Style.FlexShrink);
            Assert.Equal("0", rule.Style.FlexBasis);
        }

        [Theory]
        [InlineData("1 30px", "1", "1", "30px")]
        [InlineData("2 2", "2", "2", "0")]
        public void FlexShorthandTwoValuesExpanded(string propertyValue,
                                                   string expectedFlexGrow,
                                                   string expectedFlexShrink,
                                                   string expectedFlexBasis)
        {
            var source = $".test {{ flex: {propertyValue}; }}";
            var styleSheet = ParseStyleSheet(source);
            var rule = (StyleRule)styleSheet.StyleRules.First();

            Assert.Equal(expectedFlexGrow, rule.Style.FlexGrow);
            Assert.Equal(expectedFlexShrink, rule.Style.FlexShrink);
            Assert.Equal(expectedFlexBasis, rule.Style.FlexBasis);
        }

        [Theory]
        [InlineData("2 2 10%", "2", "2", "10%")]
        [InlineData("1 2 20em", "1", "2", "20em")]
        [InlineData("2 1 min-content", "2", "1", "min-content")]
        public void FlexShorthandThreeValuesExpanded(string propertyValue,
                                                     string expectedFlexGrow,
                                                     string expectedFlexShrink,
                                                     string expectedFlexBasis)
        {
            var source = $".test {{ flex: {propertyValue}; }}";
            var styleSheet = ParseStyleSheet(source);
            var rule = (StyleRule)styleSheet.StyleRules.First();

            Assert.Equal(propertyValue, rule.Style.Flex);
            Assert.Equal(expectedFlexGrow, rule.Style.FlexGrow);
            Assert.Equal(expectedFlexShrink, rule.Style.FlexShrink);
            Assert.Equal(expectedFlexBasis, rule.Style.FlexBasis);
        }

        public static IEnumerable<object[]> FlexDirectionTestDataValues
        {
            get
            {
                return new[]
                {
                    Keywords.Row,
                    Keywords.RowReverse,
                    Keywords.Column,
                    Keywords.ColumnReverse,
                }.Union(GlobalKeywordTestValues).ToObjectArray();
            }
        }

        public static IEnumerable<object[]> FlexWrapTestDataValues
        {
            get
            {
                return new[]
                {
                    Keywords.Nowrap,
                    Keywords.Wrap,
                    Keywords.WrapReverse,
                }.Union(GlobalKeywordTestValues).ToObjectArray();
            }
        }

        public static IEnumerable<object[]> FlexFlowTestDataValues
        {
            get
            {
                return new[]
                {
                    new object[] { "row" },
                    ["row-reverse"],
                    ["column"],
                    ["column-reverse"],
                    ["nowrap"],
                    ["wrap"],
                    ["wrap-reverse"],
                    ["row nowrap"],
                    ["column wrap"],
                    ["column-reverse wrap-reverse"],
                }.Union(GlobalKeywordTestValues.ToObjectArray(), ObjectArrayComparer.Instance);
            }
        }

        public static IEnumerable<object[]> FlexFlowExpandedTestValues
        {
            get
            {
                return new[]
                {
                    new object[] { "row nowrap", "row", "nowrap" },
                    ["column wrap", "column", "wrap"],
                    ["column-reverse wrap-reverse", "column-reverse", "wrap-reverse"],
                    // flex-flow is "<'flex-direction'> || <'flex-wrap'>" (CSS Flexbox 1 §5.1), so the two
                    // values are equally valid in the reverse order.
                    ["nowrap row", "row", "nowrap"],
                    ["wrap column", "column", "wrap"],
                    ["wrap-reverse column-reverse", "column-reverse", "wrap-reverse"],
                };
            }
        }

        public static IEnumerable<object[]> FlexTestDataValues
        {
            get
            {
                return new[]
                {
                    new object[] { "auto" },
                    ["initial"],
                    ["none"],
                    ["2"],
                    ["10em"],
                    ["30%"],
                    ["min-content"],
                    ["1 30px"],
                    ["2 2"],
                    ["2 2 10%"],
                }.Union(GlobalKeywordTestValues.ToObjectArray(), ObjectArrayComparer.Instance);
            }
        }

        public static IEnumerable<object[]> AlignContentTestDataValues
        {
            get
            {
                return new[]
                {
                    new object[] { Keywords.Center },
                    [Keywords.Start],
                    [Keywords.End],
                    [Keywords.FlexStart],
                    [Keywords.FlexEnd],
                    [Keywords.Normal],
                    [Keywords.Baseline],
                    [$"{Keywords.First} {Keywords.Baseline}"],
                    [$"{Keywords.Last} {Keywords.Baseline}"],
                    [Keywords.SpaceBetween],
                    [Keywords.SpaceAround],
                    [Keywords.SpaceEvenly],
                    [Keywords.Stretch],
                    [$"{Keywords.Safe} {Keywords.Center}"],
                    [$"{Keywords.Unsafe} {Keywords.Center}"],
                }.Union(GlobalKeywordTestValues.ToObjectArray(), ObjectArrayComparer.Instance);
            }
        }

        public static IEnumerable<object[]> AlignSelfTestDataValues
        {
            get
            {
                return new[]
                {
                    new object[] { Keywords.Auto },
                    [Keywords.Stretch],
                    [Keywords.Center],
                    [Keywords.Start],
                    [Keywords.End],
                    [Keywords.FlexStart],
                    [Keywords.FlexEnd],
                    [Keywords.Normal],
                    [Keywords.Baseline],
                    [$"{Keywords.First} {Keywords.Baseline}"],
                    [$"{Keywords.Last} {Keywords.Baseline}"],
                    [$"{Keywords.Safe} {Keywords.Center}"],
                    [$"{Keywords.Unsafe} {Keywords.Center}"],
                }.Union(GlobalKeywordTestValues.ToObjectArray(), ObjectArrayComparer.Instance);
            }
        }

        public static IEnumerable<object[]> JustifyContentTestDataValues
        {
            get
            {
                return new[]
                {
                    new object[] { Keywords.Center },
                    [Keywords.Start],
                    [Keywords.End],
                    [Keywords.FlexStart],
                    [Keywords.FlexEnd],
                    [Keywords.Left],
                    [Keywords.Right],
                    [Keywords.Normal],
                    [Keywords.SpaceBetween],
                    [Keywords.SpaceAround],
                    [Keywords.SpaceEvenly],
                    [Keywords.Stretch],
                    [$"{Keywords.Safe} {Keywords.Center}"],
                    [$"{Keywords.Unsafe} {Keywords.Center}"],
                }.Union(GlobalKeywordTestValues.ToObjectArray(), ObjectArrayComparer.Instance);
            }
        }

        public static IEnumerable<object[]> AlignItemsTestDataValues
        {
            get
            {
                return new[]
                {
                    new object[] { Keywords.Normal },
                    [Keywords.Stretch],
                    [Keywords.Center],
                    [Keywords.Start],
                    [Keywords.End],
                    [Keywords.FlexStart],
                    [Keywords.FlexEnd],
                    [Keywords.SelfStart],
                    [Keywords.SelfEnd],
                    [Keywords.Baseline],
                    [$"{Keywords.First} {Keywords.Baseline}"],
                    [$"{Keywords.Last} {Keywords.Baseline}"],
                    [$"{Keywords.Safe} {Keywords.Center}"],
                    [$"{Keywords.Unsafe} {Keywords.Center}"],
                }.Union(GlobalKeywordTestValues.ToObjectArray(), ObjectArrayComparer.Instance);
            }
        }


        public static IEnumerable<object[]> AlignContentInvalidPrefixTestDataValues
        {
            get
            {
                return new[]
                {
                    new object[] { $"{Keywords.Safe} {Keywords.Start}" },
                    [$"{Keywords.Safe} {Keywords.End}"],
                    [$"{Keywords.Safe} {Keywords.FlexStart}"],
                    [$"{Keywords.Safe} {Keywords.FlexEnd}"],

                    [$"{Keywords.Unsafe} {Keywords.Start}"],
                    [$"{Keywords.Unsafe} {Keywords.End}"],
                    [$"{Keywords.Unsafe} {Keywords.FlexStart}"],
                    [$"{Keywords.Unsafe} {Keywords.FlexEnd}"],

                    [$"{Keywords.First} {Keywords.Start}"],
                    [$"{Keywords.First} {Keywords.End}"],
                    [$"{Keywords.First} {Keywords.FlexStart}"],
                    [$"{Keywords.First} {Keywords.FlexEnd}"],

                    [$"{Keywords.Last} {Keywords.Start}"],
                    [$"{Keywords.Last} {Keywords.End}"],
                    [$"{Keywords.Last} {Keywords.FlexStart}"],
                    [$"{Keywords.Last} {Keywords.FlexEnd}"],
                };
            }
        }

        public static IEnumerable<object[]> AlignItemsInvalidPrefixTestDataValues
        {
            get
            {
                return new[]
                {
                    new object[] { $"{Keywords.Safe} {Keywords.Start}" },
                    [$"{Keywords.Safe} {Keywords.End}"],
                    [$"{Keywords.Safe} {Keywords.FlexStart}"],
                    [$"{Keywords.Safe} {Keywords.FlexEnd}"],
                    [$"{Keywords.Safe} {Keywords.SelfStart}"],
                    [$"{Keywords.Safe} {Keywords.SelfEnd}"],

                    [$"{Keywords.Unsafe} {Keywords.Start}"],
                    [$"{Keywords.Unsafe} {Keywords.End}"],
                    [$"{Keywords.Unsafe} {Keywords.FlexStart}"],
                    [$"{Keywords.Unsafe} {Keywords.FlexEnd}"],
                    [$"{Keywords.Unsafe} {Keywords.SelfStart}"],
                    [$"{Keywords.Unsafe} {Keywords.SelfEnd}"],

                    [$"{Keywords.First} {Keywords.Start}"],
                    [$"{Keywords.First} {Keywords.End}"],
                    [$"{Keywords.First} {Keywords.FlexStart}"],
                    [$"{Keywords.First} {Keywords.FlexEnd}"],
                    [$"{Keywords.First} {Keywords.SelfStart}"],
                    [$"{Keywords.First} {Keywords.SelfEnd}"],

                    [$"{Keywords.Last} {Keywords.Start}"],
                    [$"{Keywords.Last} {Keywords.End}"],
                    [$"{Keywords.Last} {Keywords.FlexStart}"],
                    [$"{Keywords.Last} {Keywords.FlexEnd}"],
                    [$"{Keywords.Last} {Keywords.SelfStart}"],
                    [$"{Keywords.Last} {Keywords.SelfEnd}"]
                };
            }
        }

        public static IEnumerable<object[]> JustifyContentInvalidPrefixTestDataValues
        {
            get
            {
                return new[]
                {
                    new object[] { $"{Keywords.Safe} {Keywords.Start}" },
                    [$"{Keywords.Safe} {Keywords.End}"],
                    [$"{Keywords.Safe} {Keywords.FlexStart}"],
                    [$"{Keywords.Safe} {Keywords.FlexEnd}"],
                    [$"{Keywords.Safe} {Keywords.SelfStart}"],
                    [$"{Keywords.Safe} {Keywords.SelfEnd}"],

                    [$"{Keywords.Unsafe} {Keywords.Start}"],
                    [$"{Keywords.Unsafe} {Keywords.End}"],
                    [$"{Keywords.Unsafe} {Keywords.FlexStart}"],
                    [$"{Keywords.Unsafe} {Keywords.FlexEnd}"],
                    [$"{Keywords.Unsafe} {Keywords.SelfStart}"],
                    [$"{Keywords.Unsafe} {Keywords.SelfEnd}"],
                };
            }
        }

        public static IEnumerable<object[]> FlexGrowShrinkTestDataValues
        {
            get
            {
                return new[]
                {
                    new object[] { "3" },
                    ["0.6"]
                }.Union(GlobalKeywordTestValues.ToObjectArray(), ObjectArrayComparer.Instance);
            }
        }

        public static IEnumerable<object[]> FlexBasisTestDataValues
        {
            get
            {
                return new[]
                {
                    new object[] { "10em" },
                    ["3px"],
                    ["50%"],
                    [Keywords.MinContent],
                    [Keywords.MaxContent],
                    [Keywords.FitContent],
                    [Keywords.Content],
                    [Keywords.Auto]
                }.Union(GlobalKeywordTestValues.ToObjectArray(), ObjectArrayComparer.Instance);
            }
        }

        public static IEnumerable<object[]> OrderTestDataValues
        {
            get
            {
                return new[]
                {
                    new object[] { "-1" },
                    ["1"],
                }.Union(GlobalKeywordTestValues.ToObjectArray(), ObjectArrayComparer.Instance);
            }
        }
    }
}







