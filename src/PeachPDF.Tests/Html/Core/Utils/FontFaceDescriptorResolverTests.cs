using PeachDrawing.Text;
using PeachPDF.Html.Core.Utils;


namespace PeachPDF.Tests.Html.Core.Utils
{
    /// <summary>
    /// Unit tests for <see cref="FontFaceDescriptorResolver"/> - resolving an <c>@font-face</c> rule's own
    /// <c>font-weight</c>/<c>font-style</c>/<c>font-stretch</c> descriptor strings into the ranges the font set's <c>AddData</c>
    /// takes. See <c>FontFaceRangesIntegrationTests</c> and <c>FontFactoryFontFaceDescriptorOverrideIntegrationTests</c> for the equivalent
    /// coverage through the real cascade.
    /// </summary>
    public class FontFaceDescriptorResolverTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ResolveWeight_AbsentDescriptor_ReturnsNull(string? descriptor)
        {
            Assert.Null(FontFaceDescriptorResolver.ResolveWeight(descriptor));
        }

        [Theory]
        [InlineData("400", 400)]
        [InlineData("700", 700)]
        [InlineData("normal", 400)]
        [InlineData("bold", 700)]
        [InlineData("BOLD", 700)]
        [InlineData("350.5", 350.5)]
        public void ResolveWeight_SingleToken_ResolvesToOneWeight(string descriptor, double expected)
        {
            Assert.Equal(new AxisRange(expected), FontFaceDescriptorResolver.ResolveWeight(descriptor));
        }

        [Theory]
        [InlineData("100 900", 100, 900)]
        [InlineData("900 100", 100, 900)]
        [InlineData("normal bold", 400, 700)]
        [InlineData("  200   300 ", 200, 300)]
        public void ResolveWeight_TwoTokens_ResolveToARange(string descriptor, double minimum, double maximum)
        {
            Assert.Equal(new AxisRange(minimum, maximum), FontFaceDescriptorResolver.ResolveWeight(descriptor));
        }

        [Theory]
        [InlineData("not-a-weight")]
        [InlineData("auto")]
        [InlineData("bolder")]
        [InlineData("lighter")]
        [InlineData("0")]
        [InlineData("1001")]
        [InlineData("100 900 500")]
        [InlineData("100 wide")]
        public void ResolveWeight_Unusable_ReturnsNull(string descriptor)
        {
            Assert.Null(FontFaceDescriptorResolver.ResolveWeight(descriptor));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("auto")]
        [InlineData("slanted")]
        public void ResolveStyle_AbsentOrUnusable_SaysNothing(string? descriptor)
        {
            Assert.Equal((null, null), FontFaceDescriptorResolver.ResolveStyle(descriptor));
        }

        [Theory]
        [InlineData("italic", true)]
        [InlineData("normal", false)]
        public void ResolveStyle_Keyword_ResolvesItalicness(string descriptor, bool expected)
        {
            Assert.Equal((expected, null), FontFaceDescriptorResolver.ResolveStyle(descriptor));
        }

        [Fact]
        public void ResolveStyle_BareOblique_IsTheDefaultObliqueAngle()
        {
            Assert.Equal((true, new AxisRange(14)), FontFaceDescriptorResolver.ResolveStyle("oblique"));
        }

        [Theory]
        [InlineData("oblique 10deg", 10, 10)]
        [InlineData("oblique 0deg 14deg", 0, 14)]
        [InlineData("oblique 20deg 10deg", 10, 20)]
        [InlineData("oblique -10deg 10deg", -10, 10)]
        [InlineData("OBLIQUE 10DEG", 10, 10)]
        public void ResolveStyle_ObliqueWithAngles_ResolvesTheRange(string descriptor, double minimum, double maximum)
        {
            Assert.Equal((true, new AxisRange(minimum, maximum)), FontFaceDescriptorResolver.ResolveStyle(descriptor));
        }

        [Fact]
        public void ResolveStyle_ObliqueWithATurn_ConvertsToDegrees()
        {
            var (isItalic, oblique) = FontFaceDescriptorResolver.ResolveStyle("oblique 0.05turn");

            Assert.True(isItalic);
            Assert.Equal(18, oblique!.Value.Minimum, 3);
        }

        [Theory]
        [InlineData("oblique 100deg")]
        [InlineData("oblique 10")]
        [InlineData("oblique 1deg 2deg 3deg")]
        [InlineData("italic 10deg")]
        [InlineData("normal 10deg")]
        public void ResolveStyle_MalformedOblique_SaysNothing(string descriptor)
        {
            Assert.Equal((null, null), FontFaceDescriptorResolver.ResolveStyle(descriptor));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("auto")]
        public void ResolveStretch_AbsentDescriptor_ReturnsNull(string? descriptor)
        {
            Assert.Null(FontFaceDescriptorResolver.ResolveStretch(descriptor));
        }

        [Theory]
        [InlineData("condensed", 75)]
        [InlineData("expanded", 125)]
        [InlineData("normal", 100)]
        [InlineData("75%", 75)]
        [InlineData("87.5%", 87.5)]
        public void ResolveStretch_SingleValue_ResolvesToOneWidth(string descriptor, double expected)
        {
            Assert.Equal(new AxisRange(expected), FontFaceDescriptorResolver.ResolveStretch(descriptor));
        }

        [Theory]
        [InlineData("75% 125%", 75, 125)]
        [InlineData("125% 75%", 75, 125)]
        [InlineData("condensed expanded", 75, 125)]
        [InlineData("50% expanded", 50, 125)]
        public void ResolveStretch_TwoValues_ResolveToARange(string descriptor, double minimum, double maximum)
        {
            Assert.Equal(new AxisRange(minimum, maximum), FontFaceDescriptorResolver.ResolveStretch(descriptor));
        }

        [Theory]
        [InlineData("wide")]
        [InlineData("-10%")]
        [InlineData("75% 100% 125%")]
        [InlineData("75%wide")]
        public void ResolveStretch_Unusable_ReturnsNull(string descriptor)
        {
            Assert.Null(FontFaceDescriptorResolver.ResolveStretch(descriptor));
        }

        [Fact]
        public void Resolve_CombinesTheThreeDescriptors()
        {
            var descriptors = FontFaceDescriptorResolver.Resolve("100 900", "oblique 0deg 14deg", "75% 125%");

            Assert.Equal(new AxisRange(100, 900), descriptors.Weight);
            Assert.True(descriptors.IsItalic);
            Assert.Equal(new AxisRange(0, 14), descriptors.Oblique);
            Assert.Equal(new AxisRange(75, 125), descriptors.Width);
        }

        [Fact]
        public void AnyWhitespace_SeparatesTheTwoValuesOfARange()
        {
            Assert.Equal(new AxisRange(100, 900), FontFaceDescriptorResolver.ResolveWeight("100\t900"));
            Assert.Equal(new AxisRange(75, 125), FontFaceDescriptorResolver.ResolveStretch("75%\n125%"));
            Assert.Equal((true, new AxisRange(0, 14)), FontFaceDescriptorResolver.ResolveStyle("oblique\t0deg\r\n14deg"));
        }

        [Fact]
        public void Resolve_OfNothing_IsTheDefault()
        {
            Assert.Equal(default, FontFaceDescriptorResolver.Resolve(null, null, null));
        }
    }
}
