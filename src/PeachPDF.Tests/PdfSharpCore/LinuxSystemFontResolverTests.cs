using PeachPDF.PdfSharpCore.Utils;

namespace PeachPDF.Tests.PdfSharpCoreTests
{
    public class LinuxSystemFontResolverTests
    {
        [Theory]
        [InlineData("/usr/share/fonts/Foo-Regular.ttf", true)]
        [InlineData("/usr/share/fonts/Foo-Regular.TTF", true)]
        [InlineData("/usr/share/fonts/Foo-Regular.otf", true)]
        [InlineData("/usr/share/fonts/Foo-Regular.OTF", true)]
        [InlineData("/usr/share/fonts/Foo-Regular.ttc", false)]
        [InlineData("/usr/share/fonts/readme.txt", false)]
        public void IsSupportedFontFile_MatchesTtfAndOtfOnly(string path, bool expected)
        {
            Assert.Equal(expected, LinuxSystemFontResolver.IsSupportedFontFile(path));
        }

        [Fact]
        public void Resolve_DoesNotThrow()
        {
            // Exercises the fontconfig P/Invoke path (or its managed fallback if fontconfig
            // isn't available) directly, regardless of the host OS the tests run on.
            var fonts = LinuxSystemFontResolver.Resolve();

            Assert.NotNull(fonts);
        }
    }
}
