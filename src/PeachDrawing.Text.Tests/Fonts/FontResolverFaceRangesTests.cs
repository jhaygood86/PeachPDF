using PeachDrawing.Text.Internal.Fonts;
using PeachPDF.Tests.TestSupport;
using System.IO;

namespace PeachDrawing.Text.Tests.Fonts
{
    /// <summary>
    /// <see cref="FontResolver"/>'s CSS Fonts 4 section 5.2 matching when faces declare ranges: two bundled fonts, told apart by their own
    /// internal names, are registered under one family with ranges of weights and widths, and a request lands on the face whose
    /// range covers it or, when none does, the one whose range is nearest by the specification's search order.
    /// </summary>
    public class FontResolverFaceRangesTests
    {
        private const string Family = "TestRangesFamily";

        private static string FaceNameOf(string path) => TtfFontDescription.LoadDescription(path).FontNameInvariantCulture;

        private static FontResolver Build(FontResolver.DeclaredFace first, FontResolver.DeclaredFace second)
        {
            var resolver = new FontResolver();
            using (var ttf = File.OpenRead(BundledFonts.Ttf))
                resolver.AddFont(ttf, Family, first, null);
            using (var otf = File.OpenRead(BundledFonts.Otf))
                resolver.AddFont(otf, Family, second, null);
            return resolver;
        }

        private static FontResolver.DeclaredFace Weights(double minimum, double maximum) =>
            new(new AxisRange(minimum, maximum), false, null, null);

        private static FontResolver.DeclaredFace Widths(double minimum, double maximum) =>
            new(new AxisRange(400), false, new AxisRange(minimum, maximum), null);

        private static FaceRequest Request(double weight, double width = 100, bool italic = false, double? obliqueAngle = null) => new(weight, italic, width, null, obliqueAngle);

        [Theory]
        [InlineData(100, "ttf")]
        [InlineData(200, "ttf")]
        [InlineData(300, "ttf")]
        [InlineData(500, "otf")]
        [InlineData(700, "otf")]
        public void ARequestInsideARange_MatchesThatFace(int weight, string expected)
        {
            var resolver = Build(Weights(100, 300), Weights(500, 700));

            Assert.Equal(FaceNameOf(expected == "ttf" ? BundledFonts.Ttf : BundledFonts.Otf), resolver.ResolveFace(Family, Request(weight)).FaceName);
        }

        [Theory]
        [InlineData(350, "ttf")]   // below 400: search downward first, so the nearer 300 beats 500
        [InlineData(400, "otf")]   // 400 to 500: search up to 500 first, so 500 beats the 300 below
        [InlineData(450, "otf")]
        [InlineData(800, "otf")]   // above 500: search upward first, but nothing is above 700 so the nearest below wins
        [InlineData(50, "ttf")]
        [InlineData(399.5, "ttf")]   // fractions are searched by the same order: just below 400 searches downward
        [InlineData(400.5, "otf")]
        public void ARequestOutsideEveryRange_TakesTheNearestRangeBySpecificationOrder(double weight, string expected)
        {
            var resolver = Build(Weights(100, 300), Weights(500, 700));

            Assert.Equal(FaceNameOf(expected == "ttf" ? BundledFonts.Ttf : BundledFonts.Otf), resolver.ResolveFace(Family, Request(weight)).FaceName);
        }

        [Fact]
        public void AFractionalWeight_IsHeldByARangeThatHoldsItAndOnlyIt()
        {
            // 350.5 is inside the first range and not inside either whole weight around it; matched as 350 it would have missed the range
            // and taken the nearest of the other one.
            var resolver = Build(Weights(350.2, 350.8), Weights(100, 300));

            Assert.Equal(FaceNameOf(BundledFonts.Ttf), resolver.ResolveFace(Family, Request(350.5)).FaceName);
            Assert.Equal(FaceNameOf(BundledFonts.Otf), resolver.ResolveFace(Family, Request(350)).FaceName);
        }

        [Fact]
        public void ARequestBetweenTwoRangesAbove500_SearchesUpward()
        {
            var resolver = Build(Weights(100, 300), Weights(700, 800));

            Assert.Equal(FaceNameOf(BundledFonts.Otf), resolver.ResolveFace(Family, Request(600)).FaceName);
        }

