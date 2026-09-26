using PeachDrawing.Text.Layout;
using PeachDrawing.Text.Unicode;
using PeachPDF.Tests.TestSupport;
using System.Diagnostics;

namespace PeachDrawing.Text.Tests.Layout
{
    /// <summary><c>text-indent</c> and tab stops: room taken from the start of a line, and a tab that reaches the next stop.</summary>
    public class ParagraphIndentAndTabTests
    {
        private const double Size = 20;
        private static readonly Typeface Face = Load();
        private static readonly Typeface HebrewFace = LoadHebrew();

        private static Typeface Load()
        {
            var set = new FontSet();
            var family = set.AddFile(BundledFonts.Ttf, new AddOptions { FamilyName = "IndentTab-" + Guid.NewGuid().ToString("N") });
            Assert.True(family.TryMatch(new TypefaceQuery(), out var match));
            return match.Typeface;
        }

        private static Typeface LoadHebrew()
        {
            var set = new FontSet();
            var family = set.AddFile(BundledFonts.Hebrew, new AddOptions { FamilyName = "IndentTabHebrew-" + Guid.NewGuid().ToString("N") });
            Assert.True(family.TryMatch(new TypefaceQuery(), out var match));
            return match.Typeface;
        }

        private static double Advance(string text) => TextRuler.WidthOf(Face, Size, text);

        private static double Space => Advance(" ");

        private static Paragraph Build(string text, ParagraphStyle? style = null, RunStyle? run = null)
        {
            var builder = new ParagraphBuilder(run ?? new RunStyle(Face, Size));
            if (style is { } given)
            {
                builder.SetStyle(given);
            }

            return builder.AddText(text).Build();
        }

        private static ParagraphLayout Lay(string text, double width, ParagraphStyle? style = null, RunStyle? run = null) => Build(text, style, run).Layout(width);

        // ---- text-indent -----------------------------------------------------------------------------------------------------------

        [Fact]
        public void TheFirstLine_IsMovedInByTheIndent_AndTheOthersAreNot()
        {
            var layout = Lay("alpha beta gamma delta", Advance("alpha beta") + 60, new ParagraphStyle { TextIndent = new TextIndent(30) });

            Assert.True(layout.Lines.Count > 1);
            Assert.Equal(30, layout.Lines[0].Left, 6);
            Assert.Equal(30, layout.Lines[0].Runs[0].X, 6);
            Assert.All(layout.Lines.Skip(1), l => Assert.Equal(0, l.Left, 6));
        }

        [Fact]
        public void TheIndent_TakesRoomFromTheFirstLine_SoItBreaksEarlier()
        {
            double width = Advance("alpha beta");
            var plain = Lay("alpha beta gamma", width + 1);
            var indented = Lay("alpha beta gamma", width + 1, new ParagraphStyle { TextIndent = new TextIndent(40) });

            Assert.Equal(new TextRange(0, 11), plain.Lines[0].Range);
            Assert.Equal(new TextRange(0, 6), indented.Lines[0].Range);
            Assert.All(indented.Lines, l => Assert.True(l.Left + l.Width <= width + 1 + 1e-6));
        }

        [Fact]
        public void Hanging_IndentsEveryLineButTheFirst()
        {
            var layout = Lay("alpha beta gamma delta epsilon zeta eta theta", Advance("alpha beta") + 50, new ParagraphStyle { TextIndent = new TextIndent(25, Hanging: true) });

            Assert.True(layout.Lines.Count > 2);
            Assert.Equal(0, layout.Lines[0].Left, 6);
            Assert.All(layout.Lines.Skip(1), l => Assert.Equal(25, l.Left, 6));
        }

        [Fact]
        public void EachLine_IndentsTheLineAfterAForcedBreak_Too()
        {
            var style = new ParagraphStyle { TextIndent = new TextIndent(15, EachLine: true) };
            var layout = Lay("one two\nthree four five six seven", Advance("three four") + 40, style);

            Assert.Equal(15, layout.Lines[0].Left, 6);
            Assert.Equal(15, layout.Lines[1].Left, 6);
            Assert.Equal(LineEnd.Forced, layout.Lines[0].End);
            Assert.All(layout.Lines.Skip(2), l => Assert.Equal(0, l.Left, 6));

            var plain = Lay("one two\nthree four five six seven", Advance("three four") + 40, new ParagraphStyle { TextIndent = new TextIndent(15) });
            Assert.Equal(0, plain.Lines[1].Left, 6);
        }

