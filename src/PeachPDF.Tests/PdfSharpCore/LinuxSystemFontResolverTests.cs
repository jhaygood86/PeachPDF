using PeachPDF.PdfSharpCore.Utils;
using System;

using PeachPDF.Fonts;

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

        [Theory]
        [InlineData("serif")]
        [InlineData("sans-serif")]
        [InlineData("monospace")]
        [InlineData("cursive")]
        [InlineData("fantasy")]
        public void ResolveGenericFamily_DoesNotThrow(string generic)
        {
            // Exercises the fontconfig generic-alias-matching P/Invoke path directly, regardless of the
            // host OS the tests run on - falls back to null (not an exception) when libfontconfig.so.1
            // isn't available (e.g. on Windows/macOS CI runners).
            var resolved = LinuxSystemFontResolver.ResolveGenericFamily(generic);

            // No assertion on the value itself (host-dependent) - only that this never throws.
            _ = resolved;
        }

        [Fact]
        public void Resolve_OnLinux_ReturnsAtLeastOneFont()
        {
            // No-ops (rather than skipping - matching this test project's existing convention, e.g.
            // GenericFontFamilyIntegrationTests's Windows-only assertion) on any non-Linux host, since a
            // real font-discovery result is only meaningful to assert on the actual target platform.
            if (!OperatingSystem.IsLinux() || OperatingSystem.IsAndroid()) return;

            var fonts = LinuxSystemFontResolver.Resolve();

            Assert.NotEmpty(fonts);
        }

        [Theory]
        [InlineData("serif")]
        [InlineData("sans-serif")]
        [InlineData("monospace")]
        [InlineData("cursive")]
        [InlineData("fantasy")]
        public void ResolveGenericFamily_OnLinux_ReturnsARealInstalledFamilyName(string generic)
        {
            // Real Linux CI/dev machines ship fontconfig with at least a serif/sans-serif/monospace
            // alias configured (that's the whole point of fontconfig's default config) - confirms the
            // P/Invoke path genuinely resolves a family, not just that it doesn't throw.
            if (!OperatingSystem.IsLinux() || OperatingSystem.IsAndroid()) return;

            var resolved = LinuxSystemFontResolver.ResolveGenericFamily(generic);

            Assert.False(string.IsNullOrWhiteSpace(resolved));
        }
    }
}
