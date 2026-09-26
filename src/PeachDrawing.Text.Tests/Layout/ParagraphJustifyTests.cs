using PeachDrawing.Text.Layout;
using PeachDrawing.Text.Unicode;
using PeachPDF.Tests.TestSupport;

namespace PeachDrawing.Text.Tests.Layout
{
    /// <summary>Justification: which boundaries of a line get room (<c>text-justify</c>), and that the line then fills the width.</summary>
    public class ParagraphJustifyTests
    {
        private const double Size = 20;
        private const double Width = 400;
        private static readonly Typeface Latin = Load(BundledFonts.Ttf);
        private static readonly Typeface Cjk = Load(BundledFonts.Cjk);
        private static readonly Typeface Arabic = Load(BundledFonts.Arabic);
        private static readonly Typeface Hebrew = Load(BundledFonts.Hebrew);

        private static Typeface Load(string path)
        {
            var set = new FontSet();
            var family = set.AddFile(path, new AddOptions { FamilyName = "Justify-" + Guid.NewGuid().ToString("N") });
            Assert.True(family.TryMatch(new TypefaceQuery(), out var match));
            return match.Typeface;
        }

        private static ParagraphLayout Lay(string text, TextJustify justify, Typeface? face = null, double width = Width, BaseDirection direction = BaseDirection.Ltr, RunStyle? run = null)
        {
            var style = new ParagraphStyle { Align = TextAlign.Justify, TextJustify = justify, Direction = direction, AlignLast = TextAlign.Justify };
            return new ParagraphBuilder(run ?? new RunStyle(face ?? Latin, Size)).SetStyle(style).AddText(text).Build().Layout(width);
        }

        private static double Natural(string text, Typeface? face = null) => TextRuler.WidthOf(face ?? Latin, Size, text);

        /// <summary>The distances by which the glyph advances of a line exceed the font's own, in drawing order.</summary>
        private static List<double> Extras(ParagraphLayout layout, Typeface face)
        {
            var extras = new List<double>();
            foreach (var run in layout.Lines[0].Runs)
            {
                double scale = run.Style.Size / face.Metrics.UnitsPerEm;
                for (int i = 0; i < run.Glyphs.Glyphs.Count; i++)
                {
                    var glyph = run.Glyphs.Glyphs[i];
                    double natural = (face.GetAdvance((ushort)glyph.GlyphIndex) + glyph.XAdvanceDelta) * scale;
                    extras.Add(run.GetGlyphAdvance(i) - natural);
                }
            }

            return extras;
        }

        [Fact]
        public void InterCharacter_SharesTheRoomBetweenEveryPairOfCharacters()
        {
            var layout = Lay("abcd", TextJustify.InterCharacter);

            var line = layout.Lines[0];
            Assert.Equal(Width, line.Width, 3);
            var extras = Extras(layout, Latin);
            double share = (Width - Natural("abcd")) / 3;
            Assert.Equal(share, extras[0], 3);
            Assert.Equal(share, extras[1], 3);
            Assert.Equal(share, extras[2], 3);
            Assert.Equal(0, extras[3], 6);
        }

        [Fact]
        public void InterCharacter_AddsNothingAfterTheLastCharacter()
        {
            var extras = Extras(Lay("abcd", TextJustify.InterCharacter), Latin);

            Assert.Equal(0, extras[^1], 6);
            Assert.All(extras.Take(3), e => Assert.True(e > 1));
        }

        [Fact]
        public void InterCharacter_AlsoWidensSpaces_OnceEach()
        {
            var layout = Lay("a b", TextJustify.InterCharacter);

            // Boundaries: a|space and space|b.
            var extras = Extras(layout, Latin);
            Assert.Equal(2, extras.Count(e => e > 1));
            Assert.Equal(Width, layout.Lines[0].Width, 3);
        }

        [Fact]
        public void InterWord_LeavesAWordWithNoSpaceAlone()
        {
            var layout = Lay("abcd", TextJustify.InterWord);

            var line = layout.Lines[0];
            Assert.Equal(Natural("abcd"), line.Width, 3);
            Assert.All(Extras(layout, Latin), e => Assert.Equal(0, e, 6));
        }

        [Fact]
        public void InterWord_WidensTheSpacesOnly()
        {
            var layout = Lay("ab cd ef", TextJustify.InterWord);

            Assert.Equal(Width, layout.Lines[0].Width, 3);
            Assert.Equal(2, Extras(layout, Latin).Count(e => e > 1));
        }

        [Fact]
        public void Auto_TreatsLatinLikeInterWord()
        {
            var text = "ab cd ef";
            var auto = Lay(text, TextJustify.Auto);
            var word = Lay(text, TextJustify.InterWord);

            Assert.Equal(word.Lines[0].Width, auto.Lines[0].Width, 6);
            Assert.Equal(Extras(word, Latin), Extras(auto, Latin));
            Assert.Equal(Natural("abcd"), Lay("abcd", TextJustify.Auto).Lines[0].Width, 3);
        }

