using PeachPDF.PdfSharpCore.Utils;
using PeachPDF.Tests.TestSupport;
using System.IO;

namespace PeachPDF.Tests.PdfSharpCoreTests
{
    /// <summary>
    /// Tests for <see cref="FontResolver"/>'s CSS Fonts Level 4 §5.2 nearest-weight matching and the
    /// faux-bold/italic synthesis decision that rides along with it. Uses two real, but different-family,
    /// bundled fonts (TTF + OTF - see <see cref="BundledFonts"/>'s own doc comment on why same-family
    /// pairs are avoided in these tests) registered under one shared CSS family name at explicit
    /// weight/style overrides, so the two candidate faces are always distinguishable by their own real
    /// (differing) internal font names.
    /// </summary>
    public class FontResolverWeightMatchingTests
    {
        private const string Family = "TestWeightFamily";

        private static (FontResolver Resolver, string LighterFaceName, string HeavierFaceName) BuildTwoWeightFamily(int lighterWeight, int heavierWeight)
        {
            var resolver = new FontResolver();

            using (var ttf = File.OpenRead(BundledFonts.Ttf))
                resolver.AddFont(ttf, Family, lighterWeight, isItalicOverride: false);
            using (var otf = File.OpenRead(BundledFonts.Otf))
                resolver.AddFont(otf, Family, heavierWeight, isItalicOverride: false);

            var lighterFaceName = TtfFontDescription.LoadDescription(BundledFonts.Ttf).FontNameInvariantCulture;
            var heavierFaceName = TtfFontDescription.LoadDescription(BundledFonts.Otf).FontNameInvariantCulture;

            return (resolver, lighterFaceName, heavierFaceName);
        }

        [Fact]
        public void ExactWeightMatch_ReturnsThatFace()
        {
            var (resolver, lighterFaceName, heavierFaceName) = BuildTwoWeightFamily(400, 700);

            Assert.Equal(lighterFaceName, resolver.ResolveTypeface(Family, weight: 400, isItalic: false).FaceName);
            Assert.Equal(heavierFaceName, resolver.ResolveTypeface(Family, weight: 700, isItalic: false).FaceName);
        }

        [Fact]
        public void RequestAbove500_WithOnly400And700Registered_PicksHigher700()
        {
            // Target > 500 searches upward first (CSS Fonts L4 §5.2) - 700 is the nearest weight above 600.
            var (resolver, _, heavierFaceName) = BuildTwoWeightFamily(400, 700);

            var info = resolver.ResolveTypeface(Family, weight: 600, isItalic: false);

            Assert.Equal(heavierFaceName, info.FaceName);
        }

        [Fact]
        public void RequestBelow400_WithOnly400And700Registered_PicksLower400()
        {
            // Target < 400 searches downward first, then upward - nothing below 400 is registered, so
            // it falls through to the closest weight above (400).
            var (resolver, lighterFaceName, _) = BuildTwoWeightFamily(400, 700);

            var info = resolver.ResolveTypeface(Family, weight: 300, isItalic: false);

            Assert.Equal(lighterFaceName, info.FaceName);
        }

        [Fact]
        public void RequestInFourHundredToFiveHundredRange_SearchesUpwardTo500First()
        {
            // A target in [400,500] searches ascending to 500 inclusive first - 500 itself isn't
            // registered here, so with only 300/700 available, 700 (>500, "above 500 ascending" phase)
            // must NOT be preferred over correctly falling back to the below-target search for 300.
            var (resolver, lighterFaceName, heavierFaceName) = BuildTwoWeightFamily(300, 700);

            // Target 450: ascending-to-500 finds nothing (neither 300 nor 700 is in [450,500]), then
            // below-target descending finds 300, before the above-500 phase would reach 700.
            var info = resolver.ResolveTypeface(Family, weight: 450, isItalic: false);

            Assert.Equal(lighterFaceName, info.FaceName);
            Assert.NotEqual(heavierFaceName, info.FaceName);
        }

        [Fact]
        public void BoldRequest_OnlyLightFaceRegistered_TriggersBoldSynthesis()
        {
            var resolver = new FontResolver();
            using var ttf = File.OpenRead(BundledFonts.Ttf);
            resolver.AddFont(ttf, Family, weightOverride: 400, isItalicOverride: false);

            var info = resolver.ResolveTypeface(Family, weight: 700, isItalic: false);

            Assert.True(info.MustSimulateBold);
        }

        [Fact]
        public void BoldRequest_RealBoldishFaceRegistered_DoesNotTriggerSynthesis()
        {
            // Requesting 600 when a 700 face is available is a genuine, close-enough match - the UA
            // should use the real face, not synthesize on top of it.
            var (resolver, _, _) = BuildTwoWeightFamily(400, 700);

            var info = resolver.ResolveTypeface(Family, weight: 600, isItalic: false);

            Assert.False(info.MustSimulateBold);
        }

        [Fact]
        public void ItalicRequest_NoItalicFaceRegistered_TriggersItalicSynthesis()
        {
            var resolver = new FontResolver();
            using var ttf = File.OpenRead(BundledFonts.Ttf);
            resolver.AddFont(ttf, Family, weightOverride: 400, isItalicOverride: false);

            var info = resolver.ResolveTypeface(Family, weight: 400, isItalic: true);

            Assert.True(info.MustSimulateItalic);
        }

        [Fact]
        public void ItalicRequest_RealItalicFaceRegistered_DoesNotTriggerSynthesis()
        {
            var resolver = new FontResolver();
            using var ttf = File.OpenRead(BundledFonts.Ttf);
            resolver.AddFont(ttf, Family, weightOverride: 400, isItalicOverride: true);

            var info = resolver.ResolveTypeface(Family, weight: 400, isItalic: true);

            Assert.False(info.MustSimulateItalic);
        }

        [Fact]
        public void BoldBoolOverload_DelegatesTo700Or400()
        {
            var (resolver, lighterFaceName, heavierFaceName) = BuildTwoWeightFamily(400, 700);

            Assert.Equal(lighterFaceName, resolver.ResolveTypeface(Family, isBold: false, isItalic: false).FaceName);
            Assert.Equal(heavierFaceName, resolver.ResolveTypeface(Family, isBold: true, isItalic: false).FaceName);
        }
    }
}