        [Theory]
        [InlineData(60, "ttf")]
        [InlineData(75, "ttf")]
        [InlineData(90, "ttf")]    // at or narrower than normal: narrower first
        [InlineData(100, "otf")]
        [InlineData(140, "otf")]   // wider than normal: wider first, and 125 is the widest
        [InlineData(300, "otf")]
        public void WidthsAreMatchedLikeWeights_ByRange(double width, string expected)
        {
            var resolver = Build(Widths(50, 75), Widths(100, 125));

            Assert.Equal(FaceNameOf(expected == "ttf" ? BundledFonts.Ttf : BundledFonts.Otf), resolver.ResolveFace(Family, Request(400, width)).FaceName);
        }

        [Fact]
        public void TheLastFaceWins_WhereTwoRangesBothCoverTheRequest()
        {
            var resolver = Build(Weights(100, 900), Weights(300, 500));

            Assert.Equal(FaceNameOf(BundledFonts.Otf), resolver.ResolveFace(Family, Request(400)).FaceName);
            Assert.Equal(FaceNameOf(BundledFonts.Ttf), resolver.ResolveFace(Family, Request(800)).FaceName);
        }

        [Fact]
        public void ABoldRequest_IsNotFaked_WhereTheRangeReachesBold_AndIsWhereItDoesNot()
        {
            var reaches = Build(Weights(100, 900), Weights(100, 900)).ResolveFace(Family, Request(700));
            var short_ = Build(Weights(100, 500), Weights(100, 500)).ResolveFace(Family, Request(700));

            Assert.False(reaches.MustSimulateBold);
            Assert.True(short_.MustSimulateBold);
        }

        [Fact]
        public void AnObliqueRange_ServesItalicRequests_AndIsBeatenByAnUprightFaceForUprightOnes()
        {
            var resolver = new FontResolver();
            using (var ttf = File.OpenRead(BundledFonts.Ttf))
                resolver.AddFont(ttf, Family, new FontResolver.DeclaredFace(null, false, null, null), null);
            using (var otf = File.OpenRead(BundledFonts.Otf))
                resolver.AddFont(otf, Family, new FontResolver.DeclaredFace(null, true, null, new AxisRange(0, 14)), null);

            var italic = resolver.ResolveFace(Family, Request(400, italic: true));
            var upright = resolver.ResolveFace(Family, Request(400, italic: false));

            Assert.Equal(FaceNameOf(BundledFonts.Otf), italic.FaceName);
            Assert.False(italic.MustSimulateItalic);
            // Both faces could serve upright text; the one that is upright itself wins.
            Assert.Equal(FaceNameOf(BundledFonts.Ttf), upright.FaceName);
            Assert.Equal(new AxisRange(0, 14), italic.DeclaredRanges!.Oblique);
        }

        [Fact]
        public void AnUprightFace_BeatsAnObliqueRangeForUprightText_HoweverTheyWereDeclared()
        {
            // The oblique range includes 0, so it could serve upright text; the face that is upright itself is the better answer even
            // though it was declared first.
            var resolver = new FontResolver();
            using (var ttf = File.OpenRead(BundledFonts.Ttf))
                resolver.AddFont(ttf, Family, new FontResolver.DeclaredFace(null, false, null, null), null);
            using (var otf = File.OpenRead(BundledFonts.Otf))
                resolver.AddFont(otf, Family, new FontResolver.DeclaredFace(new AxisRange(100, 900), true, null, new AxisRange(0, 14)), null);

            Assert.Equal(FaceNameOf(BundledFonts.Ttf), resolver.ResolveFace(Family, Request(400, italic: false)).FaceName);
            // ... and the same when the exact step finds nothing and the nearest face is looked for.
            Assert.Equal(FaceNameOf(BundledFonts.Ttf), resolver.ResolveFace(Family, Request(400, 150, italic: false)).FaceName);
        }

        [Fact]
        public void AnObliqueRangeThatExcludesZero_DoesNotServeUprightTextWhenAnUprightFaceExists()
        {
            var resolver = new FontResolver();
            using (var ttf = File.OpenRead(BundledFonts.Ttf))
                resolver.AddFont(ttf, Family, new FontResolver.DeclaredFace(null, false, null, null), null);
            using (var otf = File.OpenRead(BundledFonts.Otf))
                resolver.AddFont(otf, Family, new FontResolver.DeclaredFace(null, true, null, new AxisRange(10, 14)), null);

            Assert.Equal(FaceNameOf(BundledFonts.Ttf), resolver.ResolveFace(Family, Request(400, italic: false)).FaceName);
        }

