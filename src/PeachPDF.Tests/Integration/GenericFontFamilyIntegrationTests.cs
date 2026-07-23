using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using System;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end regression test for the platform-aware generic-family mapping
    /// (<see cref="PeachPDF.Html.Core.Utils.GenericFontFamilyResolver"/>) actually wired into
    /// <see cref="PdfSharpAdapter"/>'s generic-family mappings, not just the pure resolver function.
    /// Windows is the only platform this test can assert a specific resolved family on regardless of
    /// which CI/dev machine runs it, since Consolas is a font every real Windows installation ships -
    /// dynamically skipped (via <c>Assert.Skip</c>, matching the convention established by
    /// <see cref="PeachPDF.Tests.Network.MimeTypeResolverTests"/>) on any other platform, so a
    /// host-inapplicable run reports as skipped rather than silently as passed.
    /// </summary>
    public class GenericFontFamilyIntegrationTests
    {
        [Fact]
        public void Monospace_OnWindows_ResolvesToConsolas_NotCourierNew()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Skip("Windows-only: Consolas is only guaranteed present on a real Windows installation.");
                return;
            }

            var adapter = new PdfSharpAdapter();
            var font = adapter.GetFont("monospace", 12, RFontStyle.Regular) as FontAdapter;

            Assert.NotNull(font);
            Assert.Equal("Consolas", font!.Font.Name);
        }

        [Theory]
        [InlineData("serif")]
        [InlineData("sans-serif")]
        [InlineData("monospace")]
        public void Generic_OnLinux_ResolvesToARealInstalledFontconfigFamily(string generic)
        {
            // Linux delegates to fontconfig at PdfSharpAdapter construction time rather than a hardcoded
            // table - confirms the resolved mapping is both non-trivial (differs from the bare generic
            // name FontsHandler would otherwise treat as a literal, almost-certainly-uninstalled family)
            // and actually installed (IsFontExists), on a real Linux CI/dev machine.
            if (!OperatingSystem.IsLinux() || OperatingSystem.IsAndroid())
            {
                Assert.Skip("Linux-only: exercises fontconfig-backed generic-family resolution.");
                return;
            }

            var adapter = new PdfSharpAdapter();

            Assert.True(adapter.IsFontExists(generic));
        }
    }
}
