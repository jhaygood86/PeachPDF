using PeachPDF.PdfSharpCore.Utils;
using PeachPDF.Tests.TestSupport;
using System.IO;

using PeachPDF.Fonts;

namespace PeachPDF.Tests.PdfSharpCoreTests
{
    /// <summary>
    /// Tests for <see cref="FontResolver"/>'s CSS Fonts Level 4 §5.2 nearest-stretch matching, narrowed
    /// within the requested italic-ness before nearest-weight matching (see
    /// <see cref="FontResolverWeightMatchingTests"/> for the weight-only equivalents). Uses two real, but
    /// different-family, bundled fonts (TTF + OTF) registered under one shared CSS family name at explicit
    /// stretch overrides, so the two candidate faces are always distinguishable by their own real
    /// (differing) internal font names.
    /// </summary>
    public class FontResolverStretchMatchingTests
    {
        private const string Family = "TestStretchFamily";

        private static (FontResolver Resolver, string NarrowerFaceName, string WiderFaceName) BuildTwoStretchFamily(int narrowerStretch, int widerStretch)
        {
            var resolver = new FontResolver();

            using (var ttf = File.OpenRead(BundledFonts.Ttf))
                resolver.AddFont(ttf, Family, weightOverride: 400, isItalicOverride: false, stretchOverride: narrowerStretch);
            using (var otf = File.OpenRead(BundledFonts.Otf))
                resolver.AddFont(otf, Family, weightOverride: 400, isItalicOverride: false, stretchOverride: widerStretch);

            var narrowerFaceName = TtfFontDescription.LoadDescription(BundledFonts.Ttf).FontNameInvariantCulture;
            var widerFaceName = TtfFontDescription.LoadDescription(BundledFonts.Otf).FontNameInvariantCulture;

            return (resolver, narrowerFaceName, widerFaceName);
        }

        [Fact]
        public void ExactStretchMatch_ReturnsThatFace()
        {
            var (resolver, narrowerFaceName, widerFaceName) = BuildTwoStretchFamily(3, 7);

            Assert.Equal(narrowerFaceName, resolver.ResolveTypeface(Family, weight: 400, isItalic: false, stretch: 3).FaceName);
            Assert.Equal(widerFaceName, resolver.ResolveTypeface(Family, weight: 400, isItalic: false, stretch: 7).FaceName);
        }

        [Fact]
        public void RequestAtOrNarrowerThanNormal_SearchesNarrowerFirst()
        {
            // Target <= normal (5) searches narrower (< target) first, then wider - with 3 and 9
            // registered, a request for 4 must prefer 3 (narrower), not 9 (wider).
            var (resolver, narrowerFaceName, widerFaceName) = BuildTwoStretchFamily(3, 9);

            var info = resolver.ResolveTypeface(Family, weight: 400, isItalic: false, stretch: 4);

            Assert.Equal(narrowerFaceName, info.FaceName);
            Assert.NotEqual(widerFaceName, info.FaceName);
        }

        [Fact]
        public void RequestWiderThanNormal_SearchesWiderFirst()
        {
            // Target > normal (5) searches wider (> target) first, then narrower - with 3 and 9
            // registered, a request for 6 must prefer 9 (wider), not 3 (narrower).
            var (resolver, narrowerFaceName, widerFaceName) = BuildTwoStretchFamily(3, 9);

            var info = resolver.ResolveTypeface(Family, weight: 400, isItalic: false, stretch: 6);

            Assert.Equal(widerFaceName, info.FaceName);
            Assert.NotEqual(narrowerFaceName, info.FaceName);
        }

        [Fact]
        public void NoStretchSpecified_MatchesNormalStretchFace()
        {
            var resolver = new FontResolver();
            using var ttf = File.OpenRead(BundledFonts.Ttf);
            resolver.AddFont(ttf, Family, weightOverride: 400, isItalicOverride: false, stretchOverride: 5);

            var normalFaceName = TtfFontDescription.LoadDescription(BundledFonts.Ttf).FontNameInvariantCulture;

            // The 3-arg overload (no stretch) must behave exactly like requesting stretch: 5 (normal).
            var info = resolver.ResolveTypeface(Family, weight: 400, isItalic: false);

            Assert.Equal(normalFaceName, info.FaceName);
        }

        [Fact]
        public void StretchNarrowingHappensBeforeWeightNarrowing()
        {
            // Family has: (weight 400, stretch 3) and (weight 700, stretch 7). A request for
            // (weight 400, stretch 7) must narrow to stretch 7 first (picking the 700-weight face,
            // the only one at that stretch) rather than picking the nearer-weight 400 face at the
            // wrong stretch.
            var resolver = new FontResolver();

            using (var ttf = File.OpenRead(BundledFonts.Ttf))
                resolver.AddFont(ttf, Family, weightOverride: 400, isItalicOverride: false, stretchOverride: 3);
            using (var otf = File.OpenRead(BundledFonts.Otf))
                resolver.AddFont(otf, Family, weightOverride: 700, isItalicOverride: false, stretchOverride: 7);

            var wideFaceName = TtfFontDescription.LoadDescription(BundledFonts.Otf).FontNameInvariantCulture;

            var info = resolver.ResolveTypeface(Family, weight: 400, isItalic: false, stretch: 7);

            Assert.Equal(wideFaceName, info.FaceName);
        }
    }
}
