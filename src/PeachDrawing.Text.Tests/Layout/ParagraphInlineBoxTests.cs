using PeachDrawing.Text.Layout;
using PeachDrawing.Text.Unicode;
using PeachPDF.Tests.TestSupport;
using System.Diagnostics;
using System.Drawing;

namespace PeachDrawing.Text.Tests.Layout
{
    /// <summary>Inline boxes: an atomic box in a paragraph takes part in line breaking, makes its line as tall as it needs, and comes back placed.</summary>
    public class ParagraphInlineBoxTests
    {
        private const double Size = 20;
        private static readonly Typeface Face = Load(BundledFonts.Ttf);
        private static readonly Typeface HebrewFace = Load(BundledFonts.Hebrew);

        private static Typeface Load(string path)
        {
            var set = new FontSet();
            var family = set.AddFile(path, new AddOptions { FamilyName = "Box-" + Guid.NewGuid().ToString("N") });
            Assert.True(family.TryMatch(new TypefaceQuery(), out var match));
            return match.Typeface;
        }

        private static double Advance(string text) => TextRuler.WidthOf(Face, Size, text);

        private static double Ascent => Face.Metrics.NormalLineAscent * Size / Face.Metrics.UnitsPerEm;

        private static double Descent => Face.Metrics.NormalLineDescent * Size / Face.Metrics.UnitsPerEm;

        private static double Gap => Face.Metrics.NormalLineGap * Size / Face.Metrics.UnitsPerEm;

        private static Paragraph Build(string before, InlineBox box, string after, ParagraphStyle? style = null, Typeface? face = null)
        {
            var builder = new ParagraphBuilder(new RunStyle(face ?? Face, Size));
            if (style is { } given)
            {
                builder.SetStyle(given);
            }

            return builder.AddText(before).AddInlineBox(box).AddText(after).Build();
        }

        private static PlacedRun BoxRun(LineBox line) => Assert.Single(line.Runs, r => r.InlineBox is not null);

        // ---- placing ---------------------------------------------------------------------------------------------------------------

        [Fact]
        public void ABox_IsOneCharacterOfTheText_AndARunOfItsOwn()
        {
            var paragraph = Build("ab", new InlineBox(30, 10, Tag: "img"), "cd");
            var line = Assert.Single(paragraph.Layout(1000).Lines);

            Assert.Equal("ab￼cd", paragraph.Text);
            Assert.Equal(3, line.Runs.Count);
            var box = BoxRun(line);
            Assert.Equal(new TextRange(2, 3), box.Range);
            Assert.Empty(box.Glyphs.Glyphs);
            Assert.Equal(30, box.Width, 6);
            Assert.Equal(Advance("ab"), box.X, 3);
            Assert.Equal("img", box.InlineBox!.Value.Tag);
            Assert.Equal(Advance("ab") + 30, line.Runs[2].X, 3);
            Assert.Equal(Advance("ab") + 30 + Advance("cd"), line.Width, 3);
        }

        [Fact]
        public void ALiteralObjectReplacementCharacter_IsOrdinaryText()
        {
            var layout = new ParagraphBuilder(new RunStyle(Face, Size)).AddText("a￼b").Build().Layout(1000);

            Assert.All(layout.Lines[0].Runs, r => Assert.Null(r.InlineBox));
        }

        [Fact]
        public void ABoxIsBrokenAroundButNeverInside()
        {
            var paragraph = Build("aaaa", new InlineBox(40, 10), "bbbb");
            double width = Advance("aaaa") + 20;
            var layout = paragraph.Layout(width);

            Assert.Equal(3, layout.Lines.Count);
            Assert.Equal(new TextRange(0, 4), layout.Lines[0].Range);
            Assert.Equal(new TextRange(4, 5), layout.Lines[1].Range);
            Assert.Equal(new TextRange(5, 9), layout.Lines[2].Range);
            Assert.NotNull(BoxRun(layout.Lines[1]).InlineBox);
        }

        [Fact]
        public void ABoxWiderThanTheLine_OverflowsOnALineOfItsOwn()
        {
            var layout = Build("a", new InlineBox(500, 10), "b").Layout(100);

            Assert.Equal(3, layout.Lines.Count);
            Assert.Equal(500, layout.Lines[1].Width, 6);
        }

        [Fact]
        public void ABoxCountsInTheNarrowestAndWidestParagraph()
        {
            var widths = Build("ab", new InlineBox(70, 10), "cd").MeasureContent();

            Assert.Equal(Advance("ab") + 70 + Advance("cd"), widths.MaxContent, 3);
            Assert.True(widths.MinContent >= 70);
        }

