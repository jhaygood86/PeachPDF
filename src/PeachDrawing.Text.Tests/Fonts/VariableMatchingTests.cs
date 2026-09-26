using PeachPDF.Tests.TestSupport;

namespace PeachDrawing.Text.Tests.Fonts
{
    /// <summary>
    /// Matching a query against a variable face: the weight and width of the query become a location in the design space, so a
    /// single file answers every weight and nothing has to be faked that the axes can do.
    /// </summary>
    public class VariableMatchingTests
    {
        private static TypefaceFamily Family()
        {
            var set = new FontSet();
            return set.AddFile(BundledFonts.VariableTest, new AddOptions { FamilyName = "Matching-" + Guid.NewGuid().ToString("N") });
        }

        private static double Value(Typeface typeface, string tag) => typeface.AxisSettings.Single(s => s.Tag == tag).Value;

        [Fact]
        public void TheWeightOfTheQuery_SetsTheWeightAxis_AndNothingIsFakedBold()
        {
            Assert.True(Family().TryMatch(new TypefaceQuery(700), out var match));

            Assert.Equal(700, Value(match.Typeface, "wght"));
            Assert.Equal(SyntheticStyle.None, match.Synthesis);
        }

        [Fact]
        public void AWeightBeyondTheAxis_IsClamped_AndTheFaceIsStillNotFakedBold()
        {
            Assert.True(Family().TryMatch(new TypefaceQuery(1000), out var match));

            Assert.Equal(900, Value(match.Typeface, "wght"));
            Assert.Equal(SyntheticStyle.None, match.Synthesis);
        }

        [Fact]
        public void TheWidthClassOfTheQuery_SetsTheWidthAxisToItsPercentage()
        {
            var family = Family();

            Assert.True(family.TryMatch(new TypefaceQuery(400, 3), out var condensed));   // semi-condensed is 75%
            Assert.True(family.TryMatch(new TypefaceQuery(400, 7), out var expanded));    // semi-expanded is 125%
            Assert.True(family.TryMatch(new TypefaceQuery(400, 9), out var widest));      // 200%, beyond the axis

            Assert.Equal(75, Value(condensed.Typeface, "wdth"));
            Assert.Equal(125, Value(expanded.Typeface, "wdth"));
            Assert.Equal(125, Value(widest.Typeface, "wdth"));
        }

        [Fact]
        public void ANormalQuery_GivesTheDefaultTypeface()
        {
            Assert.True(Family().TryMatch(new TypefaceQuery(), out var match));

            Assert.Equal(string.Empty, match.Typeface.VariationKey);
        }

        [Fact]
        public void TheAxesOfTheQuery_WinOverTheOnesTheWeightAndWidthGive()
        {
            var query = new TypefaceQuery(700, 5, false, null, [new AxisSetting("wght", 250), new AxisSetting("wdth", 80)]);
            Assert.True(Family().TryMatch(query, out var match));

            Assert.Equal(250, Value(match.Typeface, "wght"));
            Assert.Equal(80, Value(match.Typeface, "wdth"));
        }

        [Fact]
        public void Locations_HaveKeys_ThatDifferForDifferentLocations()
        {
            var family = Family();
            Assert.True(family.TryMatch(new TypefaceQuery(700), out var bold));
            Assert.True(family.TryMatch(new TypefaceQuery(600), out var semibold));
            Assert.True(family.TryMatch(new TypefaceQuery(700), out var boldAgain));

            Assert.NotEqual(bold.Typeface.VariationKey, semibold.Typeface.VariationKey);
            Assert.Equal(bold.Typeface.VariationKey, boldAgain.Typeface.VariationKey);
            Assert.Equal(bold.Typeface.ContentHash, semibold.Typeface.ContentHash);
        }

        [Fact]
        public void AFaceThatIsNotVariable_IgnoresTheAxes()
        {
            var set = new FontSet();
            var family = set.AddFile(BundledFonts.Ttf, new AddOptions { FamilyName = "Static-" + Guid.NewGuid().ToString("N") });

            Assert.True(family.TryMatch(new TypefaceQuery(400, 5, false, null, [new AxisSetting("wght", 900)]), out var match));

            Assert.False(match.Typeface.IsVariable);
            Assert.Equal(string.Empty, match.Typeface.VariationKey);
        }
    }
}
