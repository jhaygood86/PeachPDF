using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Utils;
using System;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end regression test for the platform-aware generic-family mapping
    /// (<see cref="PeachPDF.Html.Core.Utils.GenericFontFamilyResolver"/>) actually wired into
    /// <see cref="PdfSharpAdapter"/>'s generic-family mappings, not just the pure resolver function.
    /// Windows is the only platform this test can assert a specific resolved family on regardless of
    /// which CI/dev machine runs it, since Consolas is a font every real Windows installation ships -
    /// no-ops (rather than skipping, matching this test project's existing convention for host-dependent
    /// behavior - see e.g. LinuxSystemFontResolverTests) on any other platform.
    /// </summary>
    public class GenericFontFamilyIntegrationTests
    {
        [Fact]
        public void Monospace_OnWindows_ResolvesToConsolas_NotCourierNew()
        {
            if (!OperatingSystem.IsWindows()) return;

            var adapter = new PdfSharpAdapter();
            var font = adapter.GetFont("monospace", 12, RFontStyle.Regular) as FontAdapter;

            Assert.NotNull(font);
            Assert.Equal("Consolas", font!.Font.Name);
        }

        [Fact]
        public void SystemUi_WhenFontconfigCannotAnswer_FallsBackToTheDefaultFont()
        {
            // Off Linux, and on a Linux host with no libfontconfig.so.1 (or a resolution failure —
            // LinuxSystemFontResolver catches and returns null), there is no fontconfig answer at all.
            Assert.Equal(DefaultFontResolver.DefaultFont,
                PdfSharpAdapter.ResolveSystemUiFamily(null, _ => true));
        }

        [Fact]
        public void SystemUi_WhenFontconfigNamesAFamilyThatIsNotInstalled_FallsBackToTheDefaultFont()
        {
            // fontconfig can name a family this process cannot actually load. Verified with a
            // synthetic name because on any real machine fontconfig's own answer IS installed, so the
            // branch would never run and the assertion would hold whether or not the code did
            // anything — the same reason DefaultFontFallbackTests uses a synthetic default.
            Assert.Equal(DefaultFontResolver.DefaultFont,
                PdfSharpAdapter.ResolveSystemUiFamily("PeachPDF Test Family That Is Not Installed", _ => false));
        }

        [Fact]
        public void SystemUi_WhenFontconfigNamesAnInstalledFamily_UsesIt()
        {
            // The contrast case: without it, the two above also pass if the mapping always fell back.
            Assert.Equal("FreeSans",
                PdfSharpAdapter.ResolveSystemUiFamily("FreeSans", _ => true));
        }

        [Theory]
        [InlineData("serif")]
        [InlineData("sans-serif")]
        [InlineData("monospace")]
        [InlineData("system-ui")]   // resolved through fontconfig like the three above
        public void Generic_OnLinux_ResolvesToARealInstalledFontconfigFamily(string generic)
        {
            // Linux delegates to fontconfig at PdfSharpAdapter construction time rather than a hardcoded
            // table - confirms the resolved mapping is both non-trivial (differs from the bare generic
            // name FontsHandler would otherwise treat as a literal, almost-certainly-uninstalled family)
            // and actually installed (IsFontExists), on a real Linux CI/dev machine.
            if (!OperatingSystem.IsLinux() || OperatingSystem.IsAndroid()) return;

            var adapter = new PdfSharpAdapter();

            Assert.True(adapter.IsFontExists(generic));
        }
    }
}