        [Fact]
        public void HangingWithEachLine_IndentsOnlyTheLinesThatNeitherStartTheParagraphNorFollowAForcedBreak()
        {
            var style = new ParagraphStyle { TextIndent = new TextIndent(15, Hanging: true, EachLine: true) };
            var layout = Lay("one two\nthree four five six seven", Advance("three four") + 40, style);

            Assert.Equal(0, layout.Lines[0].Left, 6);
            Assert.Equal(0, layout.Lines[1].Left, 6);
            Assert.All(layout.Lines.Skip(2), l => Assert.Equal(15, l.Left, 6));
        }

        [Fact]
        public void ARightToLeftParagraph_IsIndentedFromTheRight()
        {
            var style = new ParagraphStyle { Direction = BaseDirection.Rtl, TextIndent = new TextIndent(30) };
            var layout = Lay("Hello", 300, style);

            var line = layout.Lines[0];
            // Aligned at its start, the right: what is left of the indent is 270 wide, and the line ends where that does.
            Assert.Equal(270, line.Left + line.Width, 3);
        }

        [Theory]
        [InlineData(TextAlign.Left, 0)]
        [InlineData(TextAlign.Right, 1)]
        [InlineData(TextAlign.Center, 0.5)]
        public void AlignmentHappensInWhatTheIndentLeaves(TextAlign align, double fraction)
        {
            var layout = Lay("Hello", 300, new ParagraphStyle { Align = align, Direction = BaseDirection.Ltr, TextIndent = new TextIndent(40) });

            var line = layout.Lines[0];
            Assert.Equal(40 + ((260 - line.Width) * fraction), line.Left, 3);
        }

        [Fact]
        public void JustifiedText_FillsWhatTheIndentLeaves()
        {
            var layout = Lay("alpha beta gamma delta epsilon", Advance("alpha beta gamma") + 60, new ParagraphStyle { Align = TextAlign.Justify, TextIndent = new TextIndent(40) });

            var first = layout.Lines[0];
            Assert.Equal(40, first.Left, 6);
            Assert.Equal(layout.Width - 40, first.Width, 3);
            Assert.Equal(layout.Width, first.Left + first.Width, 3);
        }

        [Fact]
        public void ANegativeIndent_MovesTheTextOutOfTheParagraph()
        {
            var layout = Lay("Hello world", 300, new ParagraphStyle { TextIndent = new TextIndent(-12) });

            Assert.Equal(-12, layout.Lines[0].Left, 6);
        }