        [Fact]
        public void TheDeclaredRanges_TravelWithTheResolvedFace_AndAnOrdinaryFaceHasNone()
        {
            var resolver = Build(Weights(100, 300), new FontResolver.DeclaredFace(null, false, null, null));

            var ranged = resolver.ResolveFace(Family, Request(200));
            var ordinary = resolver.ResolveFace(Family, Request(700));

            Assert.Equal(new AxisRange(100, 300), ranged.DeclaredRanges!.Weight);
            Assert.Equal(new AxisRange(100, 300), Weights(100, 300).Weight);
            Assert.Null(ordinary.DeclaredRanges);
        }

        [Fact]
        public void ARequestWithAWeightEqualToARangeAtAnotherWidth_DoesNotThrow()
        {
            // Nothing matches the width exactly and the only weight on offer equals the target: the nearest-weight search must hand it back.
            var resolver = Build(new FontResolver.DeclaredFace(new AxisRange(300), false, new AxisRange(75), null), Widths(75, 75));

            var info = resolver.ResolveFace(Family, Request(300, 100));

            Assert.NotNull(info);
        }

        [Fact]
        public void TheClassOverloads_AreTheClassPercentages()
        {
            var resolver = Build(Widths(50, 75), Widths(100, 125));

            Assert.Equal(FaceNameOf(BundledFonts.Ttf), resolver.ResolveTypeface(Family, 400, false, 3).FaceName);   // 75%
            Assert.Equal(FaceNameOf(BundledFonts.Otf), resolver.ResolveTypeface(Family, 400, false, 7).FaceName);   // 125%
        }

        [Theory]
        [InlineData(1, 50)]
        [InlineData(4, 87.5)]
        [InlineData(5, 100)]
        [InlineData(9, 200)]
        [InlineData(0, 50)]
        [InlineData(12, 200)]
        public void WidthClasses_ConvertToPercentages(int widthClass, double expected)
        {
            Assert.Equal(expected, WidthClasses.ToPercent(widthClass));
        }

        [Theory]
        [InlineData(50, 1)]
        [InlineData(90, 4)]
        [InlineData(100, 5)]
        [InlineData(140, 8)]
        [InlineData(1000, 9)]
        public void Percentages_ConvertToTheNearestWidthClass(double percent, int expected)
        {
            Assert.Equal(expected, WidthClasses.FromPercent(percent));
        }

        private static FontResolver.DeclaredFace Oblique(double minimum, double maximum) =>
            new(null, true, null, new AxisRange(minimum, maximum));

        private static FontResolver.DeclaredFace Italic() => new(null, true, null, null);

        private static FontResolver.DeclaredFace Upright() => new(null, false, null, null);

        private static FontResolver.DeclaredFace At(double weight, bool italic, double width) =>
            new(new AxisRange(weight), italic, new AxisRange(width), null);

        [Fact]
        public void TheWidthIsNarrowedBeforeTheStyle_SoACondensedItalicRequestGetsTheCondensedUprightFace()
        {
            // The family has a condensed upright face and an italic face of normal width. CSS Fonts 4 section 5.2 narrows by width first, so
            // condensed italic text is set in the condensed face (and its lean is faked), not in the italic one.
            var resolver = Build(At(400, italic: false, width: 75), At(400, italic: true, width: 100));
            var condensed = FaceNameOf(BundledFonts.Ttf);
            var italic = FaceNameOf(BundledFonts.Otf);

            var condensedItalic = resolver.ResolveFace(Family, Request(400, 75, italic: true));
            Assert.Equal(condensed, condensedItalic.FaceName);
            Assert.True(condensedItalic.MustSimulateItalic);

            var normalItalic = resolver.ResolveFace(Family, Request(400, 100, italic: true));
            Assert.Equal(italic, normalItalic.FaceName);
            Assert.False(normalItalic.MustSimulateItalic);

            // Normal-width upright text: the width leaves only the italic face, which is then what there is.
            Assert.Equal(italic, resolver.ResolveFace(Family, Request(400, 100)).FaceName);
            Assert.Equal(condensed, resolver.ResolveFace(Family, Request(400, 75)).FaceName);
            // 90% is not a width either has: at or below normal searches narrower first, so 75 wins over 100.
            Assert.Equal(condensed, resolver.ResolveFace(Family, Request(400, 90, italic: true)).FaceName);
        }

