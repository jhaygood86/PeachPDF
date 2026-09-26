using PeachPDF.Tests.TestSupport;

namespace PeachDrawing.Text.Tests.Fonts
{
    /// <summary>
    /// A face that declares a range of weights, widths or oblique angles (the range form of the <c>@font-face</c> descriptors) is matched
    /// as covering every value in it, is set inside it, and needs nothing faked that its axes can do. A variable font declared with
    /// no range covers the range of its own axes.
    /// </summary>
    public class VariableFaceRangesTests
    {
        private static string NewName() => "Ranges-" + Guid.NewGuid().ToString("N");

        private static TypefaceFamily Add(FontSet set, string path, string name, AddOptions options) =>
            set.AddFile(path, new AddOptions
            {
                FamilyName = name,
                Weight = options.Weight,
                IsItalic = options.IsItalic,
                Width = options.Width,
                WeightRange = options.WeightRange,
                WidthRange = options.WidthRange,
                ObliqueRange = options.ObliqueRange,
                UnicodeRanges = options.UnicodeRanges
            });

        private static double Value(Typeface typeface, string tag) => typeface.AxisSettings.Single(s => s.Tag == tag).Value;

        [Theory]
        [InlineData(100, 300)]   // below the range: brought up to its lower end
        [InlineData(300, 300)]
        [InlineData(450, 450)]   // inside the range: what was asked for
        [InlineData(600, 600)]
        [InlineData(900, 600)]   // above the range: brought down to its upper end
        public void ADeclaredWeightRange_KeepsTheWeightAxisInsideIt(int requested, double expected)
        {
            var family = Add(new FontSet(), BundledFonts.VariableTest, NewName(), new AddOptions { WeightRange = new AxisRange(300, 600) });

            Assert.True(family.TryMatch(new TypefaceQuery(requested), out var match));

            Assert.Equal(expected, Value(match.Typeface, "wght"));
        }

        [Fact]
        public void ARangeThatCannotReachABoldRequest_LeavesTheBoldToBeFaked()
        {
            var family = Add(new FontSet(), BundledFonts.VariableTest, NewName(), new AddOptions { WeightRange = new AxisRange(300, 500) });

            Assert.True(family.TryMatch(new TypefaceQuery(700), out var match));

            Assert.Equal(500, Value(match.Typeface, "wght"));
            Assert.Equal(SyntheticStyle.Bold, match.Synthesis);
        }

        [Fact]
        public void ARangeThatReachesABoldRequest_FakesNothing()
        {
            var family = Add(new FontSet(), BundledFonts.VariableTest, NewName(), new AddOptions { WeightRange = new AxisRange(100, 900) });

            Assert.True(family.TryMatch(new TypefaceQuery(700), out var match));

            Assert.Equal(700, Value(match.Typeface, "wght"));
            Assert.Equal(SyntheticStyle.None, match.Synthesis);
        }

        [Fact]
        public void ADeclaredSingleWeight_IsARangeOfOne()
        {
            var family = Add(new FontSet(), BundledFonts.VariableTest, NewName(), new AddOptions { Weight = 700 });

            Assert.True(family.TryMatch(new TypefaceQuery(400), out var match));

            Assert.Equal(700, Value(match.Typeface, "wght"));
        }

        [Theory]
        [InlineData(50, 90)]
        [InlineData(90, 90)]
        [InlineData(100, 100)]
        [InlineData(125, 110)]
        public void ADeclaredWidthRange_KeepsTheWidthAxisInsideIt(double requested, double expected)
        {
            var family = Add(new FontSet(), BundledFonts.VariableTest, NewName(), new AddOptions { WidthRange = new AxisRange(90, 110) });

            Assert.True(family.TryMatch(new TypefaceQuery(WidthPercent: requested), out var match));

            Assert.Equal(expected, Value(match.Typeface, "wdth"));
        }

        [Fact]
        public void AWidthAsAPercentage_SetsTheWidthAxis_WhereTheClassOnlyHasNinePlaces()
        {
            var family = Add(new FontSet(), BundledFonts.VariableTest, NewName(), new AddOptions());

            Assert.True(family.TryMatch(new TypefaceQuery(WidthPercent: 93.5), out var match));

            Assert.Equal(93.5, Value(match.Typeface, "wdth"));
        }

