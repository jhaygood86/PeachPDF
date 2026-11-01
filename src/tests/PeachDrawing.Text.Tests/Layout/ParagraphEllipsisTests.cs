using PeachDrawing.Text.Layout;
using PeachDrawing.Text.Unicode;
using PeachPDF.Tests.TestSupport;
using System.Diagnostics;
using System.Text;

namespace PeachDrawing.Text.Tests.Layout
{
    /// <summary>A line limit (<c>line-clamp</c>) and <c>text-overflow: ellipsis</c>: text cut short, and the ellipsis a line then ends with.</summary>
    public class ParagraphEllipsisTests
    {
        private const double Size = 20;
        private static readonly Typeface Face = Load(BundledFonts.Ttf);
        private static readonly Typeface HebrewFace = Load(BundledFonts.Hebrew);
        private static readonly string EllipsisText = Face.TryMapRune(new Rune(0x2026), out _) ? "…" : "...";
        private static readonly double EllipsisWidth = TextRuler.WidthOf(Face, Size, EllipsisText);

        private static Typeface Load(string path)
        {
            var set = new FontSet();
            var family = set.AddFile(path, new AddOptions { FamilyName = "Ellipsis-" + Guid.NewGuid().ToString("N") });
            Assert.True(family.TryMatch(new TypefaceQuery(), out var match));
            return match.Typeface;
        }

        private static double Advance(string text) => TextRuler.WidthOf(Face, Size, text);

        private static ParagraphLayout Lay(string text, double width, ParagraphStyle style, Typeface? face = null)
            => new ParagraphBuilder(new RunStyle(face ?? Face, Size)).SetStyle(style).AddText(text).Build().Layout(width);

        private static PlacedRun? Generated(LineBox line) => line.Runs.SingleOrDefault(r => r.IsGenerated);

        private const string Words = "alpha beta gamma delta epsilon zeta eta theta iota kappa lambda";

        // ---- MaxLines --------------------------------------------------------------------------------------------------------------

        [Fact]
        public void TextThatDoesNotFitTheLines_IsCutOnTheLastOne_WhichEndsWithAnEllipsis()
        {
            double width = Advance("alpha beta gamma") + 1;
            var layout = Lay(Words, width, new ParagraphStyle { MaxLines = 2 });

            Assert.Equal(2, layout.Lines.Count);
            Assert.True(layout.IsTruncated);
            var last = layout.Lines[^1];
            Assert.True(last.IsTruncated);
            Assert.False(layout.Lines[0].IsTruncated);
            Assert.Equal(LineEnd.Last, last.End);
            Assert.Equal(Words.Length, last.Range.End);
            Assert.True(last.ContentEnd < last.Range.End);
            Assert.True(last.Width <= width + 1e-6);
            var ellipsis = Generated(last);
            Assert.NotNull(ellipsis);
            Assert.True(ellipsis!.Range.IsEmpty);
            Assert.Equal(last.ContentEnd, ellipsis.Range.Start);
            Assert.Equal(EllipsisWidth, ellipsis.Width, 3);
            Assert.Same(last.Runs[^1], ellipsis);
        }

        [Fact]
        public void TheLastLine_IsFilledAsFarAsTheEllipsisAllows()
        {
            double width = Advance("alpha beta gamma") + 1;
            var layout = Lay(Words, width, new ParagraphStyle { MaxLines = 1 });

            var line = layout.Lines[0];
            string shown = Words[line.Range.Start..line.ContentEnd];
            Assert.True(Advance(shown) + EllipsisWidth <= width + 1e-6);
            // One more character would not have fitted.
            string more = Words[line.Range.Start..Math.Min(Words.Length, line.ContentEnd + 1)].TrimEnd();
            Assert.True(Advance(more) + EllipsisWidth > width - 1e-6 || more == shown);
        }

        [Fact]
        public void TextThatFits_IsNotCut()
        {
            var layout = Lay("alpha beta", 1000, new ParagraphStyle { MaxLines = 2 });

            Assert.False(layout.IsTruncated);
            var line = Assert.Single(layout.Lines);
            Assert.False(line.IsTruncated);
            Assert.Null(Generated(line));
        }

        [Fact]
        public void ATrailingNewline_IsNotTextThatIsLeftOut()
        {
            var layout = Lay("one\n", 1000, new ParagraphStyle { MaxLines = 1 });

            Assert.False(layout.IsTruncated);
            var line = Assert.Single(layout.Lines);
            Assert.Null(Generated(line));
            Assert.Equal(LineEnd.Forced, line.End);
        }

        [Fact]
        public void ALineEndingInAForcedBreak_GetsTheEllipsisWhenMoreTextFollows()
        {
            var layout = Lay("one\ntwo", 1000, new ParagraphStyle { MaxLines = 1 });

            Assert.True(layout.IsTruncated);
            var line = Assert.Single(layout.Lines);
            Assert.Equal(new TextRange(0, 7), line.Range);
            Assert.Equal(3, line.ContentEnd);
            Assert.NotNull(Generated(line));
            Assert.Equal(Advance("one") + EllipsisWidth, line.Width, 3);
        }