        [Fact]
        public void TheStyleIsNarrowedBeforeTheWeight()
        {
            // An italic bold request goes to the italic face even though it is the light one; the bold is faked.
            var resolver = Build(At(700, italic: false, width: 100), At(300, italic: true, width: 100));

            var info = resolver.ResolveFace(Family, Request(700, italic: true));

            Assert.Equal(FaceNameOf(BundledFonts.Otf), info.FaceName);
            Assert.True(info.MustSimulateBold);
            Assert.False(info.MustSimulateItalic);
        }

        [Theory]
        [InlineData(5.0, 0)]     // inside the first range
        [InlineData(25.0, 1)]    // inside the second
        [InlineData(10.5, 0)]    // below 11 degrees: the angles below are searched first
        [InlineData(15.0, 1)]    // 11 degrees or more: the angles above are searched first
        [InlineData(12.0, 1)]
        [InlineData(40.0, 1)]    // nothing above: the nearest below
        [InlineData(null, 1)]    // italic with no angle is compared as 11 degrees
        public void AmongObliqueRanges_TheRequestedAngleChoosesTheNearest_WhicheverWasDeclaredLast(double? angle, int expectedRange)
        {
            var ranges = new[] { new AxisRange(0, 10), new AxisRange(20, 30) };

            foreach (var reversed in new[] { false, true })
            {
                var declared = reversed ? new[] { ranges[1], ranges[0] } : ranges;
                var resolver = Build(Oblique(declared[0].Minimum, declared[0].Maximum), Oblique(declared[1].Minimum, declared[1].Maximum));

                var info = resolver.ResolveFace(Family, Request(400, italic: true, obliqueAngle: angle));

                Assert.Equal(ranges[expectedRange], info.DeclaredRanges!.Oblique);
                Assert.False(info.MustSimulateItalic);
            }
        }

        [Theory]
        [InlineData(-25, 1)]
        [InlineData(-15, 1)]
        [InlineData(-8, 0)]
        [InlineData(-10.5, 0)]
        public void ALeanToTheLeft_IsTheMirrorImage(double angle, int expectedRange)
        {
            var ranges = new[] { new AxisRange(-10, -5), new AxisRange(-30, -20) };
            var resolver = Build(Oblique(ranges[0].Minimum, ranges[0].Maximum), Oblique(ranges[1].Minimum, ranges[1].Maximum));

            var info = resolver.ResolveFace(Family, Request(400, italic: true, obliqueAngle: angle));

            Assert.Equal(ranges[expectedRange], info.DeclaredRanges!.Oblique);
        }

        [Fact]
        public void ARangeThatHoldsAnAngleBeatsOneThatIsMerelyNearer()
        {
            var resolver = Build(Oblique(0, 20), Oblique(14, 16));

            Assert.Equal(new AxisRange(0, 20), resolver.ResolveFace(Family, Request(400, italic: true, obliqueAngle: 10)).DeclaredRanges!.Oblique);
            // 15 is held by both, and the one declared last wins.
            Assert.Equal(new AxisRange(14, 16), resolver.ResolveFace(Family, Request(400, italic: true, obliqueAngle: 15)).DeclaredRanges!.Oblique);
        }

        [Fact]
        public void AnItalicFace_IsPreferredToAnObliqueRange_ForItalicText_AndTheRangeForAnExplicitAngle()
        {
            foreach (var italicFirst in new[] { true, false })
            {
                var resolver = italicFirst ? Build(Italic(), Oblique(0, 14)) : Build(Oblique(0, 14), Italic());
                var italicName = FaceNameOf(italicFirst ? BundledFonts.Ttf : BundledFonts.Otf);
                var obliqueName = FaceNameOf(italicFirst ? BundledFonts.Otf : BundledFonts.Ttf);

                Assert.Equal(italicName, resolver.ResolveFace(Family, Request(400, italic: true)).FaceName);
                Assert.Equal(obliqueName, resolver.ResolveFace(Family, Request(400, italic: true, obliqueAngle: 10)).FaceName);
            }
        }