        [Fact]
        public void ABoxOfNoWidth_IsAllowed()
        {
            var layout = Build("ab", new InlineBox(0, 0), "cd").Layout(1000);

            Assert.Equal(Advance("ab") + Advance("cd"), layout.Lines[0].Width, 3);
        }

        // ---- height and vertical alignment ------------------------------------------------------------------------------------------

        [Fact]
        public void ABoxOnTheBaseline_SitsOnItWithItsBottomEdge_AndMakesTheLineTallerAbove()
        {
            var tall = Build("ab", new InlineBox(30, 50), "cd").Layout(1000).Lines[0];

            var bounds = BoxRun(tall).InlineBoxBounds;
            Assert.Equal((float)tall.Baseline, bounds.Bottom, 2);
            Assert.Equal(50f, bounds.Height);
            Assert.Equal(30f, bounds.Width);
            Assert.Equal((float)tall.Top, bounds.Top, 2);
            Assert.Equal(50, tall.Ascent, 3);
            Assert.Equal(Descent + (Gap / 2), tall.Descent, 3);
            Assert.Equal(tall.Ascent + tall.Descent, tall.Height, 6);
        }

        [Fact]
        public void ASmallBox_DoesNotChangeTheLineHeight()
        {
            var plain = new ParagraphBuilder(new RunStyle(Face, Size)).AddText("abcd").Build().Layout(1000).Lines[0];
            var boxed = Build("ab", new InlineBox(10, 5), "cd").Layout(1000).Lines[0];

            Assert.Equal(plain.Height, boxed.Height, 6);
            Assert.Equal(plain.Ascent, boxed.Ascent, 6);
        }

        [Fact]
        public void ABoxWithABaseline_HasThatPointOnTheLineBaseline()
        {
            var line = Build("ab", new InlineBox(30, 50, Baseline: 40), "cd").Layout(1000).Lines[0];

            var bounds = BoxRun(line).InlineBoxBounds;
            Assert.Equal((float)line.Baseline - 40, bounds.Top, 2);
            Assert.Equal(40, line.Ascent, 3);
            // The box reaches 10 below the baseline, further than the text does.
            Assert.Equal(10, line.Descent, 3);
        }

        [Fact]
        public void ABoxRaisedByAShift_ReachesFurtherAbove()
        {
            var plain = Build("ab", new InlineBox(30, 50), "cd").Layout(1000).Lines[0];
            var raised = Build("ab", new InlineBox(30, 50, BaselineShift: 10), "cd").Layout(1000).Lines[0];

            Assert.Equal(plain.Ascent + 10, raised.Ascent, 3);
            Assert.Equal((float)(raised.Baseline - 60), BoxRun(raised).InlineBoxBounds.Top, 2);
        }

        [Fact]
        public void Middle_PutsTheMiddleOfTheBoxAtHalfTheXHeight()
        {
            var line = Build("ab", new InlineBox(30, 60, VerticalAlign: VerticalAlign.Middle), "cd").Layout(1000).Lines[0];

            var bounds = BoxRun(line).InlineBoxBounds;
            double xHeight = Face.Metrics.XHeight * Size / Face.Metrics.UnitsPerEm;
            Assert.Equal(line.Baseline - (xHeight / 2), bounds.Top + (bounds.Height / 2), 2);
        }

        [Fact]
        public void TextTop_AndTextBottom_AlignToTheEdgesOfTheText()
        {
            var top = Build("ab", new InlineBox(30, 60, VerticalAlign: VerticalAlign.TextTop), "cd").Layout(1000).Lines[0];
            var bottom = Build("ab", new InlineBox(30, 60, VerticalAlign: VerticalAlign.TextBottom), "cd").Layout(1000).Lines[0];

            Assert.Equal(top.Baseline - Ascent, BoxRun(top).InlineBoxBounds.Top, 2);
            Assert.Equal(bottom.Baseline + Descent, BoxRun(bottom).InlineBoxBounds.Bottom, 2);
        }

        [Fact]
        public void Top_AndBottom_AlignToTheLine_WhichGrowsToHoldThem()
        {
            var top = Build("ab", new InlineBox(30, 80, VerticalAlign: VerticalAlign.Top), "cd").Layout(1000).Lines[0];
            var bottom = Build("ab", new InlineBox(30, 80, VerticalAlign: VerticalAlign.Bottom), "cd").Layout(1000).Lines[0];

            Assert.Equal(80, top.Height, 3);
            Assert.Equal((float)top.Top, BoxRun(top).InlineBoxBounds.Top, 2);
            Assert.Equal(80, bottom.Height, 3);
            Assert.Equal((float)(bottom.Top + bottom.Height), BoxRun(bottom).InlineBoxBounds.Bottom, 2);
            // The text stays where it would be, and the line grows on the other side.
            Assert.Equal(Ascent + (Gap / 2), top.Ascent, 3);
            Assert.Equal(Descent + (Gap / 2), bottom.Descent, 3);
        }

