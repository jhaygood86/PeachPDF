using PeachDrawing.Text.Layout;
using PeachDrawing.Text.Unicode;
using PeachPDF.Tests.TestSupport;

namespace PeachDrawing.Text.Tests.Layout
{
    /// <summary>The host-driven tier: a paragraph laid out one line at a time in spaces its caller chooses, without changing its inputs.</summary>
    public class LineFlowTests
    {
        private const double Size = 20;
        private static readonly Typeface Face = Load(BundledFonts.Ttf);
        private static readonly Typeface HebrewFace = Load(BundledFonts.Hebrew);
        private static readonly LineBreakOptions English = new() { Language = "en" };

        private static Typeface Load(string path)
        {
            var set = new FontSet();
            var family = set.AddFile(path, new AddOptions { FamilyName = "Flow-" + Guid.NewGuid().ToString("N") });
            Assert.True(family.TryMatch(new TypefaceQuery(), out var match));
            return match.Typeface;
        }

        private static double Advance(string text) => TextRuler.WidthOf(Face, Size, text);

        private static Paragraph Build(string text, ParagraphStyle? style = null, Typeface? face = null)
        {
            var builder = new ParagraphBuilder(new RunStyle(face ?? Face, Size));
            if (style is { } given)
            {
                builder.SetStyle(given);
            }

            return builder.AddText(text).Build();
        }

        /// <summary>Lays a paragraph out through the flow the way <see cref="Paragraph.Layout"/> does: every line in the full width, the next one under it.</summary>
        private static List<LineBox> Flow(Paragraph paragraph, double width)
        {
            var flow = paragraph.CreateFlow();
            var lines = new List<LineBox>();
            var cursor = flow.Start;
            double top = 0;
            while (flow.TryNext(cursor, new LineSpace(0, width, flow.GetIndent(cursor), top), out var line, out var next))
            {
                lines.Add(line);
                top += line.Height;
                cursor = next;
                Assert.True(lines.Count <= paragraph.Text.Length + 2, "the flow does not end");
            }

            return lines;
        }

        private static void AssertSame(LineBox expected, LineBox actual)
        {
            Assert.Equal(expected.Range, actual.Range);
            Assert.Equal(expected.End, actual.End);
            Assert.Equal(expected.ContentEnd, actual.ContentEnd);
            Assert.Equal(expected.Left, actual.Left, 6);
            Assert.Equal(expected.Top, actual.Top, 6);
            Assert.Equal(expected.Width, actual.Width, 6);
            Assert.Equal(expected.Ascent, actual.Ascent, 6);
            Assert.Equal(expected.Height, actual.Height, 6);
            Assert.Equal(expected.Runs.Count, actual.Runs.Count);
            for (int i = 0; i < expected.Runs.Count; i++)
            {
                Assert.Equal(expected.Runs[i].Range, actual.Runs[i].Range);
                Assert.Equal(expected.Runs[i].X, actual.Runs[i].X, 6);
                Assert.Equal(expected.Runs[i].Width, actual.Runs[i].Width, 6);
                Assert.Equal(expected.Runs[i].Glyphs.Glyphs.Count, actual.Runs[i].Glyphs.Glyphs.Count);
                Assert.Equal(expected.Runs[i].IsGenerated, actual.Runs[i].IsGenerated);
            }
        }

        private static void AssertSameAsLayout(Paragraph paragraph, double width)
        {
            var layout = paragraph.Layout(width);
            var flowed = Flow(paragraph, width);

            Assert.Equal(layout.Lines.Count, flowed.Count);
            for (int i = 0; i < flowed.Count; i++)
            {
                AssertSame(layout.Lines[i], flowed[i]);
            }
        }

        public static TheoryData<string> Cases => new()
        {
            "plain",
            "indent-first",
            "indent-hanging-each-line",
            "tabs",
            "justify",
            "justify-inter-character",
            "hyphens-auto",
            "soft-hyphens",
            "align-center",
            "align-right",
            "letter-spacing",
            "boxes",
            "text-overflow",
            "break-word",
            "forced-breaks",
            "rtl",
            "line-height",
            "nowrap",
            "last-line-limit",
        };

