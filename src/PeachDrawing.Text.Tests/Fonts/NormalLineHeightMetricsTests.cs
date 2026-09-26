using PeachDrawing.Text.Internal.Fonts;
using PeachDrawing.Text.Internal.Fonts.OpenType;
using PeachPDF.Tests.TestSupport;
using System.IO;
using Xunit;

namespace PeachDrawing.Text.Tests.Fonts
{
    /// <summary>
    /// <c>NormalLineHeightAscent</c>/<c>NormalLineHeightDescent</c>/
    /// <c>NormalLineHeightGap</c> selection logic (issue #956): the raw <c>hhea</c>
    /// triple, unless <c>OS/2.fsSelection</c>'s <c>USE_TYPO_METRICS</c> bit (0x80) is set and the OS/2 typo
    /// fields aren't all-zero, in which case the OS/2 typo triple is used instead - deliberately without the
    /// <c>usWinAscent</c>/<c>usWinDescent</c> substitution the sibling <c>Ascender</c>/
    /// <c>Descender</c>/<c>LineSpacing</c> block uses (that's the
    /// WPF-derived, PDF-metrics/baseline-positioning triple; this is the separate, browser-matching one).
    ///
    /// Loads the bundled TrueType font through <see cref="FontFileData.CreateCompiledFont"/> rather than
    /// <see cref="FontFileData.GetOrCreateFrom"/> - the latter caches by content checksum in the process-wide
    /// <c>FontFactory</c> (see this repo's own CLAUDE.md warning about exactly this), and every test here
    /// mutates the parsed face's <c>OS/2</c> fields directly, which would corrupt that shared cached
    /// instance for every other test loading the same file. <c>CreateCompiledFont</c> returns a fresh,
    /// uncached <see cref="FontFileData"/> each call, so each test's face is exclusively its own.
    /// </summary>
    public class NormalLineHeightMetricsTests
    {
        private static OpenTypeFontface FreshUncachedFace()
        {
            var bytes = File.ReadAllBytes(BundledFonts.Ttf);
            return new OpenTypeFontface(FontFileData.CreateCompiledFont(bytes));
        }

        private static OpenTypeDescriptor Descriptor(OpenTypeFontface face) =>
            new("normal-line-height-test", "normal-line-height-test", face);

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
    }
}
