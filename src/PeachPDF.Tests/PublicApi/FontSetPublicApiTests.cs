using PeachDrawing.Text;
using PeachDrawing.Text.Internal.Fonts;
using PeachDrawing.Text.Unicode;
using PeachPDF.Tests.TestSupport;
using System.Text;

namespace PeachPDF.Tests.PublicApi
{
    /// <summary>
    /// <see cref="FontSet"/> and the types it hands out, exercised as a consumer outside the assembly would use them: add fonts,
    /// look families up, match, and ask what has to be faked.
    /// </summary>
    public class FontSetPublicApiTests
    {
        private static string UniqueName(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N");

        private static (FontSet Set, TypefaceFamily Family) SetWith(string path, AddOptions? options = null)
        {
            var set = new FontSet();
            return (set, set.AddFile(path, options));
        }

        [Fact]
        public void AddFile_WithoutAName_RegistersTheFamilyTheFileDeclares()
        {
            var (set, family) = SetWith(BundledFonts.Ttf);

            Assert.Equal("Source Sans 3", family.Name, ignoreCase: true);
            Assert.True(set.TryFindFamily("SOURCE SANS 3", out var found));
            Assert.Equal(family.Name, found.Name);
        }

        [Fact]
        public void AddFile_WithAName_RegistersTheFamilyUnderThatName()
        {
            var name = UniqueName("Renamed");
            var (set, family) = SetWith(BundledFonts.Ttf, new AddOptions { FamilyName = name });

            Assert.Equal(name, family.Name);
            Assert.True(set.TryFindFamily(name.ToUpperInvariant(), out _));
        }

        [Fact]
        public void TryFindFamily_ForAnUnknownName_ReportsNothing()
        {
            var set = new FontSet();

            Assert.False(set.TryFindFamily(UniqueName("NoSuchFamily"), out var family));
            Assert.Null(family);
        }

        [Fact]
        public void AddStream_ReadsTheStreamAndLeavesItOpen()
        {
            var set = new FontSet();
            using var stream = File.OpenRead(BundledFonts.Ttf);

            var family = set.AddStream(stream, new AddOptions { FamilyName = UniqueName("Streamed") });

            Assert.True(stream.CanRead);
            Assert.True(set.TryFindFamily(family.Name, out _));
        }

        [Fact]
        public void AddData_RecognisesWoff2ByItsContent()
        {
            var set = new FontSet();

            var family = set.AddData(File.ReadAllBytes(BundledFonts.Woff2), new AddOptions { FamilyName = UniqueName("Woff2") });

            Assert.True(family.TryMatch(new TypefaceQuery(), out var match));
            Assert.NotEmpty(match.Typeface.FamilyName);
        }

        [Fact]
        public void AddData_WithDataThatIsNotAFont_ThrowsTypefaceFormatException()
        {
            var set = new FontSet();

            var exception = Assert.Throws<TypefaceFormatException>(() => set.AddData(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));

            Assert.NotNull(exception.InnerException);
        }

        [Fact]
        public void Add_RejectsNullArguments()
        {
            var set = new FontSet();

            Assert.Throws<ArgumentNullException>(() => set.AddStream(null!));
            Assert.Throws<ArgumentNullException>(() => set.AddFile(null!));
            Assert.Throws<ArgumentNullException>(() => set.TryFindFamily(null!, out _));
            Assert.Throws<ArgumentNullException>(() => set.HasExplicitRanges(null!));
            Assert.Throws<ArgumentNullException>(() => set.MatchOrFallback(null!, new TypefaceQuery()));
            Assert.Throws<ArgumentNullException>(() => set.TryGetFontData(null!, out _));
        }

        [Fact]
        public void TryMatch_OfARegularOnlyFamily_SaysWhatHasToBeFaked()
        {
            var (_, family) = SetWith(BundledFonts.Ttf, new AddOptions { FamilyName = UniqueName("RegularOnly") });

            Assert.True(family.TryMatch(new TypefaceQuery(), out var regular));
            Assert.Equal(SyntheticStyle.None, regular.Synthesis);

            Assert.True(family.TryMatch(new TypefaceQuery(TypefaceQuery.BoldWeight), out var bold));
            Assert.Equal(SyntheticStyle.Bold, bold.Synthesis);

            Assert.True(family.TryMatch(new TypefaceQuery(IsItalic: true), out var italic));
            Assert.Equal(SyntheticStyle.Italic, italic.Synthesis);

            Assert.True(family.TryMatch(new TypefaceQuery(TypefaceQuery.BoldWeight, IsItalic: true), out var both));
            Assert.Equal(SyntheticStyle.BoldItalic, both.Synthesis);
        }

        [Fact]
        public void TryMatch_TakesTheNearestWeightAmongTheFacesOfAFamily()
        {
            var name = UniqueName("TwoWeights");
            var set = new FontSet();
            var family = set.AddFile(BundledFonts.Ttf, new AddOptions { FamilyName = name, Weight = 300 });
            set.AddFile(BundledFonts.Otf, new AddOptions { FamilyName = name, Weight = 700 });

            Assert.True(family.TryMatch(new TypefaceQuery(Weight: 800), out var heavy));
            Assert.True(family.TryMatch(new TypefaceQuery(Weight: 200), out var light));

            Assert.NotEqual(heavy.Typeface, light.Typeface);
            // The face declared at 700 is heavy enough for a request for 800 that nothing has to be faked.
            Assert.Equal(SyntheticStyle.None, heavy.Synthesis);
            Assert.Equal("Source Code Pro", heavy.Typeface.FamilyName);
            Assert.Equal("Source Sans 3", light.Typeface.FamilyName);
        }

        [Fact]
        public void TryMatch_WithACharacterToCover_OnlyAcceptsAFaceThatCoversIt()
        {
            var range = new RuneInterval(new Rune('A'), new Rune('Z'));
            var (_, family) = SetWith(BundledFonts.Ttf, new AddOptions { FamilyName = UniqueName("Ranged"), UnicodeRanges = [range] });

            Assert.True(family.TryMatch(new TypefaceQuery(MustCover: new Rune('B')), out _));
            Assert.False(family.TryMatch(new TypefaceQuery(MustCover: new Rune('b')), out var none));
            Assert.Equal(default, none);
        }

        [Fact]
        public void TryMatch_WithoutARange_UsesWhatTheFontCoversForMustCover()
        {
            var (_, family) = SetWith(BundledFonts.Ttf, new AddOptions { FamilyName = UniqueName("Cmap") });

            Assert.True(family.TryMatch(new TypefaceQuery(MustCover: new Rune('a')), out _));
            Assert.False(family.TryMatch(new TypefaceQuery(MustCover: new Rune(0x10FFFF)), out _));
        }

        [Fact]
        public void HasExplicitRanges_IsTrueOnlyForAFamilyThatDeclaredThem()
        {
            var set = new FontSet();
            var ranged = set.AddFile(BundledFonts.Ttf, new AddOptions
            {
                FamilyName = UniqueName("HasRanges"),
                UnicodeRanges = [new RuneInterval(new Rune('a'), new Rune('z'))]
            });
            var plain = set.AddFile(BundledFonts.Ttf, new AddOptions { FamilyName = UniqueName("NoRanges") });

            Assert.True(set.HasExplicitRanges(ranged.Name));
            Assert.False(set.HasExplicitRanges(plain.Name));
            Assert.False(set.HasExplicitRanges(UniqueName("Unknown")));
        }

        [Fact]
        public void MatchOrFallback_ForAKnownFamily_MatchesIt()
        {
            var (set, family) = SetWith(BundledFonts.Ttf, new AddOptions { FamilyName = UniqueName("Known") });

            var match = set.MatchOrFallback(family.Name, new TypefaceQuery());

            Assert.Equal("Source Sans 3", match.Typeface.FamilyName);
            Assert.Equal(SyntheticStyle.None, match.Synthesis);
        }

        [Fact]
        public void MatchOrFallback_ForAnUnknownFamily_StillAnswersWithSomeFace()
        {
            var set = new FontSet();
            set.AddFile(BundledFonts.Ttf, new AddOptions { FamilyName = UniqueName("Anything") });

            var match = set.MatchOrFallback(UniqueName("NoSuchFamily"), new TypefaceQuery());

            Assert.NotEmpty(match.Typeface.FamilyName);
        }

        [Fact]
        public void MatchOrFallback_WithACharacterToCover_IsRejected()
        {
            var set = new FontSet();

            Assert.Throws<ArgumentException>(() => set.MatchOrFallback("anything", new TypefaceQuery(MustCover: new Rune('a'))));
        }

        [Fact]
        public void TryFindCoveringFamily_FindsTheFamilyThatCoversACharacterNoOtherDoes()
        {
            var set = new FontSet();
            var name = UniqueName("PlaneSixteen");
            set.AddFile(BundledFonts.Ttf, new AddOptions
            {
                FamilyName = name,
                UnicodeRanges = [new RuneInterval(new Rune(0x10FF00), new Rune(0x10FFF0))]
            });

            Assert.True(set.TryFindCoveringFamily(new Rune(0x10FF01), EmojiPresentation.NoPreference, out var family));
            Assert.Equal(name, family.Name);
            Assert.False(set.TryFindCoveringFamily(new Rune(0x10FFFF), EmojiPresentation.NoPreference, out var none));
            Assert.Null(none);
        }

        [Fact]
        public void TryGetFontData_ServesAFontByItsOwnName()
        {
            var set = new FontSet();
            set.AddFile(BundledFonts.Ttf, new AddOptions { FamilyName = UniqueName("Local") });
            var ownName = TtfFontDescription.LoadDescription(BundledFonts.Ttf).FontNameInvariantCulture;

            Assert.True(set.TryGetFontData(ownName, out var data));
            Assert.NotEmpty(data);
            Assert.False(set.TryGetFontData(UniqueName("NoSuchFont"), out var none));
            Assert.Empty(none);
        }

        [Fact]
        public void ResolveGeneric_OnlyAnswersWithAFamilyTheCallerCanUse()
        {
            var set = new FontSet();

            foreach (var generic in Enum.GetValues<GenericFamily>())
            {
                Assert.Null(set.ResolveGeneric(generic, _ => false));
            }

            // Math has a list of candidates on every platform, so the first one answers when everything is available.
            Assert.NotNull(set.ResolveGeneric(GenericFamily.Math, _ => true));
        }

        [Fact]
        public void ResolveGeneric_ByDefault_ChecksTheFamiliesOfTheSet()
        {
            var set = new FontSet();
            var unusable = set.ResolveGeneric(GenericFamily.Fantasy);

            // Whatever the platform answers, it is a family the set has.
            Assert.True(unusable is null || set.TryFindFamily(unusable, out _));
        }

        [Fact]
        public void InstalledFamilyNames_AreNonEmptyNames()
        {
            Assert.All(FontSet.InstalledFamilyNames, name => Assert.False(string.IsNullOrEmpty(name)));
        }

        [Fact]
        public void Typeface_DescribesTheFaceAndComparesByFontData()
        {
            var (_, family) = SetWith(BundledFonts.Ttf, new AddOptions { FamilyName = UniqueName("Identity") });
            var (_, other) = SetWith(BundledFonts.Otf, new AddOptions { FamilyName = UniqueName("Identity") });

            Assert.True(family.TryMatch(new TypefaceQuery(), out var regular));
            Assert.True(family.TryMatch(new TypefaceQuery(TypefaceQuery.BoldWeight), out var bold));
            Assert.True(other.TryMatch(new TypefaceQuery(), out var different));

            // The same font data, matched by two different queries.
            Assert.Equal(regular.Typeface, bold.Typeface);
            Assert.Equal(regular.Typeface.GetHashCode(), bold.Typeface.GetHashCode());
            Assert.True(regular.Typeface.Equals((object)bold.Typeface));
            Assert.NotEqual(regular.Typeface, different.Typeface);
            Assert.False(regular.Typeface.Equals(null));
            Assert.False(regular.Typeface.Equals((object?)"a string"));

            Assert.Equal("Source Sans 3", regular.Typeface.FamilyName);
            Assert.False(string.IsNullOrEmpty(regular.Typeface.StyleName));
            Assert.False(regular.Typeface.IsBold);
            Assert.False(regular.Typeface.IsItalic);
            Assert.False(string.IsNullOrEmpty(regular.Typeface.ToString()));
        }

        [Fact]
        public void TypefaceQuery_DefaultsToNormalWeightAndWidth()
        {
            var query = new TypefaceQuery();

            Assert.Equal(TypefaceQuery.NormalWeight, query.Weight);
            Assert.Equal(TypefaceQuery.NormalWidth, query.Width);
            Assert.False(query.IsItalic);
            Assert.Null(query.MustCover);
        }

        [Fact]
        public void TypefaceFormatException_CarriesItsMessageAndCause()
        {
            var cause = new InvalidOperationException("inner");

            Assert.False(string.IsNullOrEmpty(new TypefaceFormatException().Message));
            Assert.Equal("m", new TypefaceFormatException("m").Message);
            Assert.Same(cause, new TypefaceFormatException("m", cause).InnerException);
        }
    }
}