        [Fact]
        public void ALineOfOneBox_IsAsTallAsItsTextWouldBe()
        {
            var layout = Build("", new InlineBox(10, 5), "").Layout(1000);

            var line = Assert.Single(layout.Lines);
            Assert.True(line.Height >= Ascent + Descent - 1e-6);
            Assert.Equal(10, line.Width, 6);
        }

        [Fact]
        public void TheBoxesOfManyLines_AreEachPlacedOnTheirOwnLine()
        {
            var builder = new ParagraphBuilder(new RunStyle(Face, Size));
            builder.AddInlineBox(new InlineBox(30, 30)).AddText("\n").AddInlineBox(new InlineBox(30, 60));
            var layout = builder.Build().Layout(1000);

            Assert.Equal(2, layout.Lines.Count);
            Assert.Equal((float)layout.Lines[0].Baseline, BoxRun(layout.Lines[0]).InlineBoxBounds.Bottom, 2);
            Assert.Equal((float)layout.Lines[1].Baseline, BoxRun(layout.Lines[1]).InlineBoxBounds.Bottom, 2);
            Assert.True(layout.Lines[1].Top > layout.Lines[0].Top);
        }

        [Fact]
        public void ALineHeightMultiple_IsKeptForTheText_AndABoxStillGrowsIt()
        {
            var style = new ParagraphStyle { LineHeight = 2 };
            var small = Build("ab", new InlineBox(10, 5), "cd", style).Layout(1000).Lines[0];
            var tall = Build("ab", new InlineBox(10, 200), "cd", style).Layout(1000).Lines[0];

            Assert.Equal(2 * Size, small.Height, 3);
            Assert.True(tall.Height > 200);
        }

        // ---- editing, direction, spacing -------------------------------------------------------------------------------------------

        [Fact]
        public void TheCaretAndTheSelection_TakeInTheBox()
        {
            var layout = Build("ab", new InlineBox(30, 10), "cd").Layout(1000);

            Assert.Equal((float)Advance("ab"), layout.CaretRect(new TextPosition(2)).X, 2);
            Assert.Equal((float)(Advance("ab") + 30), layout.CaretRect(new TextPosition(3)).X, 2);
            var box = Assert.Single(layout.SelectionBoxes(new TextRange(2, 3)));
            Assert.Equal(30f, box.Width, 2);
            Assert.Equal(3, layout.PositionAt(new PointF((float)(Advance("ab") + 25), 5)).Index);
            Assert.Equal(2, layout.PositionAt(new PointF((float)(Advance("ab") + 3), 5)).Index);
        }

        [Fact]
        public void ABoxInRightToLeftText_IsPlacedInReadingOrder()
        {
            var builder = new ParagraphBuilder(new RunStyle(HebrewFace, Size)).SetStyle(new ParagraphStyle { Direction = BaseDirection.Rtl });
            builder.AddText("אב").AddInlineBox(new InlineBox(30, 10)).AddText("גד");
            var line = builder.Build().Layout(500).Lines[0];

            // Drawn left to right: the later text first, the box in the middle.
            Assert.Equal(new TextRange(3, 5), line.Runs[0].Range);
            Assert.Equal(new TextRange(2, 3), line.Runs[1].Range);
            Assert.NotNull(line.Runs[1].InlineBox);
            Assert.Equal(new TextRange(0, 2), line.Runs[2].Range);
            Assert.Equal(line.Runs[1].X, line.Runs[1].InlineBoxBounds.X, 2);
        }

        [Fact]
        public void ABoxIsAWall_ForJustification()
        {
            var style = new ParagraphStyle { Align = TextAlign.Justify, TextJustify = TextJustify.InterCharacter };
            var layout = Build("ab", new InlineBox(30, 10), "cd", style).Layout(400);

            // Left as it is: the only opportunities would be next to the box.
            Assert.Equal(Advance("ab") + 30 + Advance("cd"), layout.Lines[0].Width, 3);
        }

