using PeachDrawing.Text;
using PeachDrawing.Text.Shaping;
using PeachDrawing.Text.Unicode;
using PeachPDF.Tests.TestSupport;
using System.Text;

namespace PeachPDF.Tests.PublicApi
{
    /// <summary>
    /// The shaper as a consumer outside the assembly would use it, and the joining and syllable classifications that feed its
    /// settings. Shaping is also compared with the engine's own shaper, which the public one wraps.
    /// </summary>
    public class ShaperPublicApiTests
    {
        private static Typeface Face(string path)
        {
            var set = new FontSet();
            var family = set.AddFile(path, new AddOptions { FamilyName = "Shaper-" + Guid.NewGuid().ToString("N") });
            Assert.True(family.TryMatch(new TypefaceQuery(), out var match));
            return match.Typeface;
        }

        private static void AssertSameGlyphs(IReadOnlyList<PlacedGlyph> expected, IReadOnlyList<PlacedGlyph> actual)
        {
            Assert.Equal(expected.Count, actual.Count);
            for (var i = 0; i < expected.Count; i++)
            {
                Assert.Equal(expected[i] with { LigatureComponentClusterStarts = null }, actual[i] with { LigatureComponentClusterStarts = null });
                Assert.Equal(expected[i].LigatureComponentClusterStarts, actual[i].LigatureComponentClusterStarts);
            }
        }

        [Fact]
        public void Shape_MapsEachCharacterToItsGlyphAndKeepsTheClusters()
        {
            var face = Face(BundledFonts.Ttf);

            var run = Shaper.Shape(face, "Hi!", ShapeSettings.Default);

            Assert.Same(face, run.Typeface);
            Assert.Equal(3, run.Glyphs.Count);
            for (var i = 0; i < 3; i++)
            {
                face.TryMapRune(new Rune("Hi!"[i]), out var glyph);
                Assert.Equal(glyph, run.Glyphs[i].GlyphIndex);
                Assert.Equal(i, run.Glyphs[i].ClusterStart);
                Assert.Equal(1, run.Glyphs[i].ClusterLength);
            }
        }

        [Theory]
        [InlineData("Hello, world")]
        [InlineData("AVATAR To")]
        [InlineData("fi ffi fl")]
        [InlineData("")]
        public void Shape_GivesWhatTheEnginesShaperGives(string text)
        {
            var face = Face(BundledFonts.Ttf);
            var settings = ShapeSettings.Default;

            var run = Shaper.Shape(face, text, settings);

            AssertSameGlyphs(face.Face.Descriptor.Shape(text, settings), run.Glyphs);
        }

        [Fact]
        public void Shape_WithDifferentSettings_GivesWhatTheEnginesShaperGivesForThose()
        {
            var face = Face(BundledFonts.Ttf);
            var settings = new ShapeSettings(
                LigatureSet.None, CapsMode.SmallCaps, NumeralSet.TabularNums, EastAsianSet.None,
                [new FeatureSetting("ss01", 1)], Kerning: false, Language: "en", Position: SubSuperMode.Super);

            var run = Shaper.Shape(face, "Small 0123", settings);

            AssertSameGlyphs(face.Face.Descriptor.Shape("Small 0123", settings), run.Glyphs);
        }

        [Fact]
        public void Shape_RemovesInvisibleCharactersAfterTheyTookPartInShaping()
        {
            var face = Face(BundledFonts.Ttf);

            var run = Shaper.Shape(face, "a‍b", ShapeSettings.Default);

            Assert.Equal(2, run.Glyphs.Count);
            Assert.All(run.Glyphs, glyph => Assert.False(glyph.IsHiddenIgnorable));
        }

        [Fact]
        public void Shape_ForVisualOrder_ReversesTheGlyphsButNotTheirClusters()
        {
            var face = Face(BundledFonts.Ttf);

            var run = Shaper.Shape(face, "abc", new ShapeSettings(ReverseForDisplay: true));

            Assert.Equal([2, 1, 0], run.Glyphs.Select(g => g.ClusterStart));
        }

        [Fact]
        public void Advance_IsThePenTravelAlongTheRun()
        {
            var face = Face(BundledFonts.Ttf);
            var run = Shaper.Shape(face, "Hello", ShapeSettings.Default);

            var expected = run.Glyphs.Sum(g => face.GetAdvance((ushort)g.GlyphIndex) + g.XAdvanceDelta);

            Assert.Equal(expected, run.Advance);
            Assert.True(run.Advance > 0);
            Assert.Equal(0, Shaper.Shape(face, "", ShapeSettings.Default).Advance);
        }