        [Fact]
        public void APercentageThatIsAClassPercentage_IsTheSameTypefaceAsTheClass()
        {
            var family = Add(new FontSet(), BundledFonts.VariableTest, NewName(), new AddOptions());

            Assert.True(family.TryMatch(new TypefaceQuery(400, 4), out var byClass));
            Assert.True(family.TryMatch(new TypefaceQuery(WidthPercent: 87.5), out var byPercent));
            Assert.True(family.TryMatch(new TypefaceQuery(WidthPercent: 88), out var different));

            Assert.Same(byClass.Typeface, byPercent.Typeface);
            Assert.NotSame(byClass.Typeface, different.Typeface);
            Assert.NotEqual(byClass.Typeface.VariationKey, different.Typeface.VariationKey);
        }

        [Fact]
        public void AVariableFontDeclaredWithNoRange_CoversTheRangeOfItsAxes()
        {
            var set = new FontSet();
            var name = NewName();
            Add(set, BundledFonts.VariableTest, name, new AddOptions());
            // A static face of the same family that is registered later and declares a weight the variable font also covers.
            var family = Add(set, BundledFonts.Ttf, name, new AddOptions { Weight = 700, IsItalic = false });

            Assert.True(family.TryMatch(new TypefaceQuery(300), out var light));   // 300 is inside the variable font's 100 to 900 only
            Assert.True(family.TryMatch(new TypefaceQuery(700), out var bold));    // both cover it; the one added last wins

            Assert.True(light.Typeface.IsVariable);
            Assert.Equal(300, Value(light.Typeface, "wght"));
            Assert.False(bold.Typeface.IsVariable);
        }

        [Fact]
        public void TwoRangesOfOneFile_AreTwoFaces_AndTheRequestPicksTheOneThatCoversIt()
        {
            var set = new FontSet();
            var name = NewName();
            Add(set, BundledFonts.VariableTest, name, new AddOptions { WeightRange = new AxisRange(100, 400) });
            var family = Add(set, BundledFonts.VariableTest, name, new AddOptions { WeightRange = new AxisRange(600, 900) });

            Assert.True(family.TryMatch(new TypefaceQuery(350), out var light));
            Assert.True(family.TryMatch(new TypefaceQuery(800), out var heavy));
            Assert.True(family.TryMatch(new TypefaceQuery(500), out var between));   // 500: searches up to 500, then down, so 400 wins
            Assert.True(family.TryMatch(new TypefaceQuery(700), out var boldish));

            Assert.Equal(350, Value(light.Typeface, "wght"));
            Assert.Equal(800, Value(heavy.Typeface, "wght"));
            Assert.Equal(400, Value(between.Typeface, "wght"));
            Assert.Equal(700, Value(boldish.Typeface, "wght"));
        }

        [Fact]
        public void TheSameRangeAddedTwice_ReplacesTheFace_AndADifferentOneJoinsIt()
        {
            var set = new FontSet();
            var name = NewName();
            Add(set, BundledFonts.VariableTest, name, new AddOptions { WeightRange = new AxisRange(100, 300) });
            Add(set, BundledFonts.VariableTest, name, new AddOptions { WeightRange = new AxisRange(100, 300) });
            var family = Add(set, BundledFonts.VariableTest, name, new AddOptions { WeightRange = new AxisRange(500, 900) });

            Assert.True(family.TryMatch(new TypefaceQuery(200), out var low));
            Assert.True(family.TryMatch(new TypefaceQuery(700), out var high));

            Assert.Equal(200, Value(low.Typeface, "wght"));
            Assert.Equal(700, Value(high.Typeface, "wght"));
        }