        [Fact]
        public void AnIndentWiderThanTheParagraph_StillMakesProgress()
        {
            var layout = Lay("alpha beta gamma", 50, new ParagraphStyle { TextIndent = new TextIndent(500), OverflowWrap = OverflowWrap.BreakWord });

            Assert.True(layout.Lines.Count >= 3);
            Assert.Equal(0, layout.Lines[0].Range.Start);
            Assert.Equal(16, layout.Lines[^1].Range.End);
            for (int i = 1; i < layout.Lines.Count; i++)
            {
                Assert.Equal(layout.Lines[i - 1].Range.End, layout.Lines[i].Range.Start);
                Assert.True(layout.Lines[i].Range.End > layout.Lines[i].Range.Start);
            }
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void AnIndentThatIsNotFinite_IsRefused(double length)
        {
            var builder = new ParagraphBuilder(new RunStyle(Face, Size));

            Assert.Throws<ArgumentOutOfRangeException>(() => builder.SetStyle(new ParagraphStyle { TextIndent = new TextIndent(length) }));
        }

        [Fact]
        public void TheContentWidths_IncludeTheIndent()
        {
            var plain = Build("alpha beta").MeasureContent();
            var indented = Build("alpha beta", new ParagraphStyle { TextIndent = new TextIndent(30) }).MeasureContent();
            var hanging = Build("alpha beta", new ParagraphStyle { TextIndent = new TextIndent(30, Hanging: true) }).MeasureContent();

            Assert.Equal(plain.MaxContent + 30, indented.MaxContent, 3);
            // Taking every break puts "alpha" alone on the first line and "beta" alone on a later one.
            Assert.Equal(Math.Max(Advance("alpha") + 30, Advance("beta")), indented.MinContent, 3);
            Assert.Equal(plain.MaxContent, hanging.MaxContent, 3);
            Assert.Equal(Math.Max(Advance("alpha"), Advance("beta") + 30), hanging.MinContent, 3);
        }

        [Fact]
        public void AnUnlimitedWidth_IsAsWideAsTheWidestLinePlusItsIndent()
        {
            var layout = Lay("alpha\nbeta gamma", double.PositiveInfinity, new ParagraphStyle { TextIndent = new TextIndent(30) });

            Assert.Equal(30 + Advance("alpha"), layout.Lines[0].Left + layout.Lines[0].Width, 3);
            Assert.Equal(Math.Max(30 + Advance("alpha"), Advance("beta gamma")), layout.ContentWidth, 3);
        }

        // ---- tab stops -------------------------------------------------------------------------------------------------------------

        [Fact]
        public void ATab_ReachesTheNextStop()
        {
            var style = new ParagraphStyle { TabSize = TabSize.FromLength(100) };
            var layout = Lay("a\tb", 1000, style);

            var runs = layout.Lines[0].Runs;
            Assert.Equal(3, runs.Count);
            Assert.Equal(new TextRange(1, 2), runs[1].Range);
            Assert.Equal(100 - Advance("a"), runs[1].Width, 3);
            Assert.Equal(100, runs[2].X, 3);
            Assert.Equal(100 + Advance("b"), layout.Lines[0].Width, 3);
        }

        [Fact]
        public void ATab_DrawsNothing_AndHasNoGlyphAdvances()
        {
            var layout = Lay("a\tb", 1000, new ParagraphStyle { TabSize = TabSize.FromLength(100) });

            var tab = layout.Lines[0].Runs[1];
            Assert.Empty(tab.Glyphs.Glyphs);
            Assert.Throws<ArgumentOutOfRangeException>(() => tab.GetGlyphAdvance(0));
        }

        [Fact]
        public void TabsInARow_EachMoveOnToTheNextStop()
        {
            var layout = Lay("\t\tx", 1000, new ParagraphStyle { TabSize = TabSize.FromLength(50) });

            var runs = layout.Lines[0].Runs;
            Assert.Equal(50, runs[0].Width, 3);
            Assert.Equal(50, runs[1].Width, 3);
            Assert.Equal(100, runs[2].X, 3);
        }

        [Fact]
        public void ATabAtAStop_MovesOnAWholeTabSize()
        {
            // Text that ends exactly on a stop leaves the tab a whole tab size to cover.
            double stop = Advance("abc");
            var layout = Lay("abc\td", 1000, new ParagraphStyle { TabSize = TabSize.FromLength(stop) });

            Assert.Equal(stop, layout.Lines[0].Runs[1].Width, 3);
        }

        [Fact]
        public void TheDefaultTabSize_IsEightSpaces()
        {
            var layout = Lay("\tx", 1000);

            Assert.Equal(8 * Space, layout.Lines[0].Runs[0].Width, 3);
            Assert.Equal(default, TabSize.FromSpaces(8));
            Assert.False(default(TabSize).IsLength);
            Assert.Equal(8, default(TabSize).Value);
        }

        [Fact]
        public void ATabSizeInSpaces_CountsTheSpacingOfASpace()
        {
            var run = new RunStyle(Face, Size, LetterSpacing: 1, WordSpacing: 2);
            var layout = Lay("\tx", 1000, new ParagraphStyle { TabSize = TabSize.FromSpaces(4) }, run);

            Assert.Equal(4 * (Space + 3), layout.Lines[0].Runs[0].Width, 3);
        }

        [Fact]
        public void TabStopsAreMeasuredFromTheStartEdge_SoAnIndentCountsInTheDistance()
        {
            var style = new ParagraphStyle { TabSize = TabSize.FromLength(50), TextIndent = new TextIndent(20) };
            var layout = Lay("\tx", 1000, style);

            var tab = layout.Lines[0].Runs[0];
            Assert.Equal(20, tab.X, 3);
            Assert.Equal(30, tab.Width, 3);
            Assert.Equal(50, layout.Lines[0].Runs[1].X, 3);
        }

        [Fact]
        public void TheTabsOfEveryLine_StartFromTheEdgeAgain()
        {
            var layout = Lay("ab\tx\nab\ty", 1000, new ParagraphStyle { TabSize = TabSize.FromLength(80) });

            Assert.Equal(layout.Lines[0].Runs[2].X, layout.Lines[1].Runs[2].X, 3);
            Assert.Equal(80, layout.Lines[0].Runs[2].X, 3);
        }

        [Fact]
        public void ATabIsPartOfWhereLinesBreak()
        {
            double width = 120;
            var style = new ParagraphStyle { TabSize = TabSize.FromLength(100) };
            var layout = Lay("a\tb c\td", width, style);

            Assert.All(layout.Lines, l => Assert.True(l.Left + l.Width <= width + 1e-6, $"{l.Range} is {l.Width} wide"));
            Assert.True(layout.Lines.Count > 1);
        }

        [Fact]
        public void ATabAtTheEndOfALine_Hangs()
        {
            var layout = Lay("ab\t", 1000, new ParagraphStyle { TabSize = TabSize.FromLength(100) });

            var line = layout.Lines[0];
            Assert.Equal(new TextRange(0, 3), line.Range);
            Assert.Equal(2, line.ContentEnd);
            Assert.Equal(Advance("ab"), line.Width, 3);
        }

        [Fact]
        public void ATabSizeOfZero_TakesNoRoom()
        {
            var layout = Lay("a\tb", 1000, new ParagraphStyle { TabSize = TabSize.FromLength(0) });

            Assert.Equal(Advance("a") + Advance("b"), layout.Lines[0].Width, 3);
            var spaces = Lay("a\tb", 1000, new ParagraphStyle { TabSize = TabSize.FromSpaces(0) });
            Assert.Equal(layout.Lines[0].Width, spaces.Lines[0].Width, 6);
        }

        [Theory]
        [InlineData(-1.0)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void ATabSizeThatIsNegativeOrNotFinite_IsRefused(double value)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => TabSize.FromLength(value));
            Assert.Throws<ArgumentOutOfRangeException>(() => TabSize.FromSpaces(value));
        }

