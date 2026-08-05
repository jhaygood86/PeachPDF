namespace PeachPDF.Tests.CSS
{
    using PeachPDF.CSS;
    using Xunit;

    public class ParserExtensionsCreateRuleTests
    {
        [Theory]
        [InlineData((byte)RuleType.Charset, typeof(CharsetRule))]
        [InlineData((byte)RuleType.Document, typeof(DocumentRule))]
        [InlineData((byte)RuleType.FontFace, typeof(FontFaceRule))]
        [InlineData((byte)RuleType.Import, typeof(ImportRule))]
        [InlineData((byte)RuleType.Keyframe, typeof(KeyframeRule))]
        [InlineData((byte)RuleType.Keyframes, typeof(KeyframesRule))]
        [InlineData((byte)RuleType.Media, typeof(MediaRule))]
        [InlineData((byte)RuleType.Container, typeof(ContainerRule))]
        [InlineData((byte)RuleType.Namespace, typeof(NamespaceRule))]
        [InlineData((byte)RuleType.Page, typeof(PageRule))]
        [InlineData((byte)RuleType.Style, typeof(StyleRule))]
        [InlineData((byte)RuleType.Supports, typeof(SupportsRule))]
        [InlineData((byte)RuleType.Viewport, typeof(ViewportRule))]
        public void CreateRule_KnownRuleType_ReturnsExpectedRuleSubtype(byte typeValue, System.Type expectedType)
        {
            var parser = new StylesheetParser();

            var rule = parser.CreateRule((RuleType)typeValue);

            Assert.NotNull(rule);
            Assert.IsType(expectedType, rule);
        }

        [Theory]
        [InlineData((byte)RuleType.Unknown)]
        [InlineData((byte)RuleType.RegionStyle)]
        [InlineData((byte)RuleType.FontFeatureValues)]
        [InlineData((byte)RuleType.CounterStyle)]
        public void CreateRule_RuleTypeWithNoSubtype_ReturnsNull(byte typeValue)
        {
            var parser = new StylesheetParser();

            Assert.Null(parser.CreateRule((RuleType)typeValue));
        }
    }
}