        [Theory]
        [InlineData(5, 5)]
        [InlineData(10, 10)]
        [InlineData(14, 10)]
        [InlineData(25, 10)]
        public void ADeclaredObliqueRange_KeepsTheSlantAxisInsideIt(double angle, double expectedLean)
        {
            var family = Add(new FontSet(), BundledFonts.VariableSlantTest, NewName(), new AddOptions { ObliqueRange = new AxisRange(0, 10) });

            Assert.True(family.TryMatch(new TypefaceQuery(IsItalic: true, ObliqueAngle: angle), out var match));

            // The slant axis measures a lean to the right as negative.
            Assert.Equal(-expectedLean, Value(match.Typeface, "slnt"));
        }

        [Fact]
        public void AnItalicRequestWithNoAngle_LeansFourteenDegrees_WhereTheAxisAllows()
        {
            var family = Add(new FontSet(), BundledFonts.VariableSlantTest, NewName(), new AddOptions { ObliqueRange = new AxisRange(0, 15) });

            Assert.True(family.TryMatch(new TypefaceQuery(IsItalic: true), out var match));

            Assert.Equal(-14, Value(match.Typeface, "slnt"));
        }

        [Fact]
        public void TheSlantAxis_IsNotFakedAsWell()
        {
            var family = Add(new FontSet(), BundledFonts.VariableSlantTest, NewName(), new AddOptions { ObliqueRange = new AxisRange(0, 15) });

            Assert.True(family.TryMatch(new TypefaceQuery(IsItalic: true), out var match));

            Assert.Equal(SyntheticStyle.None, match.Synthesis);
        }

        [Fact]
        public void AnObliqueRangeThatIncludesZero_AlsoServesUprightText_AtNoSlant()
        {
            var family = Add(new FontSet(), BundledFonts.VariableSlantTest, NewName(), new AddOptions { ObliqueRange = new AxisRange(0, 15) });

            Assert.True(family.TryMatch(new TypefaceQuery(), out var upright));

            Assert.Equal(0, Value(upright.Typeface, "slnt"));
            Assert.Equal(SyntheticStyle.None, upright.Synthesis);
        }

        [Fact]
        public void AnObliqueRangeThatExcludesZero_BringsUprightTextToItsNearestEnd()
        {
            var family = Add(new FontSet(), BundledFonts.VariableSlantTest, NewName(), new AddOptions { ObliqueRange = new AxisRange(10, 15) });

            Assert.True(family.TryMatch(new TypefaceQuery(), out var match));

            Assert.Equal(-10, Value(match.Typeface, "slnt"));
        }

        [Fact]
        public void ASlantAxisWithNoDeclaredRange_IsCoveredAsAnObliqueRange()
        {
            // The axis runs from 0 to -15, which is 0 to 15 degrees of oblique: upright and italic text are both served by the face.
            var family = Add(new FontSet(), BundledFonts.VariableSlantTest, NewName(), new AddOptions());

            Assert.True(family.TryMatch(new TypefaceQuery(), out var upright));
            Assert.True(family.TryMatch(new TypefaceQuery(IsItalic: true, ObliqueAngle: 20), out var italic));

            Assert.Equal(0, Value(upright.Typeface, "slnt"));
            Assert.Equal(-15, Value(italic.Typeface, "slnt"));
            Assert.Equal(SyntheticStyle.None, italic.Synthesis);
        }

        [Fact]
        public void AFaceDeclaredItalicWithNoRange_IsUsedForItalicText_AndNothingIsFaked()
        {
            var set = new FontSet();
            var name = NewName();
            Add(set, BundledFonts.Ttf, name, new AddOptions { IsItalic = false });
            var family = Add(set, BundledFonts.Otf, name, new AddOptions { IsItalic = true });

            Assert.True(family.TryMatch(new TypefaceQuery(IsItalic: true), out var match));

            Assert.Equal(SyntheticStyle.None, match.Synthesis);
            Assert.Equal(TtfName(BundledFonts.Otf), match.Typeface.FullName);
        }

        [Fact]
        public void FontVariationSettings_StillWinOverTheRange()
        {
            var family = Add(new FontSet(), BundledFonts.VariableTest, NewName(), new AddOptions { WeightRange = new AxisRange(300, 600) });

            Assert.True(family.TryMatch(new TypefaceQuery(400, Axes: [new AxisSetting("wght", 900)]), out var match));

            Assert.Equal(900, Value(match.Typeface, "wght"));
        }

