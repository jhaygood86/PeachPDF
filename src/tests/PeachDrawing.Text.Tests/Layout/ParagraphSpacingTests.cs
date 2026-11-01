using PeachDrawing.Text.Layout;
using PeachDrawing.Text.Unicode;
using PeachPDF.Tests.TestSupport;

namespace PeachDrawing.Text.Tests.Layout
{
    /// <summary>Letter and word spacing, and justification: extra distance a paragraph puts between glyphs and at spaces.</summary>
    public class ParagraphSpacingTests
    {
        private const double Size = 20;
        private static readonly Typeface Face = Load();

        private static Typeface Load()
        {
            var set = new FontSet();
            var family = set.AddFile(BundledFonts.Ttf, new AddOptions { FamilyName = "Spacing-" + Guid.NewGuid().ToString("N") });
            Assert.True(family.TryMatch(new TypefaceQuery(), out var match));
            return match.Typeface;
        }

        private static double Advance(string text) => TextRuler.WidthOf(Face, Size, text);

        private static ParagraphLayout Lay(string text, double width, RunStyle? style = null, ParagraphStyle? paragraph = null)
        {
            var builder = new ParagraphBuilder(style ?? new RunStyle(Face, Size));
            if (paragraph is { } given)
            {
                builder.SetStyle(given);
            }

            return builder.AddText(text).Build().Layout(width);
        }

        // ---- letter and word spacing -----------------------------------------------------------------------------------------------

        [Fact]
        public void LetterSpacing_AddsToEveryGlyph()
        {
            var layout = Lay("Hello", 1000, new RunStyle(Face, Size, LetterSpacing: 3));

            Assert.Equal(Advance("Hello") + 5 * 3, layout.Lines[0].Width, 3);
        }

        [Fact]
        public void WordSpacing_AddsToEverySpace_AndNoBreakSpace()
        {
            var layout = Lay("a b c", 1000, new RunStyle(Face, Size, WordSpacing: 5));

            Assert.Equal(Advance("a b c") + 2 * 5, layout.Lines[0].Width, 3);
        }

        [Fact]
        public void TheSpacing_IsPartOfWhereLinesBreak()
        {
            var plain = Lay("alpha beta gamma", Advance("alpha beta") + 1);
            var spaced = Lay("alpha beta gamma", Advance("alpha beta") + 1, new RunStyle(Face, Size, LetterSpacing: 4));

            Assert.True(spaced.Lines.Count > plain.Lines.Count);
        }

        [Fact]
        public void TheGlyphAdvances_AddUpToTheRunsWidth_IncludingTheSpacing()
        {
            var layout = Lay("a b c", 1000, new RunStyle(Face, Size, LetterSpacing: 2, WordSpacing: 6));
            var run = Assert.Single(layout.Lines[0].Runs);

            double total = 0;
            for (int i = 0; i < run.Glyphs.Glyphs.Count; i++)
            {
                total += run.GetGlyphAdvance(i);
            }

            Assert.Equal(run.Width, total, 3);
            Assert.Throws<ArgumentOutOfRangeException>(() => run.GetGlyphAdvance(run.Glyphs.Glyphs.Count));
        }

        [Fact]
        public void TheCaretsFollowTheSpacing()
        {
            var layout = Lay("ab", 1000, new RunStyle(Face, Size, LetterSpacing: 10));
            var run = layout.Lines[0].Runs[0];

            Assert.Equal(TextRuler.WidthOf(Face, Size, "a") + 10, run.GetCaretX(1), 3);
        }

        [Fact]
        public void LetterSpacing_IsAddedOncePerCluster_NotPerGlyph()
        {
            // A base with a combining mark is two glyphs for one character: the spacing goes after the pair.
            var plain = Lay("e\u0301x", 1000);
            var spaced = Lay("e\u0301x", 1000, new RunStyle(Face, Size, LetterSpacing: 5));

            Assert.Equal(plain.Lines[0].Width + 2 * 5, spaced.Lines[0].Width, 3);
        }