        [Fact]
        public void Auto_JustifiesRunsOfAScriptWrittenWithoutSpaces()
        {
            var text = "日本語";
            var layout = Lay(text, TextJustify.Auto, Cjk);

            Assert.Equal(Width, layout.Lines[0].Width, 3);
            var extras = Extras(layout, Cjk);
            Assert.Equal(2, extras.Count(e => e > 1));
            Assert.Equal(0, extras[^1], 6);
        }

        [Fact]
        public void Auto_GivesRoomAtTheBoundaryBetweenLatinAndIdeograph_ButNotWithinLatin()
        {
            var layout = Lay("ab日cd", TextJustify.Auto, Cjk);

            var extras = Extras(layout, Cjk);
            // a|b none, b|ideograph yes, ideograph|c yes, c|d none, d last.
            Assert.Equal(2, extras.Count(e => e > 1));
            Assert.True(extras[1] > 1);
            Assert.True(extras[2] > 1);
        }

        [Fact]
        public void None_AlignsAsStart()
        {
            var layout = Lay("ab cd ef", TextJustify.None);

            var line = layout.Lines[0];
            Assert.Equal(0, line.Left, 6);
            Assert.Equal(Natural("ab cd ef"), line.Width, 3);
        }

        [Fact]
        public void AJustifiedLine_KeepsTheAdvancesAddingUpToItsWidth()
        {
            var layout = Lay("ab cd ef", TextJustify.InterCharacter);

            var run = Assert.Single(layout.Lines[0].Runs);
            double total = 0;
            for (int i = 0; i < run.Glyphs.Glyphs.Count; i++)
            {
                total += run.GetGlyphAdvance(i);
            }

            Assert.Equal(Width, total, 3);
            Assert.Equal(Width, run.GetCaretX(run.Range.End) - run.X, 3);
        }

        [Fact]
        public void TheRoomIsInAdditionToTheLetterSpacing()
        {
            var layout = Lay("abcd", TextJustify.InterCharacter, run: new RunStyle(Latin, Size, LetterSpacing: 2));

            Assert.Equal(Width, layout.Lines[0].Width, 3);
            var extras = Extras(layout, Latin);
            Assert.Equal(extras[0], extras[1], 3);
            Assert.Equal(2, extras[3], 3);
        }

        [Fact]
        public void ATab_IsAWall_NothingIsAddedNextToIt()
        {
            var layout = Lay("a\tb", TextJustify.InterCharacter);

            Assert.Equal(Natural("a") + Natural("b") + (layout.Lines[0].Runs[1].Width), layout.Lines[0].Width, 3);
        }

        [Fact]
        public void ALineWithNothingToWiden_IsLeftAsItIs()
        {
            var layout = Lay("a", TextJustify.InterCharacter);

            Assert.Equal(Natural("a"), layout.Lines[0].Width, 3);
        }

        [Fact]
        public void InterCharacter_DoesNotPullJoinedArabicLettersApart()
        {
            // BEH BEH BEH BEH: joined, so every boundary is a join.
            var text = "بببب";
            var layout = Lay(text, TextJustify.InterCharacter, Arabic, direction: BaseDirection.Rtl);

            var unjustified = Lay(text, TextJustify.InterWord, Arabic, direction: BaseDirection.Rtl);
            Assert.Equal(unjustified.Lines[0].Width, layout.Lines[0].Width, 3);
            Assert.True(layout.Lines[0].Width < Width);
        }

        [Fact]
        public void InterCharacter_WorksRightToLeft()
        {
            var text = "אבגד";
            var layout = Lay(text, TextJustify.InterCharacter, Hebrew, direction: BaseDirection.Rtl);

            var line = layout.Lines[0];
            Assert.Equal(Width, line.Width, 3);
            Assert.Equal(0, line.Left, 3);
            Assert.Equal(3, Extras(layout, Hebrew).Count(e => e > 1));
        }

        [Fact]
        public void LetterSpacing_TurnsOffOptionalLigatures()
        {
            var plain = new ParagraphBuilder(new RunStyle(Latin, Size)).AddText("ff").Build().Layout(1000);
            var spaced = new ParagraphBuilder(new RunStyle(Latin, Size, LetterSpacing: 1)).AddText("ff").Build().Layout(1000);

            int plainGlyphs = plain.Lines[0].Runs.Sum(r => r.Glyphs.Glyphs.Count);
            int spacedGlyphs = spaced.Lines[0].Runs.Sum(r => r.Glyphs.Glyphs.Count);
            Assert.Equal(2, spacedGlyphs);
            Assert.Equal(1, plainGlyphs);
        }
    }
}