        [Fact]
        public void ShapeSettings_HaveUsefulDefaults_ButDefaultOfTheStructIsAllZeros()
        {
            var settings = new ShapeSettings();

            Assert.Equal(LigatureSet.Default, settings.Ligatures);
            Assert.True(settings.Kerning);
            Assert.Equal(CapsMode.None, settings.Caps);
            Assert.Equal(settings, ShapeSettings.Default);

            Assert.Equal(LigatureSet.None, default(ShapeSettings).Ligatures);
            Assert.False(default(ShapeSettings).Kerning);
        }

        [Fact]
        public void GetFeatureTags_NamesTheGsubFeaturesOfACapsOrPositionMode()
        {
            Assert.Equal(["c2sc", "smcp"], Shaper.GetFeatureTags(CapsMode.AllSmallCaps).Order());
            Assert.Equal(["smcp"], Shaper.GetFeatureTags(CapsMode.SmallCaps));
            Assert.Empty(Shaper.GetFeatureTags(CapsMode.None));
            Assert.Equal(["subs"], Shaper.GetFeatureTags(SubSuperMode.Sub));
            Assert.Equal(["sups"], Shaper.GetFeatureTags(SubSuperMode.Super));
            Assert.Empty(Shaper.GetFeatureTags(SubSuperMode.None));
        }

        [Fact]
        public void Shape_RejectsAnExplicitFeatureWithNoTag()
        {
            var face = Face(BundledFonts.Ttf);
            var settings = new ShapeSettings(ExplicitFeatures: [new FeatureSetting(null!, 1)]);

            Assert.Throws<ArgumentException>(() => Shaper.Shape(face, "a", settings));
        }

        [Fact]
        public void GetFeatureTags_HandsOutSetsThatCannotBeChangedThroughACast()
        {
            var tags = Shaper.GetFeatureTags(CapsMode.SmallCaps);

            Assert.IsNotType<HashSet<string>>(tags);
            Assert.Throws<NotSupportedException>(() => ((ISet<string>)tags).Add("zzzz"));
            Assert.DoesNotContain("zzzz", Shaper.GetFeatureTags(CapsMode.SmallCaps));
        }

        [Fact]
        public void Shape_RejectsNullArguments()
        {
            var face = Face(BundledFonts.Ttf);

            Assert.Throws<ArgumentNullException>(() => Shaper.Shape(null!, "a", ShapeSettings.Default));
            Assert.Throws<ArgumentNullException>(() => Shaper.Shape(face, null!, ShapeSettings.Default));
        }

        [Fact]
        public void ArabicJoining_KnowsTheJoiningTypeOfACharacter()
        {
            Assert.Equal(ArabicJoiningType.D, ArabicJoining.TypeOf(new Rune(0x0628))); // beh
            Assert.Equal(ArabicJoiningType.R, ArabicJoining.TypeOf(0x0627)); // alef
            Assert.Equal(ArabicJoiningType.U, ArabicJoining.TypeOf('a'));
            Assert.Equal(ArabicJoiningType.T, ArabicJoining.TypeOf(0x064E)); // fatha
        }

        [Fact]
        public void ArabicJoining_Resolve_GivesEachCharacterItsPositionalForm()
        {
            // beh beh: the first joins the one after it and the second the one before it.
            Assert.Equal([ArabicJoiningForm.Init, ArabicJoiningForm.Fina], ArabicJoining.Resolve([0x0628, 0x0628]));
            Assert.Equal([ArabicJoiningForm.Init, ArabicJoiningForm.Medi, ArabicJoiningForm.Fina], ArabicJoining.Resolve([0x0628, 0x0628, 0x0628]));
            Assert.Equal([ArabicJoiningForm.Isol], ArabicJoining.Resolve([0x0628]));
            // A space joins nothing, so it splits two words and takes no form itself.
            Assert.Equal([ArabicJoiningForm.Isol, ArabicJoiningForm.None, ArabicJoiningForm.Isol], ArabicJoining.Resolve([0x0628, ' ', 0x0628]));
            Assert.Equal([ArabicJoiningForm.None, ArabicJoiningForm.None], ArabicJoining.Resolve(['a', 'b']));
            Assert.Throws<ArgumentNullException>(() => ArabicJoining.Resolve(null!));
        }

        [Theory]
        [InlineData(0x0915, UseCategory.B)] // ka
        [InlineData(0x093F, UseCategory.VPre)] // vowel sign i
        [InlineData(0x094D, UseCategory.H)] // virama
        [InlineData(0x0902, UseCategory.VMAbv)] // anusvara
        [InlineData(0x093C, UseCategory.CMBlw)] // nukta
        [InlineData('a', UseCategory.O)]
        public void UniversalShaping_ClassifiesCharactersByTheirRoleInASyllable(int codepoint, UseCategory expected)
        {
            Assert.Equal(expected, UniversalShaping.Classify(codepoint));
        }
    }
}