        private static Paragraph CaseParagraph(string name)
        {
            const string Words = "alpha beta gamma delta epsilon zeta eta theta iota kappa lambda mu nu xi omicron pi rho sigma tau";
            switch (name)
            {
                case "plain": return Build(Words);
                case "indent-first": return Build(Words, new ParagraphStyle { TextIndent = new TextIndent(30) });
                case "indent-hanging-each-line": return Build(Words.Replace("gamma", "gamma\n").Replace("iota", "iota\n"), new ParagraphStyle { TextIndent = new TextIndent(25, Hanging: true, EachLine: true) });
                case "tabs": return Build("a\tb c\td e\tf g\th i\tj k\tl", new ParagraphStyle { TabSize = TabSize.FromLength(70) });
                case "justify": return Build(Words, new ParagraphStyle { Align = TextAlign.Justify });
                case "justify-inter-character": return Build("abcdefghij klmnopqrst uvwxyz abcdefghij klmnopqrst", new ParagraphStyle { Align = TextAlign.Justify, TextJustify = TextJustify.InterCharacter });
                case "hyphens-auto": return Build("internationalization of representational characteristics and internationalization", new ParagraphStyle { Hyphens = Hyphens.Auto, LineBreak = English, HyphenateLimitLines = 2, Align = TextAlign.Justify });
                case "soft-hyphens": return Build("hy­phen­ation of long­er words in a para­graph of text", new ParagraphStyle());
                case "align-center": return Build(Words, new ParagraphStyle { Align = TextAlign.Center });
                case "align-right": return Build(Words, new ParagraphStyle { Align = TextAlign.Right });
                case "letter-spacing": return new ParagraphBuilder(new RunStyle(Face, Size, LetterSpacing: 1.5, WordSpacing: 4)).AddText(Words).Build();
                case "boxes":
                    return new ParagraphBuilder(new RunStyle(Face, Size)).AddText("one two ").AddInlineBox(new InlineBox(40, 30)).AddText(" three four ").AddInlineBox(new InlineBox(25, 50, VerticalAlign: VerticalAlign.Top)).AddText(" five six seven").Build();
                case "text-overflow": return Build("Supercalifragilisticexpialidocious and words", new ParagraphStyle { TextOverflow = TextOverflow.Ellipsis });
                case "break-word": return Build("Supercalifragilisticexpialidocious and words", new ParagraphStyle { OverflowWrap = OverflowWrap.BreakWord });
                case "forced-breaks": return Build("one two three four\nfive six seven eight nine ten\n\nlast line here\n");
                case "rtl": return Build("אבגד אבג דאבגד אב גדאבגד אבגד", new ParagraphStyle { Direction = BaseDirection.Rtl, TextIndent = new TextIndent(15) }, HebrewFace);
                case "line-height": return Build(Words, new ParagraphStyle { LineHeight = 1.7 });
                case "last-line-limit": return Build("internationalization of representational characteristics internationalization", new ParagraphStyle { Hyphens = Hyphens.Auto, LineBreak = English, HyphenateLimitLast = HyphenateLimitLast.Always, TextIndent = new TextIndent(40) });
                case "nowrap": return Build(Words + "\n" + Words, new ParagraphStyle { NoWrap = true });
                default: throw new ArgumentException(name);
            }
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void FlowingAtAWidth_GivesTheLinesOfLayout(string name)
        {
            var paragraph = CaseParagraph(name);

            AssertSameAsLayout(paragraph, 200);
            AssertSameAsLayout(paragraph, 96);
            AssertSameAsLayout(paragraph, 1000);
        }

        // ---- restartability and immutability ---------------------------------------------------------------------------------------

        [Fact]
        public void ALineCanBeLaidOutAgain_FromTheCursorItStartedAt()
        {
            var paragraph = CaseParagraph("plain");
            var flow = paragraph.CreateFlow();
            var space = new LineSpace(0, 150);
            var cursors = new List<FlowCursor> { flow.Start };
            var first = new List<LineBox>();
            var cursor = flow.Start;
            while (flow.TryNext(cursor, space, out var line, out var next))
            {
                first.Add(line);
                cursors.Add(next);
                cursor = next;
            }

            // Out of order, and twice: the same lines again, and the same cursors.
            foreach (int i in new[] { 3, 0, 2, 3, first.Count - 1, 1 })
            {
                Assert.True(flow.TryNext(cursors[i], space, out var again, out var nextAgain));
                AssertSame(first[i], again);
                Assert.Equal(cursors[i + 1], nextAgain);
            }
        }

        [Fact]
        public void TheSameCursor_InAnotherSpace_GivesAnotherLine()
        {
            var paragraph = CaseParagraph("plain");
            var flow = paragraph.CreateFlow();

            Assert.True(flow.TryNext(flow.Start, new LineSpace(0, 300), out var wide, out var wideNext));
            Assert.True(flow.TryNext(flow.Start, new LineSpace(120, 200, Top: 40), out var narrow, out var narrowNext));
            Assert.True(flow.TryNext(flow.Start, new LineSpace(0, 300), out var wideAgain, out var wideNextAgain));

            Assert.True(narrow.Range.End < wide.Range.End);
            Assert.True(narrow.Left >= 120);
            Assert.Equal(40, narrow.Top, 6);
            Assert.True(narrow.Left + narrow.Width <= 200 + 1e-6);
            Assert.NotEqual(wideNext, narrowNext);
            AssertSame(wide, wideAgain);
            Assert.Equal(wideNext, wideNextAgain);
        }

        [Fact]
        public void TheFlow_DoesNotChangeTheParagraph()
        {
            var paragraph = CaseParagraph("hyphens-auto");
            var before = paragraph.Layout(150);
            var flow = paragraph.CreateFlow();
            var cursor = flow.Start;
            while (flow.TryNext(cursor, new LineSpace(10, 60), out _, out var next))
            {
                cursor = next;
            }

            var after = paragraph.Layout(150);
            Assert.Equal(before.Lines.Count, after.Lines.Count);
            for (int i = 0; i < before.Lines.Count; i++)
            {
                AssertSame(before.Lines[i], after.Lines[i]);
            }
        }

        [Fact]
        public void TheStartCursor_IsTheDefault_AndTheEndIsReportedOnceTheLastLineIsLaidOut()
        {
            var flow = Build("one two").CreateFlow();

            Assert.Equal(default, flow.Start);
            Assert.False(flow.Start.IsEnd);
            Assert.Equal(0, flow.Start.Offset);
            Assert.True(flow.TryNext(flow.Start, new LineSpace(0, 1000), out var line, out var next));
            Assert.Equal(LineEnd.Last, line.End);
            Assert.True(next.IsEnd);
            Assert.Equal(7, next.Offset);
            Assert.False(flow.TryNext(next, new LineSpace(0, 1000), out var none, out var after));
            Assert.Null(none);
            Assert.Equal(next, after);
            Assert.Equal(flow.Start.GetHashCode(), default(FlowCursor).GetHashCode());
            Assert.True(flow.Start == default);
            Assert.True(flow.Start != next);
            Assert.NotEmpty(next.ToString());
        }

        [Fact]
        public void ATrailingNewline_LeavesAnEmptyLastLine_ThenTheEnd()
        {
            var flow = Build("one\n").CreateFlow();

            Assert.True(flow.TryNext(flow.Start, new LineSpace(0, 1000), out var first, out var second));
            Assert.Equal(LineEnd.Forced, first.End);
            Assert.False(second.IsEnd);
            Assert.True(flow.TryNext(second, new LineSpace(0, 1000, Top: first.Height), out var last, out var end));
            Assert.True(last.Range.IsEmpty);
            Assert.True(end.IsEnd);
        }

        [Fact]
        public void TheCountOfHyphenatedLines_TravelsInTheCursor()
        {
            var paragraph = Build("internationalization internationalization", new ParagraphStyle { Hyphens = Hyphens.Auto, LineBreak = English, HyphenateLimitLines = 1 });
            var flow = paragraph.CreateFlow();
            var space = new LineSpace(0, Advance("interna") + Advance("-") + 1);

            Assert.True(flow.TryNext(flow.Start, space, out var first, out var second));
            Assert.Equal(LineEnd.Hyphenated, first.End);
            Assert.True(flow.TryNext(second, space, out var afterOne, out _));
            Assert.NotEqual(LineEnd.Hyphenated, afterOne.End);

            // From the start again, the count is back to nothing: the first line hyphenates again.
            Assert.True(flow.TryNext(flow.Start, space, out var firstAgain, out var secondAgain));
            AssertSame(first, firstAgain);
            Assert.Equal(second, secondAgain);
        }

        [Fact]
        public void TheIndentOfALine_FollowsTheStyle()
        {
            var flow = Build("one\ntwo three four five six", new ParagraphStyle { TextIndent = new TextIndent(20, EachLine: true) }).CreateFlow();

            Assert.Equal(20, flow.GetIndent(flow.Start), 6);
            Assert.True(flow.TryNext(flow.Start, new LineSpace(0, 1000, 20), out _, out var second));
            Assert.Equal(20, flow.GetIndent(second), 6);
            Assert.True(flow.TryNext(second, new LineSpace(0, Advance("two three"), 20), out _, out var third));
            Assert.Equal(0, flow.GetIndent(third), 6);
        }

        [Fact]
        public void TheLineLimit_IsTheCallersToApply()
        {
            var paragraph = Build("alpha beta gamma delta epsilon zeta eta theta", new ParagraphStyle { MaxLines = 1 });

            Assert.True(Flow(paragraph, 80).Count > 2);
            Assert.Single(paragraph.Layout(80).Lines);
        }

        [Fact]
        public void TabStopsAreMeasuredFromTheStartOfTheSpace()
        {
            var paragraph = Build("a\tb", new ParagraphStyle { TabSize = TabSize.FromLength(60) });
            var flow = paragraph.CreateFlow();

            Assert.True(flow.TryNext(flow.Start, new LineSpace(100, 400), out var line, out _));
            Assert.Equal(60 - Advance("a"), line.Runs[1].Width, 3);
            Assert.Equal(100 + 60, line.Runs[2].X, 3);
        }

        [Fact]
        public void AnUnlimitedSpace_IsOneLine_StartAligned()
        {
            var flow = Build("alpha beta gamma\ndelta", new ParagraphStyle { Align = TextAlign.Right }).CreateFlow();

            Assert.True(flow.TryNext(flow.Start, new LineSpace(10, double.PositiveInfinity), out var line, out var next));
            Assert.Equal(LineEnd.Forced, line.End);
            Assert.Equal(10, line.Left, 6);
            Assert.Equal(17, next.Offset);
        }

        [Fact]
        public void ALineThatOverflowsItsSpace_IsStartAligned()
        {
            var flow = Build("Supercalifragilistic", new ParagraphStyle { Align = TextAlign.Center }).CreateFlow();

            Assert.True(flow.TryNext(flow.Start, new LineSpace(50, 80), out var line, out _));
            Assert.Equal(50, line.Left, 6);
        }

        // ---- hostile input ---------------------------------------------------------------------------------------------------------

        public static TheoryData<double, double, double, double> BadSpaces => new()
        {
            { double.NaN, 100, 0, 0 },
            { double.PositiveInfinity, 100, 0, 0 },
            { 0, double.NaN, 0, 0 },
            { 0, 100, double.NaN, 0 },
            { 0, 100, double.PositiveInfinity, 0 },
            { 0, 100, 0, double.NaN },
            { 0, 100, 0, double.NegativeInfinity },
        };

        [Theory]
        [MemberData(nameof(BadSpaces))]
        public void ASpaceThatIsNotANumberOrIsInfiniteWhereItMustNotBe_IsRefused(double left, double right, double indent, double top)
        {
            var flow = Build("one two").CreateFlow();

            Assert.Throws<ArgumentException>(() => flow.TryNext(flow.Start, new LineSpace(left, right, indent, top), out _, out _));
        }

        [Theory]
        [InlineData(0.0, 0.0)]
        [InlineData(100.0, 50.0)]
        [InlineData(0.0, double.NegativeInfinity)]
        [InlineData(1e308, -1e308)]
        [InlineData(-500.0, -400.0)]
        public void ASpaceWithNoWidth_StillMakesProgress_UntilTheEnd(double left, double right)
        {
            var paragraph = Build("alpha beta\tgamma ￼ delta hy­phen", new ParagraphStyle { OverflowWrap = OverflowWrap.Anywhere });
            var flow = paragraph.CreateFlow();
            var cursor = flow.Start;
            int lines = 0;
            Assert.True(flow.TryNext(cursor, new LineSpace(left, right), out var narrow, out _));
            Assert.True(flow.TryNext(cursor, new LineSpace(left, left), out var none, out _));
            if (right <= left)
            {
                Assert.Equal(none.Range, narrow.Range);
            }

            while (flow.TryNext(cursor, new LineSpace(left, right), out var line, out var next))
            {
                Assert.True(next.IsEnd || next.Offset > cursor.Offset, "a line took no text");
                Assert.False(double.IsNaN(line.Width));
                cursor = next;
                Assert.True(++lines <= paragraph.Text.Length + 2);
            }

            Assert.True(cursor.IsEnd);
        }

        [Fact]
        public void APositionThatOverflowsWithItsIndent_IsRefused()
        {
            var flow = Build("one two").CreateFlow();

            Assert.Throws<ArgumentException>(() => flow.TryNext(flow.Start, new LineSpace(1e308, double.PositiveInfinity, 1e308), out _, out _));
        }

        [Fact]
        public void ACursorFromALongerParagraph_IsRefused()
        {
            var longer = Build("a much longer paragraph of text").CreateFlow();
            var shorter = Build("short").CreateFlow();
            Assert.True(longer.TryNext(longer.Start, new LineSpace(0, 1000), out _, out var end));

            Assert.Throws<ArgumentException>(() => shorter.TryNext(end, new LineSpace(0, 1000), out _, out _));
            Assert.Throws<ArgumentException>(() => shorter.GetIndent(end));
        }

        [Fact]
        public void HugeText_AtNoWidth_FlowsQuickly()
        {
            var paragraph = Build(new string('m', 6000), new ParagraphStyle { OverflowWrap = OverflowWrap.Anywhere });

            var lines = Flow(paragraph, 0);

            Assert.Equal(6000, lines.Count);
        }

        [Fact]
        public void TheFlowCanBeUsedFromSeveralThreadsAtOnce()
        {
            var paragraph = CaseParagraph("hyphens-auto");
            var expected = Flow(paragraph, 130);

            Parallel.For(0, 16, _ =>
            {
                var lines = Flow(paragraph, 130);
                Assert.Equal(expected.Count, lines.Count);
                for (int i = 0; i < lines.Count; i++)
                {
                    AssertSame(expected[i], lines[i]);
                }
            });
        }
    }
}
