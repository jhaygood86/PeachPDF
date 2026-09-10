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
        [InlineData("system-ui")]   // resolved through fontconfig like the five above
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
        [InlineData("system-ui")]   // resolved through fontconfig like the five above
        public void ResolveGenericFamily_OnLinux_ReturnsARealInstalledFamilyName(string generic)
        {
            // Real Linux CI/dev machines ship fontconfig with at least a serif/sans-serif/monospace
            // alias configured (that's the whole point of fontconfig's default config) - confirms the
            // P/Invoke path genuinely resolves a family, not just that it doesn't throw.
            if (!OperatingSystem.IsLinux() || OperatingSystem.IsAndroid()) return;

            var resolved = LinuxSystemFontResolver.ResolveGenericFamily(generic);

            Assert.False(string.IsNullOrWhiteSpace(resolved));
        }

        /// <summary>
        /// The fontconfig round trip happens once per family for the life of the process, not once per
        /// caller.
        /// </summary>
        /// <remarks>
        /// Asserted by reference, which is what makes it a real check: every uncached resolution
        /// marshals a fresh <see cref="string"/> out of native memory, so two uncached calls can be
        /// equal but can never be the same instance. <c>Assert.Equal</c> here would pass against
        /// unfixed code and prove nothing.
        /// <para>
        /// It matters because <c>PdfGenerator</c> holds its <c>PdfSharpAdapter</c> as an instance
        /// field and that constructor resolves every generic family plus <c>system-ui</c>, so a caller
        /// rendering N documents used to make 7N identical native round trips.
        /// </para>
        /// </remarks>
        [Fact]
        public void ResolveGenericFamily_OnLinux_AsksFontconfigOncePerFamily()
        {
            if (!OperatingSystem.IsLinux() || OperatingSystem.IsAndroid()) return;

            var first = LinuxSystemFontResolver.ResolveGenericFamily("sans-serif");

            // Guards the guard: on a Linux host with no usable fontconfig the method returns null for
            // every call, and Assert.Same(null, null) would pass without exercising anything. The
            // sibling theory above already asserts this is non-null on a real Linux host.
            Assert.NotNull(first);

            var second = LinuxSystemFontResolver.ResolveGenericFamily("sans-serif");

            Assert.Same(first, second);
        }
    }
}