        [Fact]
        public void TheCaretAndTheSelection_TakeInTheTab()
        {
            var layout = Lay("a\tb", 1000, new ParagraphStyle { TabSize = TabSize.FromLength(100) });

            Assert.Equal((float)Advance("a"), layout.CaretRect(new TextPosition(1)).X, 3);
            Assert.Equal(100f, layout.CaretRect(new TextPosition(2)).X, 3);
            var box = Assert.Single(layout.SelectionBoxes(new TextRange(1, 2)));
            Assert.Equal((float)Advance("a"), box.Left, 3);
            Assert.Equal(100f, box.Right, 3);
            // A point inside the tab is nearer one of its two edges.
            Assert.Equal(2, layout.PositionAt(new System.Drawing.PointF(95, 5)).Index);
        }

        [Fact]
        public void ATab_IsNotAJustificationOpportunity()
        {
            var style = new ParagraphStyle { Align = TextAlign.Justify, TabSize = TabSize.FromLength(60) };
            var layout = Lay("a\tb c d e f g h i j k l m n", 200, style);

            var first = layout.Lines[0];
            Assert.Equal(200, first.Left + first.Width, 3);
            var tab = first.Runs[1];
            Assert.Equal(new TextRange(1, 2), tab.Range);
            Assert.Equal(60 - Advance("a"), tab.Width, 3);
        }

        [Fact]
        public void ATabInARightToLeftParagraph_IsPlacedFromTheRightEdge()
        {
            var run = new RunStyle(HebrewFace, Size);
            var style = new ParagraphStyle { Direction = BaseDirection.Rtl, TabSize = TabSize.FromLength(100) };
            var layout = Lay("א\tב", 1000, style, run);

            var line = layout.Lines[0];
            Assert.Equal(3, line.Runs.Count);
            // Drawn left to right, the last character is first; the tab still reaches a stop 100 from the right edge.
            Assert.Equal(new TextRange(2, 3), line.Runs[0].Range);
            Assert.Equal(new TextRange(1, 2), line.Runs[1].Range);
            Assert.Equal(1000, line.Left + line.Width, 3);
        }

        // ---- hostile input ---------------------------------------------------------------------------------------------------------

        [Fact]
        public void AHugeWordCutIntoManyLines_FinishesQuickly()
        {
            var text = new string('m', 60_000);
            var stopwatch = Stopwatch.StartNew();
            var layout = Lay(text, 20 * Size, new ParagraphStyle { OverflowWrap = OverflowWrap.Anywhere });
            stopwatch.Stop();

            Assert.True(layout.Lines.Count > 100);
            Assert.Equal(text.Length, layout.Lines[^1].Range.End);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(60), $"took {stopwatch.Elapsed}");
        }

        [Fact]
        public void AZeroWidth_WithBreaksAllowed_PutsACharacterOnEveryLine()
        {
            var layout = Lay("abcdef", 0, new ParagraphStyle { OverflowWrap = OverflowWrap.Anywhere });

            Assert.Equal(6, layout.Lines.Count);
        }

        [Fact]
        public void ManyTabsWithAHugeTabSize_DoNotOverflowIntoNonsense()
        {
            var layout = Lay(new string('\t', 50), 100, new ParagraphStyle { TabSize = TabSize.FromLength(1e300) });

            Assert.All(layout.Lines, l => Assert.False(double.IsNaN(l.Width)));
        }
    }
}