        [Fact]
        public void ACodepointRestrictedMatch_IsSetInsideTheRangeToo()
        {
            var family = Add(new FontSet(), BundledFonts.VariableTest, NewName(), new AddOptions { WeightRange = new AxisRange(300, 600) });

            Assert.True(family.TryMatch(new TypefaceQuery(900, MustCover: new System.Text.Rune('A')), out var match));

            Assert.Equal(600, Value(match.Typeface, "wght"));
        }

        [Fact]
        public void TwoRegistrationsOfOneFile_WithDifferentRanges_DoNotShareACodepointTypeface()
        {
            var set = new FontSet();
            var name = NewName();
            Add(set, BundledFonts.VariableTest, name, new AddOptions { WeightRange = new AxisRange(100, 300) });
            var family = Add(set, BundledFonts.VariableTest, name, new AddOptions { WeightRange = new AxisRange(700, 900) });

            Assert.True(family.TryMatch(new TypefaceQuery(100, MustCover: new System.Text.Rune('A')), out var light));
            Assert.True(family.TryMatch(new TypefaceQuery(900, MustCover: new System.Text.Rune('A')), out var heavy));

            Assert.Equal(100, Value(light.Typeface, "wght"));
            Assert.Equal(900, Value(heavy.Typeface, "wght"));
        }

        [Fact]
        public void AFaceThatIsNotVariable_IgnoresTheRangesItIsGiven()
        {
            var family = Add(new FontSet(), BundledFonts.Ttf, NewName(), new AddOptions { WeightRange = new AxisRange(100, 900), WidthRange = new AxisRange(50, 200) });

            Assert.True(family.TryMatch(new TypefaceQuery(700, WidthPercent: 150), out var match));

            Assert.False(match.Typeface.IsVariable);
            Assert.Equal(string.Empty, match.Typeface.VariationKey);
            Assert.Equal(SyntheticStyle.None, match.Synthesis);
        }

        private static string TtfName(string path) => new FontSet().AddFile(path, new AddOptions { FamilyName = NewName() })
            .TryMatch(new TypefaceQuery(), out var match) ? match.Typeface.FullName : "";
    }

    public class AxisRangeTests
    {
        [Fact]
        public void TheConstructor_OrdersTheEnds()
        {
            var range = new AxisRange(900, 100);

            Assert.Equal(100, range.Minimum);
            Assert.Equal(900, range.Maximum);
        }

        [Fact]
        public void ASingleValue_IsARangeOfOne()
        {
            var range = new AxisRange(700);

            Assert.Equal(700, range.Minimum);
            Assert.Equal(700, range.Maximum);
            Assert.True(range.Contains(700));
            Assert.False(range.Contains(699.9));
        }

        [Theory]
        [InlineData(50, 100)]
        [InlineData(100, 100)]
        [InlineData(500, 500)]
        [InlineData(900, 900)]
        [InlineData(2000, 900)]
        public void Clamp_BringsAValueIntoTheRange(double value, double expected)
        {
            Assert.Equal(expected, new AxisRange(100, 900).Clamp(value));
        }

        [Fact]
        public void Ranges_CompareByTheirEnds()
        {
            Assert.Equal(new AxisRange(1, 2), new AxisRange(2, 1));
            Assert.True(new AxisRange(1, 2) == new AxisRange(1, 2));
            Assert.True(new AxisRange(1, 2) != new AxisRange(1, 3));
            Assert.False(new AxisRange(1, 2).Equals((object)"1..2"));
            Assert.Equal(new AxisRange(1, 2).GetHashCode(), new AxisRange(2, 1).GetHashCode());
        }

        [Fact]
        public void ToString_ShowsTheEnds()
        {
            Assert.Equal("100..900", new AxisRange(100, 900).ToString());
            Assert.Equal("700", new AxisRange(700).ToString());
            Assert.Equal("87.5", new AxisRange(87.5).ToString());
        }
    }
}
