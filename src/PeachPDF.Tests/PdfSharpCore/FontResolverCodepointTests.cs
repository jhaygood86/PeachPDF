using PeachPDF;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Fonts.OpenType;
using PeachPDF.PdfSharpCore.Utils;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.IO;
using System.Text;

using PeachPDF.Fonts;

namespace PeachPDF.Tests.PdfSharpCoreTests
{
    public class FontResolverCodepointTests
    {
        private static List<RuneRange> R(int start, int end) => [new RuneRange(new Rune(start), new Rune(end))];

        [Fact]
        public void ResolveTypeface_WithCodepoint_FiltersToTheCoveringFace()
        {
            var upperName = TtfFontDescription.LoadDescription(BundledFonts.Ttf).FontNameInvariantCulture; // Source Sans 3
            var lowerName = TtfFontDescription.LoadDescription(BundledFonts.Otf).FontNameInvariantCulture; // Source Code Pro

            var resolver = new FontResolver { NullIfFontNotFound = true };
            resolver.AddFont(File.OpenRead(BundledFonts.Ttf), "Combo", null, null, null, R(0x41, 0x5A)); // A-Z
            resolver.AddFont(File.OpenRead(BundledFonts.Otf), "Combo", null, null, null, R(0x61, 0x7A)); // a-z

            Assert.True(resolver.HasExplicitRanges("Combo"));

            // Each codepoint resolves to the face whose declared unicode-range covers it - even though both
            // fonts physically contain both cases.
            Assert.Equal(upperName, resolver.ResolveTypeface("Combo", 400, false, 5, new Rune('A')).FaceName);
            Assert.Equal(upperName, resolver.ResolveTypeface("Combo", 400, false, 5, new Rune('Z')).FaceName);
            Assert.Equal(lowerName, resolver.ResolveTypeface("Combo", 400, false, 5, new Rune('a')).FaceName);

            // A codepoint covered by no face's range reports "no covering face" so the caller can fall back.
            Assert.Null(resolver.ResolveTypeface("Combo", 400, false, 5, new Rune('0')));
        }

        [Fact]
        public void ResolveTypeface_CodepointLess_Ignores_Ranges_And_Never_Returns_Null()
        {
            // The existing (codepoint-less) overloads must keep working regardless of ranges: no coverage
            // filter, always a result (box/metrics resolution).
            var resolver = new FontResolver { NullIfFontNotFound = true };
            resolver.AddFont(File.OpenRead(BundledFonts.Ttf), "Ranged", null, null, null, R(0x41, 0x5A));

            Assert.NotNull(resolver.ResolveTypeface("Ranged", 400, false));
            Assert.NotNull(resolver.ResolveTypeface("Ranged", 400, false, 5));
        }

        [Fact]
        public void CMapCoverage_Extract_ReportsCoveredCodepoints_ForRangelessFont()
        {
            var fontSource = XFontSource.GetOrCreateFrom(File.ReadAllBytes(BundledFonts.Ttf));
            var coverage = CMapCoverage.Extract(fontSource.Fontface.cmap.cmap4);

            Assert.NotEmpty(coverage);
            Assert.True(CMapCoverage.Contains(coverage, new Rune('A')));
            Assert.True(CMapCoverage.Contains(coverage, new Rune('z')));

            var otf = XFontSource.GetOrCreateFrom(File.ReadAllBytes(BundledFonts.Otf));
            var otfCoverage = CMapCoverage.Extract(otf.Fontface.cmap.cmap4);
            Assert.True(CMapCoverage.Contains(otfCoverage, new Rune('a')));
        }
    }
}