        [Fact]
        public void AnExplicitAngle_FallsBackToAnItalicFace_WhereThereIsNoObliqueRange()
        {
            var resolver = Build(Upright(), Italic());

            var info = resolver.ResolveFace(Family, Request(400, italic: true, obliqueAngle: 10));

            Assert.Equal(FaceNameOf(BundledFonts.Otf), info.FaceName);
            Assert.False(info.MustSimulateItalic);
        }

        [Fact]
        public void ItalicText_WithNoItalicFaceAtAll_TakesAnUprightFaceAndFakesTheLean()
        {
            var resolver = Build(Upright(), Upright());

            var info = resolver.ResolveFace(Family, Request(400, italic: true, obliqueAngle: 10));

            Assert.True(info.MustSimulateItalic);
        }

        [Fact]
        public void UprightText_WithOnlyObliqueRangesThatExcludeZero_TakesTheOneNearestToUpright()
        {
            var resolver = Build(Oblique(20, 30), Oblique(10, 14));

            var info = resolver.ResolveFace(Family, Request(400));

            Assert.Equal(new AxisRange(10, 14), info.DeclaredRanges!.Oblique);
        }

        [Fact]
        public void UprightText_WithOnlyItalicFaces_TakesOneOfThem()
        {
            var resolver = Build(Italic(), Italic());

            var info = resolver.ResolveFace(Family, Request(400));

            Assert.False(info.MustSimulateItalic);
            Assert.Equal(FaceNameOf(BundledFonts.Otf), info.FaceName);
        }

        [Fact]
        public void UprightText_WithOnlyObliqueRangesBelowZero_TakesTheOneNearestToUpright()
        {
            var resolver = Build(Oblique(-30, -20), Oblique(-10, -5));

            var info = resolver.ResolveFace(Family, Request(400));

            Assert.Equal(new AxisRange(-10, -5), info.DeclaredRanges!.Oblique);
        }

        [Fact]
        public void AnItalicFace_BeatsAnObliqueRangeThatIsAtOrBelowZero_ForAPositiveObliqueAngle()
        {
            // The family's only oblique range leans left of upright (entirely at or below 0), and a positive oblique angle is requested.
            // CSS Fonts 4 §5.2 tries the oblique ranges on the same (positive) side of upright first, then the italic faces, and only then
            // crosses over to the ranges on the other side - so the italic face must win here, not the negative range.
            var resolver = Build(Oblique(-30, -20), Italic());

            var info = resolver.ResolveFace(Family, Request(400, italic: true, obliqueAngle: 20));

            Assert.Equal(FaceNameOf(BundledFonts.Otf), info.FaceName);
            Assert.Null(info.DeclaredRanges);
        }

        [Fact]
        public void AnItalicRequest_StillPrefersItsItalicFace_OverAnObliqueRangeAtOrBelowZero()
        {
            // The plain `italic` keyword (no explicit angle) already prefers a declared italic face over any oblique range, whichever
            // side of upright that range leans - unaffected by the explicit-angle fall-through fix above.
            var resolver = Build(Oblique(-30, -20), Italic());

            var info = resolver.ResolveFace(Family, Request(400, italic: true));

            Assert.Equal(FaceNameOf(BundledFonts.Otf), info.FaceName);
        }

        [Fact]
        public void AnUprightFace_BeatsAnObliqueRangeThatExcludesZero_ForAnExplicitObliqueZeroRequest()
        {
            // `oblique 0deg` is upright's equivalent on the specification's scale: an upright face should win over an oblique range that
            // merely happens to exclude 0, the same preference `PreferStrictSlant` already gives upright requests.
            var resolver = Build(Oblique(10, 14), Upright());

            var info = resolver.ResolveFace(Family, Request(400, italic: true, obliqueAngle: 0));

            Assert.Equal(FaceNameOf(BundledFonts.Otf), info.FaceName);
            Assert.Null(info.DeclaredRanges);
        }

        [Fact]
        public void AnUprightFace_BeatsAnObliqueRangeThatIncludesZero_ForAnExplicitObliqueZeroRequest()
        {
            // Even a range that does include 0 is only "equivalent to upright" for the text it draws - an upright face declared as such
            // is still the more precise match, same as `AnUprightFace_BeatsAnObliqueRangeForUprightText_HoweverTheyWereDeclared` above.
            var resolver = Build(Oblique(-10, 10), Upright());

            var info = resolver.ResolveFace(Family, Request(400, italic: true, obliqueAngle: 0));

            Assert.Equal(FaceNameOf(BundledFonts.Otf), info.FaceName);
            Assert.Null(info.DeclaredRanges);
        }