        [Fact]
        public void ACustomEllipsis_IsUsed_AndAnEmptyOneIsNothing()
        {
            var custom = Lay(Words, Advance("alpha beta gamma"), new ParagraphStyle { MaxLines = 1, Ellipsis = "~" });
            var empty = Lay(Words, Advance("alpha beta gamma"), new ParagraphStyle { MaxLines = 1, Ellipsis = "" });

            Assert.Equal(Advance("~"), Generated(custom.Lines[0])!.Width, 3);
            Assert.True(custom.Lines[0].Width <= Advance("alpha beta gamma") + 1e-6);
            Assert.True(empty.IsTruncated);
            Assert.True(empty.Lines[0].IsTruncated);
            Assert.Null(Generated(empty.Lines[0]));
        }

        [Fact]
        public void AnEllipsisWiderThanTheLine_IsAllThatIsLeft()
        {
            var layout = Lay(Words, EllipsisWidth / 2, new ParagraphStyle { MaxLines = 1 });

            var line = Assert.Single(layout.Lines);
            Assert.Equal(0, line.ContentEnd);
            var run = Assert.Single(line.Runs);
            Assert.True(run.IsGenerated);
        }

        [Fact]
        public void TheCut_IsBetweenCharactersAReaderSeesAsOne()
        {
            // The accent stays with its letter, so the cut is never between them.
            var text = string.Concat(Enumerable.Repeat("é", 30));
            for (double width = 40; width < 120; width += 3.3)
            {
                var layout = Lay(text, width, new ParagraphStyle { MaxLines = 1 });

                int cut = layout.Lines[0].ContentEnd;
                Assert.True(cut % 2 == 0, $"cut at {cut} for {width}");
            }
        }

        [Fact]
        public void AJustifiedParagraph_AlignsTheCutLineAsALastLine()
        {
            var layout = Lay(Words, Advance("alpha beta gamma") + 1, new ParagraphStyle { MaxLines = 2, Align = TextAlign.Justify });

            Assert.True(layout.Lines[0].Width > Advance("alpha beta gamma") - 1);
            var last = layout.Lines[1];
            Assert.Equal(0, last.Left, 6);
            Assert.True(last.Width < layout.Width || last.Width <= layout.Width + 1e-6);
        }

        [Fact]
        public void AHyphenatedLine_LosesItsHyphenToTheEllipsis()
        {
            var text = "hy­phen more text after it";
            var layout = Lay(text, Advance("hy") + 30, new ParagraphStyle { MaxLines = 1 });

            var line = Assert.Single(layout.Lines);
            Assert.Equal(LineEnd.Last, line.End);
            var generated = Assert.Single(line.Runs, r => r.IsGenerated);
            Assert.Equal(EllipsisWidth, generated.Width, 3);
        }

        [Fact]
        public void ARightToLeftParagraph_PutsTheEllipsisAtTheLeftEnd()
        {
            var text = "אבגדאבגדאבגד";
            double width = TextRuler.WidthOf(HebrewFace, Size, text.Substring(0, 5)) + 20;
            var layout = Lay(text, width, new ParagraphStyle { MaxLines = 1, Direction = BaseDirection.Rtl, OverflowWrap = OverflowWrap.BreakWord }, HebrewFace);

            var line = layout.Lines[0];
            Assert.True(line.IsTruncated);
            Assert.True(line.Runs[0].IsGenerated);
            // The caret at the end of the drawn text is where the text meets the ellipsis.
            var caret = layout.CaretRect(new TextPosition(line.ContentEnd, TextAffinity.Upstream));
            Assert.Equal((float)(line.Runs[0].X + line.Runs[0].Width), caret.X, 1);
        }

        [Fact]
        public void TheHiddenText_HasNoSelectionBoxAndTheDrawnTextHasOne()
        {
            var layout = Lay(Words, Advance("alpha beta gamma") + 1, new ParagraphStyle { MaxLines = 1 });

            var line = layout.Lines[0];
            var boxes = layout.SelectionBoxes(new TextRange(0, Words.Length));
            var box = Assert.Single(boxes);
            Assert.Equal((float)(line.Runs[^2].X + line.Runs[^2].Width), box.Right, 1);
            // A caret in the hidden text stays on the line, after what is drawn.
            var caret = layout.CaretRect(new TextPosition(Words.Length - 2));
            Assert.Equal((float)line.Top, caret.Top);
        }

