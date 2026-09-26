using PeachDrawing.Text;
using PeachDrawing.Text.Internal.Fonts;
using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using System;
using System.Threading.Tasks;

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
                GenericFamilyTable.Resolve(GenericFamily.SystemUi, null, false, false, false, _ => true) ?? DefaultFontResolver.DefaultFont);
        }

        [Fact]
        public void SystemUi_WhenFontconfigNamesAFamilyThatIsNotInstalled_FallsBackToTheDefaultFont()
        {
            // fontconfig can name a family this process cannot actually load. Verified with a
            // synthetic name because on any real machine fontconfig's own answer IS installed, so the
            // branch would never run and the assertion would hold whether or not the code did
            // anything — the same reason DefaultFontFallbackTests uses a synthetic default.
            Assert.Equal(DefaultFontResolver.DefaultFont,
                GenericFamilyTable.Resolve(GenericFamily.SystemUi, "PeachPDF Test Family That Is Not Installed", false, false, false, _ => false) ?? DefaultFontResolver.DefaultFont);
        }

        [Fact]
        public void SystemUi_WhenFontconfigNamesAnInstalledFamily_UsesIt()
        {
            // The contrast case: without it, the two above also pass if the mapping always fell back.
            Assert.Equal("FreeSans",
                GenericFamilyTable.Resolve(GenericFamily.SystemUi, "FreeSans", false, false, false, _ => true) ?? DefaultFontResolver.DefaultFont);
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

        [Fact]
        public void Math_OnWindows_ResolvesToCambriaMath_WhenNoLatinModernMathIsInstalled()
        {
            // Cambria Math ships only inside cambria.ttc, so this holds only because font discovery reads
            // font collections. Skipped (returns) on any host that isn't stock-Windows-with-Cambria.
            var cambria = System.IO.Path.Combine(Environment.ExpandEnvironmentVariables(@"%SystemRoot%\Fonts"), "cambria.ttc");
            if (!OperatingSystem.IsWindows() || !System.IO.File.Exists(cambria))
                return;

            var adapter = new PdfSharpAdapter();
            if (adapter.IsFontExists("Latin Modern Math"))
                return;

            var font = adapter.GetFont("math", 12, RFontStyle.Regular) as FontAdapter;

            Assert.NotNull(font);
            Assert.Equal("Cambria Math", font!.Font.Name);
        }

        [Fact]
        public void Math_IsAlwaysAnInstalledFamily_OnEveryPlatform()
        {
            // Whichever math font the host has, or the platform default when it has none, the generic must
            // never be left as an unknown family name.
            var adapter = new PdfSharpAdapter();

            Assert.True(adapter.IsFontExists("math"));
        }

        private const string MathFamily = "TestMathGeneric";
        private const string MathFontName = "STIX Two Math"; // the name the bundled font itself carries

        /// <summary>
        /// Lays <c>&lt;p&gt;text&lt;/p&gt;&lt;math&gt;&lt;mi&gt;x&lt;/mi&gt;&lt;/math&gt;</c> out with the
        /// <c>math</c> generic remapped to a bundled real math font, and returns the family each of the two
        /// boxes actually resolved to.
        /// </summary>
        private static async Task<(string Paragraph, string Math)> ResolvedFamiliesAsync(string css)
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            await BundledFonts.RegisterFont(adapter, BundledFonts.Math, MathFamily);
            adapter.AddFontFamilyMapping("math", MathFamily);

            var container = new HtmlContainerInt(adapter);
            await container.SetHtml($"<html><head><style>{css}</style></head><body><p>text</p><math><mi>x</mi></math></body></html>", null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            var paragraph = FindByTag(container.Root!, "p")!;
            var math = FindByTag(container.Root!, "math")!;
            return (((FontAdapter)paragraph.ActualFont).Font.Name, ((FontAdapter)math.ActualFont).Font.Name);
        }

        private static CssBox? FindByTag(CssBox box, string tag)
        {
            if (box.HtmlTag?.Name.Equals(tag, StringComparison.OrdinalIgnoreCase) == true) return box;
            foreach (var child in box.Boxes)
            {
                var found = FindByTag(child, tag);
                if (found != null) return found;
            }
            return null;
        }

        [Fact]
        public async Task MathElement_UsesTheMathGeneric_ThroughTheUserAgentStylesheet()
        {
            // No author font-family anywhere: the only thing that can select the math font for <math> is
            // the UA rule `math { font-family: math }` resolving through the generic's mapping.
            var (paragraph, math) = await ResolvedFamiliesAsync("");

            Assert.NotEqual(MathFontName, paragraph);
            Assert.Equal(MathFontName, math);
        }

        [Fact]
        public async Task MathElement_AuthorFontFamilyOverridesTheUserAgentRule()
        {
            var (_, math) = await ResolvedFamiliesAsync("math { font-family: serif }");

            Assert.NotEqual(MathFontName, math);
        }

        [Fact]
        public async Task MathGeneric_OnAnyOtherElement_ResolvesToTheMathMapping()
        {
            var (paragraph, _) = await ResolvedFamiliesAsync("p { font-family: math }");

            Assert.Equal(MathFontName, paragraph);
        }
    }
}