        [Fact]
        public void AnObliqueZeroRequest_FallsBackToTheOldSearch_WhereThereIsNoUprightFace()
        {
            // With no upright face and no range covering 0, an explicit `oblique 0deg` request behaves like any other non-negative angle:
            // the nearest oblique range wins.
            var resolver = Build(Oblique(20, 30), Oblique(10, 14));

            var info = resolver.ResolveFace(Family, Request(400, italic: true, obliqueAngle: 0));

            Assert.Equal(new AxisRange(10, 14), info.DeclaredRanges!.Oblique);
        }

        [Fact]
        public void WidthThenStyleThenWeight_OrderingAndPreferStrictSlant_StillHoldAfterTheObliqueFallThroughFix()
        {
            // Regression guard for the ordering the prior face-matching PR established: width narrows before style, style narrows before
            // weight, and PreferStrictSlant still prefers a genuinely upright/italic face over a range that merely covers the request.
            var widthFirst = Build(At(400, italic: true, width: 75), At(400, italic: false, width: 100));
            var condensedItalicRequest = widthFirst.ResolveFace(Family, Request(400, 75, italic: true));
            Assert.Equal(FaceNameOf(BundledFonts.Ttf), condensedItalicRequest.FaceName);
            Assert.False(condensedItalicRequest.MustSimulateItalic);

            var styleBeforeWeight = Build(At(700, italic: false, width: 100), At(300, italic: true, width: 100));
            var italicBoldRequest = styleBeforeWeight.ResolveFace(Family, Request(700, italic: true));
            Assert.Equal(FaceNameOf(BundledFonts.Otf), italicBoldRequest.FaceName);
            Assert.True(italicBoldRequest.MustSimulateBold);
            Assert.False(italicBoldRequest.MustSimulateItalic);

            var strictSlant = Build(new FontResolver.DeclaredFace(null, false, null, null), new FontResolver.DeclaredFace(null, true, null, new AxisRange(0, 14)));
            var uprightRequest = strictSlant.ResolveFace(Family, Request(400, italic: false));
            Assert.Equal(FaceNameOf(BundledFonts.Ttf), uprightRequest.FaceName);
        }

        [Fact]
        public void ATypefaceKey_CarriesAFractionalWeight_AndTheAngleOfAnItalicRequestOnly()
        {
            var whole = new FontResolvingOptions(FaceStyle.Regular, 350).ComputeTypefaceKey("F");
            var fraction = new FontResolvingOptions(FaceStyle.Regular, 350.5).ComputeTypefaceKey("F");
            var angled = new FontResolvingOptions(FaceStyle.Italic) { ObliqueAngle = 20 }.ComputeTypefaceKey("F");
            var plainItalic = new FontResolvingOptions(FaceStyle.Italic).ComputeTypefaceKey("F");
            var uprightWithAngle = new FontResolvingOptions(FaceStyle.Regular) { ObliqueAngle = 20 }.ComputeTypefaceKey("F");

            Assert.Equal("tk:f/n/350/5", whole);
            Assert.Equal("tk:f/n/350.5/5", fraction);
            Assert.Equal("tk:f/i@20/400/5", angled);
            Assert.Equal("tk:f/i/400/5", plainItalic);
            Assert.Equal("tk:f/n/400/5", uprightWithAngle);
        }

        [Fact]
        public void ATypefaceKey_ForAWidthBetweenTheClasses_DiffersFromEveryClassKey()
        {
            var byClass = new FontResolvingOptions(FaceStyle.Regular, 400, 5).ComputeTypefaceKey("F");
            var samePercent = FontResolvingOptions.ForWidthPercent(FaceStyle.Regular, 400, 100.0).ComputeTypefaceKey("F");
            var between = FontResolvingOptions.ForWidthPercent(FaceStyle.Regular, 400, 93.5).ComputeTypefaceKey("F");

            Assert.Equal("tk:f/n/400/5", byClass);
            Assert.Equal(byClass, samePercent);
            Assert.Equal("tk:f/n/400/w93.5", between);
        }
    }
}
