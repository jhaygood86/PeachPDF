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

        private static FaceRequest Request(int weight, double width = 100, bool italic = false) => new(weight, italic, width);

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
        public void ARequestOutsideEveryRange_TakesTheNearestRangeBySpecificationOrder(int weight, string expected)
        {
            var resolver = Build(Weights(100, 300), Weights(500, 700));

            Assert.Equal(FaceNameOf(expected == "ttf" ? BundledFonts.Ttf : BundledFonts.Otf), resolver.ResolveFace(Family, Request(weight)).FaceName);
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