        [Fact]
        public void ASpaceWithAMark_GetsItsWordSpacingOnce()
        {
            var plain = Lay("a \u0301b", 1000);
            var spaced = Lay("a \u0301b", 1000, new RunStyle(Face, Size, WordSpacing: 7));

            Assert.Equal(plain.Lines[0].Width + 7, spaced.Lines[0].Width, 3);
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void ASpacingThatIsNotFinite_IsRejected(double spacing)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ParagraphBuilder(new RunStyle(Face, Size, LetterSpacing: spacing)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ParagraphBuilder(new RunStyle(Face, Size, WordSpacing: spacing)));
        }

        // ---- justification ---------------------------------------------------------------------------------------------------------

        [Fact]
        public void JustifiedLines_FillTheWidth_ExceptTheLast()
        {
            double width = Advance("one two six") + 15;
            var layout = Lay("one two six ten one two six ten one two six ten one", width, paragraph: new ParagraphStyle { Align = TextAlign.Justify });

            Assert.True(layout.Lines.Count > 2);
            foreach (var line in layout.Lines.Take(layout.Lines.Count - 1))
            {
                Assert.Equal(width, line.Width, 3);
                Assert.Equal(0, line.Left, 3);
                var last = line.Runs[^1];
                Assert.Equal(width, last.X + last.Width, 3);
            }

            var final = layout.Lines[^1];
            Assert.True(final.Width < width);
            Assert.Equal(0, final.Left, 3);
        }

        [Fact]
        public void AJustifiedLineWithNoSpaces_IsNotChanged()
        {
            var layout = Lay("Supercalifragilistic", 1000, paragraph: new ParagraphStyle { Align = TextAlign.Justify, OverflowWrap = OverflowWrap.BreakWord });

            Assert.Equal(Advance("Supercalifragilistic"), layout.Lines[0].Width, 3);
        }

        [Fact]
        public void AForcedBreak_EndsAJustifiedLineAtItsNaturalWidth()
        {
            var layout = Lay("alpha beta\ngamma delta", 1000, paragraph: new ParagraphStyle { Align = TextAlign.Justify });

            Assert.Equal(Advance("alpha beta"), layout.Lines[0].Width, 3);
        }

        [Fact]
        public void TheLastLineAlignment_CanBeSet()
        {
            double width = Advance("alpha beta") + 20;
            var layout = Lay("alpha beta gamma delta", width, paragraph: new ParagraphStyle { Align = TextAlign.Justify, AlignLast = TextAlign.Right });

            var final = layout.Lines[^1];
            Assert.Equal(width - final.Width, final.Left, 3);
        }

        [Fact]
        public void TheSpacesAreWidenedEqually()
        {
            double width = Advance("alpha beta gamma") + 30;
            var layout = Lay("alpha beta gamma delta", width, paragraph: new ParagraphStyle { Align = TextAlign.Justify });

            var run = layout.Lines[0].Runs[0];
            var spaces = Enumerable.Range(0, run.Glyphs.Glyphs.Count)
                .Where(i => layout.Paragraph.Text[run.Range.Start + run.Glyphs.Glyphs[i].ClusterStart] == ' ')
                .Select(run.GetGlyphAdvance).ToList();

            Assert.Equal(2, spaces.Count);
            Assert.Equal(spaces[0], spaces[1], 6);
            Assert.True(spaces[0] > TextRuler.WidthOf(Face, Size, " ") + 1);
        }

        [Fact]
        public void JustificationOfARightToLeftParagraph_FillsTheWidthToo()
        {
            var hebrew = Load();   // any face: the paragraph direction comes from the text and the style
            double width = Advance("aaaa bbbb") + 25;
            var layout = Lay("aaaa bbbb cccc dddd eeee", width, new RunStyle(hebrew, Size),
                new ParagraphStyle { Align = TextAlign.Justify, Direction = BaseDirection.Rtl });

            Assert.True(layout.Lines.Count > 1);
            Assert.Equal(width, layout.Lines[0].Width, 3);
        }
    }
}