        [Fact]
        public void MaxLinesAndAnEllipsis_AreValidated()
        {
            var builder = new ParagraphBuilder(new RunStyle(Face, Size));

            Assert.Throws<ArgumentOutOfRangeException>(() => builder.SetStyle(new ParagraphStyle { MaxLines = 0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => builder.SetStyle(new ParagraphStyle { Ellipsis = new string('.', 33) }));
        }

        [Fact]
        public void TheLineLimit_IsQuickOnAHugeText()
        {
            var text = string.Join(' ', Enumerable.Repeat("alpha", 100_000));
            var stopwatch = Stopwatch.StartNew();
            var layout = Lay(text, 200, new ParagraphStyle { MaxLines = 3 });
            stopwatch.Stop();

            Assert.Equal(3, layout.Lines.Count);
            Assert.True(layout.IsTruncated);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30), $"took {stopwatch.Elapsed}");
        }

        // ---- text-overflow ---------------------------------------------------------------------------------------------------------

        [Fact]
        public void AnOverflowingLine_IsCutToFitWithTheEllipsis()
        {
            double width = Advance("alpha beta");
            var layout = Lay(Words, width, new ParagraphStyle { NoWrap = true, TextOverflow = TextOverflow.Ellipsis });

            var line = Assert.Single(layout.Lines);
            Assert.False(layout.IsTruncated);
            Assert.True(line.IsTruncated);
            Assert.Equal(LineEnd.Last, line.End);
            Assert.True(line.Width <= width + 1e-6);
            Assert.NotNull(Generated(line));
            Assert.Equal(Words.Length, line.Range.End);
        }

        [Fact]
        public void AWordWiderThanTheLine_IsCutToo()
        {
            var layout = Lay("Supercalifragilistic and more", Advance("Super"), new ParagraphStyle { TextOverflow = TextOverflow.Ellipsis });

            Assert.True(layout.Lines[0].IsTruncated);
            Assert.True(layout.Lines[0].Width <= Advance("Super") + 1e-6);
            Assert.All(layout.Lines.Skip(1), l => Assert.True(l.Width <= Advance("Super") + 1e-6 || !l.IsTruncated));
        }

        [Fact]
        public void ALineThatFits_IsLeftAlone_WhateverTheOverflowSays()
        {
            var layout = Lay("alpha", 1000, new ParagraphStyle { NoWrap = true, TextOverflow = TextOverflow.Ellipsis });

            Assert.False(layout.Lines[0].IsTruncated);
            Assert.Null(Generated(layout.Lines[0]));
        }

        [Fact]
        public void Clip_LeavesTheOverflowAlone()
        {
            var layout = Lay(Words, Advance("alpha"), new ParagraphStyle { NoWrap = true, TextOverflow = TextOverflow.Clip });

            Assert.False(layout.Lines[0].IsTruncated);
            Assert.True(layout.Lines[0].Width > Advance("alpha"));
        }

        [Fact]
        public void AnUnlimitedWidth_HasNoOverflow()
        {
            var layout = Lay(Words, double.PositiveInfinity, new ParagraphStyle { TextOverflow = TextOverflow.Ellipsis });

            Assert.False(layout.Lines[0].IsTruncated);
        }

        [Fact]
        public void EveryLineOfANoWrapParagraph_IsCutOnItsOwn()
        {
            var layout = Lay(Words + "\n" + Words, Advance("alpha beta"), new ParagraphStyle { NoWrap = true, TextOverflow = TextOverflow.Ellipsis });

            Assert.Equal(2, layout.Lines.Count);
            Assert.All(layout.Lines, l => Assert.True(l.IsTruncated));
            Assert.Equal(LineEnd.Forced, layout.Lines[0].End);
        }

        [Fact]
        public void AnEllipsisInAnotherSize_StillFitsTheLine()
        {
            for (double width = 60; width < 260; width += 7.3)
            {
                var builder = new ParagraphBuilder(new RunStyle(Face, 10)).SetStyle(new ParagraphStyle { MaxLines = 1 });
                builder.AddText("abc ").PushRun(new RunStyle(Face, 40)).AddText("defghijklmnopqrstuvwxyz ").PopRun().AddText("tail of small text");
                var line = builder.Build().Layout(width).Lines[0];

                Assert.True(line.Width <= width + 1e-6, $"{line.Width} in {width}");
            }
        }

        [Fact]
        public void TheEllipsis_UsesTheStyleOfTheLastCharacterDrawn()
        {
            var builder = new ParagraphBuilder(new RunStyle(Face, Size)).SetStyle(new ParagraphStyle { MaxLines = 1 });
            builder.AddText("abc ").PushRun(new RunStyle(Face, Size * 2)).AddText("def ghi jkl mno pqr").PopRun();
            var layout = builder.Build().Layout(Advance("abc ") + (Size * 2 / Size * Advance("def gh")));

            var ellipsis = Generated(layout.Lines[0]);
            Assert.NotNull(ellipsis);
            Assert.Equal(Size * 2, ellipsis!.Style.Size, 6);
        }
    }
}
