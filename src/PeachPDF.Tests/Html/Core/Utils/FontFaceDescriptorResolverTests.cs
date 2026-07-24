using PeachPDF.Html.Core.Utils;

using PeachPDF.Fonts;

namespace PeachPDF.Tests.Html.Core.Utils
{
    /// <summary>
    /// Unit tests for <see cref="FontFaceDescriptorResolver"/> - resolving an <c>@font-face</c> rule's own
    /// <c>font-weight</c>/<c>font-style</c>/<c>font-stretch</c> descriptor strings into the override values
    /// <c>PeachPDF.Fonts.FontResolver.AddFont</c> takes. See
    /// <c>FontFactoryFontFaceDescriptorOverrideIntegrationTests</c> for the equivalent coverage through the
    /// real cascade.
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
        public void ResolveWeight_SingleToken_ResolvesExpectedValue(string descriptor, int expected)
        {
            Assert.Equal(expected, FontFaceDescriptorResolver.ResolveWeight(descriptor));
        }

        [Fact]
        public void ResolveWeight_VariableFontRange_ResolvesToLowerBound()
        {
            Assert.Equal(100, FontFaceDescriptorResolver.ResolveWeight("100 900"));
        }

        [Fact]
        public void ResolveWeight_Unparseable_ReturnsNull()
        {
            Assert.Null(FontFaceDescriptorResolver.ResolveWeight("not-a-weight"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void ResolveIsItalic_AbsentDescriptor_ReturnsNull(string? descriptor)
        {
            Assert.Null(FontFaceDescriptorResolver.ResolveIsItalic(descriptor));
        }

        [Theory]
        [InlineData("italic", true)]
        [InlineData("oblique", true)]
        [InlineData("normal", false)]
        public void ResolveIsItalic_RecognizedKeyword_ResolvesExpectedValue(string descriptor, bool expected)
        {
            Assert.Equal(expected, FontFaceDescriptorResolver.ResolveIsItalic(descriptor));
        }

        [Fact]
        public void ResolveIsItalic_ObliqueWithAngleRange_ResolvesTrue()
        {
            Assert.True(FontFaceDescriptorResolver.ResolveIsItalic("oblique 10deg 20deg"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void ResolveStretch_AbsentDescriptor_ReturnsNull(string? descriptor)
        {
            Assert.Null(FontFaceDescriptorResolver.ResolveStretch(descriptor));
        }

        [Theory]
        [InlineData("condensed", 3)]
        [InlineData("expanded", 7)]
        [InlineData("normal", 5)]
        public void ResolveStretch_RecognizedKeyword_ResolvesExpectedValue(string descriptor, int expected)
        {
            Assert.Equal(expected, FontFaceDescriptorResolver.ResolveStretch(descriptor));
        }

        [Fact]
        public void ResolveStretch_PercentageValue_ReturnsNull()
        {
            // Variable-font stretch percentage syntax is out of scope - must not be silently coerced.
            Assert.Null(FontFaceDescriptorResolver.ResolveStretch("75%"));
        }
    }
}
