using PeachDrawing.Text.Layout;
using PeachPDF.Tests.TestSupport;

namespace PeachDrawing.Text.Tests.Layout
{
    /// <summary>
    /// A paragraph whose run has a fallback: a character the run's typeface cannot draw is set in another face, with the marks that follow it.
    /// </summary>
    public class ParagraphFallbackTests
    {
        private const string Hebrew = "אבג";

        private sealed class Fonts
        {
            public FontSet Set { get; } = new();
            public Typeface Latin { get; }
            public Typeface HebrewFace { get; }

            /// <summary>A fallback that always answers with one of the two faces, so the tests do not depend on what is installed.</summary>
            public Func<System.Text.Rune, Typeface?> UseHebrew => _ => HebrewFace;

            public Func<System.Text.Rune, Typeface?> UseLatin => _ => Latin;

            public Fonts()
            {
                var latin = Set.AddFile(BundledFonts.Ttf, new AddOptions { FamilyName = "FbLatin-" + Guid.NewGuid().ToString("N") });
                var hebrew = Set.AddFile(BundledFonts.Hebrew, new AddOptions { FamilyName = "FbHebrew-" + Guid.NewGuid().ToString("N") });
                Assert.True(latin.TryMatch(new TypefaceQuery(), out var l));
                Assert.True(hebrew.TryMatch(new TypefaceQuery(), out var h));
                Latin = l.Typeface;
                HebrewFace = h.Typeface;
            }
        }

        private static ParagraphLayout Lay(RunStyle style, string text) =>
            new ParagraphBuilder(style).AddText(text).Build().Layout(1000);

        [Fact]
        public void ACharacterTheFaceCannotDraw_IsSetInTheFallback()
        {
            var fonts = new Fonts();
            var layout = Lay(new RunStyle(fonts.Latin, 20, null, fonts.UseHebrew), "abc " + Hebrew);

            var runs = layout.Lines[0].Runs;
            var hebrewRun = runs.Single(r => layout.Paragraph.Text.Substring(r.Range.Start, r.Range.Length).Contains('א'));

            Assert.Equal(fonts.HebrewFace.FamilyName, hebrewRun.Style.Typeface.FamilyName);
            Assert.Equal(fonts.Latin.FamilyName, runs[0].Style.Typeface.FamilyName);
            Assert.Equal(20, hebrewRun.Style.Size);
        }

        [Fact]
        public void WithoutAFallback_TheRunKeepsItsFace()
        {
            var fonts = new Fonts();
            var layout = Lay(new RunStyle(fonts.Latin, 20), "abc " + Hebrew);

            Assert.All(layout.Lines[0].Runs, r => Assert.Equal(fonts.Latin.FamilyName, r.Style.Typeface.FamilyName));
        }

        [Fact]
        public void AFallbackThatHasNothing_LeavesTheCharacterInTheRunsFace()
        {
            var fonts = new Fonts();
            var layout = Lay(new RunStyle(fonts.Latin, 20, null, _ => null), Hebrew);

            Assert.All(layout.Lines[0].Runs, r => Assert.Equal(fonts.Latin.FamilyName, r.Style.Typeface.FamilyName));
        }

        [Fact]
        public void TheWidthFollowsTheFallbackFace()
        {
            var fonts = new Fonts();
            var withFallback = Lay(new RunStyle(fonts.Latin, 20, null, fonts.UseHebrew), Hebrew);
            var without = Lay(new RunStyle(fonts.Latin, 20), Hebrew);

            Assert.Equal(TextRuler.WidthOf(fonts.HebrewFace, 20, Hebrew), withFallback.Lines[0].Width, 3);
            Assert.NotEqual(without.Lines[0].Width, withFallback.Lines[0].Width);
        }

        [Fact]
        public void TheMarksThatFollowACharacter_StayInItsFace()
        {
            var fonts = new Fonts();
            // Hebrew primary, so the Latin base needs the fallback; the combining acute must not be split off into another run.
            var layout = Lay(new RunStyle(fonts.HebrewFace, 20, null, fonts.UseLatin), "é");

            var run = Assert.Single(layout.Lines[0].Runs);
            Assert.Equal(new TextRange(0, 2), run.Range);
        }

        [Fact]
        public void SpacesAndNewlines_NeverAskTheFallback()
        {
            var fonts = new Fonts();
            var asked = new List<int>();
            var style = new RunStyle(fonts.HebrewFace, 20, null, rune => { asked.Add(rune.Value); return null; });

            Lay(style, " \n\t");

            Assert.Empty(asked);
        }

        [Theory]
        [InlineData("\u200B")]      // zero width space
        [InlineData("\u00AD")]      // soft hyphen
        [InlineData("\u200E")]      // left-to-right mark
        [InlineData("\u2060")]      // word joiner
        public void FormatCharacters_NeverAskTheFallback(string invisible)
        {
            var fonts = new Fonts();
            var asked = new List<int>();
            var style = new RunStyle(fonts.Latin, 20, null, rune => { asked.Add(rune.Value); return fonts.HebrewFace; });

            var layout = Lay(style, "a" + invisible + "b");

            Assert.Empty(asked);
            Assert.All(layout.Lines[0].Runs, r => Assert.Equal(fonts.Latin.FamilyName, r.Style.Typeface.FamilyName));
        }

        [Fact]
        public void EachUncoveredCluster_AsksOnce_WithItsFirstCodePoint()
        {
            var fonts = new Fonts();
            var asked = new List<int>();
            var style = new RunStyle(fonts.HebrewFace, 20, null, rune => { asked.Add(rune.Value); return null; });

            Lay(style, "éx");

            Assert.Equal([0x65, 0x78], asked);
        }

        [Fact]
        public void CreateFallback_AnswersForACoveredCharacter_AndSetsTheQueryOnTheMatch()
        {
            var fonts = new Fonts();
            var fallback = fonts.Set.CreateFallback(new TypefaceQuery(700));

            var face = fallback(new System.Text.Rune(0x05D0));

            Assert.NotNull(face);
            Assert.True(face!.TryMapRune(new System.Text.Rune(0x05D0), out _));
        }
    }
}
