using System.IO;
using PeachPDF.Adapters;
using PeachPDF.Fonts;
using PeachPDF.Fonts.OpenType;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.Tests.TestSupport;
using Xunit;

namespace PeachPDF.Tests.PdfSharpCoreTests.Fonts
{
    /// <summary>Resolves every request to the bundled TrueType fixture, for a test that needs a real,
    /// deterministic <see cref="XFont"/> without depending on which fonts happen to be installed on the
    /// machine running the test (unlike the system-font-dependent resolvers used elsewhere in this
    /// directory, e.g. <c>SmokeTests.cs</c>'s "Times New Roman"/"Arial").</summary>
    file sealed class BundledTtfResolver : IFontResolver
    {
        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) => new("bundled-ttf");
        public FontResolverInfo ResolveTypeface(string familyName, int weight, bool isItalic) => new("bundled-ttf");
        public FontResolverInfo ResolveTypeface(string familyName, int weight, bool isItalic, int stretch) => new("bundled-ttf");
        public byte[] GetFont(string fontFaceName) => File.ReadAllBytes(BundledFonts.Ttf);
    }

    /// <summary>
    /// <see cref="FontDescriptor.NormalLineHeightAscent"/>/<see cref="FontDescriptor.NormalLineHeightDescent"/>/
    /// <see cref="FontDescriptor.NormalLineHeightGap"/> selection logic (issue #956): the raw <c>hhea</c>
    /// triple, unless <c>OS/2.fsSelection</c>'s <c>USE_TYPO_METRICS</c> bit (0x80) is set and the OS/2 typo
    /// fields aren't all-zero, in which case the OS/2 typo triple is used instead - deliberately without the
    /// <c>usWinAscent</c>/<c>usWinDescent</c> substitution the sibling <see cref="FontDescriptor.Ascender"/>/
    /// <see cref="FontDescriptor.Descender"/>/<see cref="FontDescriptor.LineSpacing"/> block uses (that's the
    /// WPF-derived, PDF-metrics/baseline-positioning triple; this is the separate, browser-matching one).
    ///
    /// Loads the bundled TrueType font through <see cref="XFontSource.CreateCompiledFont"/> rather than
    /// <see cref="XFontSource.GetOrCreateFrom"/> - the latter caches by content checksum in the process-wide
    /// <c>FontFactory</c> (see this repo's own CLAUDE.md warning about exactly this), and every test here
    /// mutates the parsed face's <c>OS/2</c> fields directly, which would corrupt that shared cached
    /// instance for every other test loading the same file. <c>CreateCompiledFont</c> returns a fresh,
    /// uncached <see cref="XFontSource"/> each call, so each test's face is exclusively its own.
    /// </summary>
    public class NormalLineHeightMetricsTests
    {
        private static OpenTypeFontface FreshUncachedFace()
        {
            var bytes = File.ReadAllBytes(BundledFonts.Ttf);
            return new OpenTypeFontface(XFontSource.CreateCompiledFont(bytes));
        }

        private static OpenTypeDescriptor Descriptor(OpenTypeFontface face) =>
            new("normal-line-height-test", "normal-line-height-test", XFontStyle.Regular, face,
                new XPdfFontOptions(PdfFontEncoding.Unicode));

        [Fact]
        public void UseTypoMetricsNotSet_UsesRawHheaTriple()
        {
            var face = FreshUncachedFace();
            Assert.Equal(0, face.os2.fsSelection & 0x80); // SourceSans3-Regular doesn't set USE_TYPO_METRICS

            var descriptor = Descriptor(face);

            Assert.Equal(face.hhea.ascender, descriptor.NormalLineHeightAscent);
            Assert.Equal(System.Math.Abs(face.hhea.descender), descriptor.NormalLineHeightDescent);
            Assert.Equal(0, descriptor.NormalLineHeightGap);
        }

        [Fact]
        public void UseTypoMetricsSet_UsesOs2TypoTriple_NotWinMetrics()
        {
            var face = FreshUncachedFace();
            face.os2.fsSelection |= 0x80;
            face.os2.sTypoAscender = 900;
            face.os2.sTypoDescender = -200;
            face.os2.sTypoLineGap = 50;

            var descriptor = Descriptor(face);

            Assert.Equal(900, descriptor.NormalLineHeightAscent);
            Assert.Equal(200, descriptor.NormalLineHeightDescent);
            Assert.Equal(50, descriptor.NormalLineHeightGap);
            // Not the OS/2 win metrics (the legacy substitution Ascender/Descender/LineSpacing makes) -
            // proves this is a genuinely separate accessor, not a relabeling of the existing one.
            Assert.NotEqual(face.os2.usWinAscent, descriptor.NormalLineHeightAscent);
        }

        [Fact]
        public void UseTypoMetricsSetButOs2TypoFieldsAllZero_FallsBackToHhea()
        {
            // Mirrors the shared `os2SeemsToBeEmpty` gate the existing Ascender/Descender/LineSpacing
            // block already relies on: a font can set the USE_TYPO_METRICS bit while leaving the typo
            // fields themselves unpopulated (spec-invalid but real-world fonts do this), so both blocks
            // must fall back to hhea rather than resolving to a bogus all-zero line height.
            var face = FreshUncachedFace();
            face.os2.fsSelection |= 0x80;
            face.os2.sTypoAscender = 0;
            face.os2.sTypoDescender = 0;
            face.os2.sTypoLineGap = 0;

            var descriptor = Descriptor(face);

            Assert.Equal(face.hhea.ascender, descriptor.NormalLineHeightAscent);
            Assert.Equal(System.Math.Abs(face.hhea.descender), descriptor.NormalLineHeightDescent);
        }

        // Regression guard for a bug caught in review: FontAdapter.NormalLineHeight originally rounded
        // each ascent/descent/gap component to a whole CSS pixel *after* multiplying by PixelsPerPoint,
        // instead of before - correct only by coincidence at the default PixelsPerPoint=1 every other test
        // in this repo uses, since Length.PointsPerPx is a fixed pt-per-CSS-px ratio, not a
        // pt-per-internal-layout-unit one. Two FontAdapters wrapping the *same* XFont (so both share
        // identical ascent/descent/gap component values in real-point space, unaffected by
        // PixelsPerPoint - see FontAdapter's own remarks on why XFont.Size is a true, unscaled point size)
        // isolate the fix precisely: with each component correctly rounded once, in real-point space,
        // before the single final multiply, NormalLineHeight must scale by *exactly* PixelsPerPoint. The
        // bug this guards against instead re-rounds at a different (wrong) granularity per PixelsPerPoint
        // value, so the ratio would generally miss 2.0 by a fraction of a CSS pixel.
        [Fact]
        public void NormalLineHeight_ScalesExactlyLinearlyWithPixelsPerPoint()
        {
            var font = new XFont("bundled", 20, XFontStyle.Regular, new XPdfFontOptions(PdfFontEncoding.Unicode),
                400, 5, null, new BundledTtfResolver());

            var unscaled = new FontAdapter(font, pixelsPerPoint: 1.0);
            var scaled = new FontAdapter(font, pixelsPerPoint: 2.0);

            Assert.Equal(2.0 * unscaled.NormalLineHeight, scaled.NormalLineHeight, 9);
        }
    }
}