        [Fact]
        public void ATabAfterABox_MeasuresFromTheEndOfTheBox()
        {
            var layout = Build("", new InlineBox(30, 10), "\tx", new ParagraphStyle { TabSize = TabSize.FromLength(50) }).Layout(1000);

            var line = layout.Lines[0];
            Assert.Equal(20, line.Runs[1].Width, 3);
            Assert.Equal(50, line.Runs[2].X, 3);
        }

        [Fact]
        public void ACutLine_KeepsAWholeBoxOrNone()
        {
            var layout = Build("ab", new InlineBox(60, 10), "cdefghij", new ParagraphStyle { MaxLines = 1 }).Layout(Advance("ab") + 30);

            var line = layout.Lines[0];
            Assert.True(line.IsTruncated);
            Assert.Equal(2, line.ContentEnd);
            Assert.DoesNotContain(line.Runs, r => r.InlineBox is not null);
        }

        // ---- validation and hostile input ------------------------------------------------------------------------------------------

        [Theory]
        [InlineData(-1.0, 10.0)]
        [InlineData(10.0, -1.0)]
        [InlineData(double.NaN, 10.0)]
        [InlineData(10.0, double.PositiveInfinity)]
        public void ABoxWithABadSize_IsRefused(double width, double height)
        {
            var builder = new ParagraphBuilder(new RunStyle(Face, Size));

            Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddInlineBox(new InlineBox(width, height)));
        }

        [Fact]
        public void ABreakIsAllowedBeforeAndAfterABox_EvenNextToPunctuationThatWouldSuppressOne()
        {
            // "(" and ")" and "," would keep a line from breaking next to an ordinary character.
            var opening = new ParagraphBuilder(new RunStyle(Face, Size)).AddText("aaaa (").AddInlineBox(new InlineBox(40, 10)).Build();
            var closing = new ParagraphBuilder(new RunStyle(Face, Size)).AddText("aaaa").AddInlineBox(new InlineBox(40, 10)).AddText(",bbbb").Build();

            var openLayout = opening.Layout(Advance("aaaa (") + 10);
            Assert.Equal(new TextRange(0, 6), openLayout.Lines[0].Range);
            Assert.NotNull(BoxRun(openLayout.Lines[1]).InlineBox);
            var closeLayout = closing.Layout(Advance("aaaa") + 45);
            Assert.Equal(new TextRange(0, 5), closeLayout.Lines[0].Range);
            Assert.Equal(new TextRange(5, 10), closeLayout.Lines[1].Range);
        }

        [Fact]
        public void ABreakIsNotAllowedNextToAWordJoiner()
        {
            var paragraph = new ParagraphBuilder(new RunStyle(Face, Size)).AddText("aaaa\u2060").AddInlineBox(new InlineBox(40, 10)).Build();

            var layout = paragraph.Layout(Advance("aaaa") + 10);
            Assert.Single(layout.Lines);
        }

        [Fact]
        public void ABoxOfAnAbsurdSize_IsRefused()
        {
            var builder = new ParagraphBuilder(new RunStyle(Face, Size));

            Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddInlineBox(new InlineBox(double.MaxValue, 1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddInlineBox(new InlineBox(1, 2e9)));
            Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddInlineBox(new InlineBox(1, 1, Baseline: 2e9)));
            Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddInlineBox(new InlineBox(1, 1, BaselineShift: -2e9)));
            builder.AddInlineBox(new InlineBox(1e9, 1e9, BaselineShift: -1e9));
        }

        [Fact]
        public void ABoxWithABadBaselineShiftOrAlignment_IsRefused()
        {
            var builder = new ParagraphBuilder(new RunStyle(Face, Size));

            Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddInlineBox(new InlineBox(1, 1, Baseline: double.NaN)));
            Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddInlineBox(new InlineBox(1, 1, BaselineShift: double.PositiveInfinity)));
            Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddInlineBox(new InlineBox(1, 1, VerticalAlign: (VerticalAlign)99)));
        }

        [Fact]
        public void ManyBoxes_AreLaidOutQuickly()
        {
            var builder = new ParagraphBuilder(new RunStyle(Face, Size));
            for (int i = 0; i < 5000; i++)
            {
                builder.AddInlineBox(new InlineBox(7, 7, Tag: i)).AddText(" ");
            }

            var stopwatch = Stopwatch.StartNew();
            var layout = builder.Build().Layout(300);
            stopwatch.Stop();

            Assert.True(layout.Lines.Count > 50);
            Assert.Equal(5000, layout.Lines.SelectMany(l => l.Runs).Count(r => r.InlineBox is not null));
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30), $"took {stopwatch.Elapsed}");
        }
    }
}
