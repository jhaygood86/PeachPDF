using PeachBrackets.Css;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Paint;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// CSS Text Decoration 4 §4 (<c>text-emphasis-*</c>) and §2 (<c>text-decoration-inset</c>/<c>-skip</c>), plus
    /// <c>text-combine-upright</c>: the declarations reach the box, and the painter draws what they ask for.
    /// </summary>
    public class TextEmphasisTests
    {
        private static string Wrap(string body) =>
            $"<!DOCTYPE html><html><head></head><body style='margin:0'>{body}</body></html>";

        private static async Task<(CssBox Box, TestRecordingGraphics Graphics)> PaintAsync(string body, string id)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(Wrap(body));
            var box = LayoutHarness.FindById(root, id)!;
            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, box, g);
            return (box, g);
        }

        [Fact]
        public async Task Shorthand_SetsStyleAndColor_InEitherOrder()
        {
            var (box, _) = await PaintAsync("<div id='d' style='text-emphasis: filled sesame #0000ff'>x</div>", "d");
            var (reversed, _) = await PaintAsync("<div id='d' style='text-emphasis: #0000ff open triangle'>x</div>", "d");

            Assert.Equal("filled sesame", box.TextEmphasisStyle);
            Assert.Equal("rgb(0, 0, 255)", box.TextEmphasisColor);
            Assert.Equal("open triangle", reversed.TextEmphasisStyle);
            Assert.Equal("rgb(0, 0, 255)", reversed.TextEmphasisColor);
        }

        [Fact]
        public async Task Shorthand_ResetsAnOmittedLonghand()
        {
            var (box, _) = await PaintAsync("<div id='d' style='text-emphasis-color: red; text-emphasis: circle'>x</div>", "d");

            Assert.Equal("circle", box.TextEmphasisStyle);
            Assert.Equal("currentcolor", box.TextEmphasisColor);
        }

        [Fact]
        public async Task InvalidStyle_IsRejected()
        {
            var (box, _) = await PaintAsync("<div id='d' style='text-emphasis-style: circle triangle'>x</div>", "d");

            Assert.Equal("none", box.TextEmphasisStyle);
        }

        [Fact]
        public async Task Style_AndCombineUpright_AreInherited_ButInsetIsNot()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(Wrap(
                "<div style='text-emphasis-style: dot; text-combine-upright: all; text-decoration-inset: 4pt'><p id='c'>x</p></div>"));
            var child = LayoutHarness.FindById(root, "c")!;

            Assert.Equal("dot", child.TextEmphasisStyle);
            Assert.Equal("all", child.TextCombineUpright);
            Assert.Equal("auto", child.TextDecorationInset);
        }

        [Fact]
        public async Task SkipAndCombineUpright_StoreTheirValues()
        {
            var (box, _) = await PaintAsync(
                "<div id='d' style='text-decoration-skip: objects spaces; text-combine-upright: digits 3'>x</div>", "d");

            Assert.Equal("objects spaces", box.TextDecorationSkip);
            Assert.Equal("digits 3", box.TextCombineUpright);
        }

        [Theory]
        [InlineData("text-decoration-skip: spaces leading-spaces")]
        [InlineData("text-decoration-skip: bogus")]
        [InlineData("text-decoration-inset: 1pt 2pt 3pt")]
        [InlineData("text-decoration-inset: red")]
        [InlineData("text-combine-upright: digits 5")]
        [InlineData("text-emphasis-position: left")]
        [InlineData("text-emphasis-position: over under")]
        public async Task InvalidValues_AreRejected(string declaration)
        {
            var (box, _) = await PaintAsync($"<div id='d' style='{declaration}'>x</div>", "d");

            Assert.Equal("objects", box.TextDecorationSkip);
            Assert.Equal("auto", box.TextDecorationInset);
            Assert.Equal("none", box.TextCombineUpright);
            Assert.Equal("over right", box.TextEmphasisPosition);
        }

        [Fact]
        public async Task Marks_AreDrawnOncePerCharacter_NotForSpaces()
        {
            var (_, g) = await PaintAsync("<div id='d' style='text-emphasis: circle red'>ab c</div>", "d");

            var marks = g.DrawStringCalls.Where(c => c.Text == "●").ToList();

            Assert.Equal(3, marks.Count);
            Assert.All(marks, m => Assert.Equal(255, m.PaintColor.R));
        }

        [Fact]
        public async Task OpenSesame_UsesItsOwnGlyph()
        {
            var (_, g) = await PaintAsync("<div id='d' style='text-emphasis-style: open sesame'>a</div>", "d");

            Assert.Single(g.DrawStringCalls, c => c.Text == "﹆");
        }

        [Fact]
        public async Task CustomString_IsTheMark()
        {
            var (_, g) = await PaintAsync("<div id='d' style='text-emphasis-style: \"*\"'>ab</div>", "d");

            Assert.Equal(2, g.DrawStringCalls.Count(c => c.Text == "*"));
        }

        [Fact]
        public async Task None_DrawsNoMarks()
        {
            var (_, g) = await PaintAsync("<div id='d'>ab</div>", "d");

            Assert.DoesNotContain(g.DrawStringCalls, c => c.Text == "●" || c.Text == "•");
        }

        [Fact]
        public async Task Over_PutsTheMarkAboveTheText_AndUnderBelowIt()
        {
            var (_, over) = await PaintAsync("<div id='d' style='text-emphasis-style: dot'>a</div>", "d");
            var (_, under) = await PaintAsync("<div id='d' style='text-emphasis-style: dot; text-emphasis-position: under'>a</div>", "d");

            var text = over.DrawStringCalls.Single(c => c.Text == "a");
            var overMark = over.DrawStringCalls.Single(c => c.Text == "•");
            var underMark = under.DrawStringCalls.Single(c => c.Text == "•");

            Assert.True(overMark.PaintPoint.Y < text.PaintPoint.Y, "an over mark sits above the text");
            Assert.True(underMark.PaintPoint.Y > text.PaintPoint.Y, "an under mark sits below the top of the text");
            Assert.True(overMark.Font.Size < text.Font.Size, "a mark is smaller than the text");
        }

        [Fact]
        public async Task Inset_PullsBothEndsOfTheLineIn()
        {
            var (_, plain) = await PaintAsync("<div id='d' style='width:300pt; text-decoration: underline'>abc</div>", "d");
            var (_, inset) = await PaintAsync("<div id='d' style='width:300pt; text-decoration: underline; text-decoration-inset: 3pt 5pt'>abc</div>", "d");

            var a = plain.Log.OfType<TestRecordingGraphics.DrawLineCall>().First();
            var b = inset.Log.OfType<TestRecordingGraphics.DrawLineCall>().First();

            Assert.Equal(a.X1 + 3, b.X1, 2);
            Assert.Equal(a.X2 - 5, b.X2, 2);
        }

        [Fact]
        public async Task Inset_OneValueInsetsBothEnds()
        {
            var (_, plain) = await PaintAsync("<div id='d' style='width:300pt; text-decoration: underline'>abc</div>", "d");
            var (_, inset) = await PaintAsync("<div id='d' style='width:300pt; text-decoration: underline; text-decoration-inset: 2pt'>abc</div>", "d");

            var a = plain.Log.OfType<TestRecordingGraphics.DrawLineCall>().First();
            var b = inset.Log.OfType<TestRecordingGraphics.DrawLineCall>().First();

            Assert.Equal(a.X1 + 2, b.X1, 2);
            Assert.Equal(a.X2 - 2, b.X2, 2);
        }

        [Fact]
        public async Task Inset_LongerThanTheLine_DrawsNothing()
        {
            var (_, g) = await PaintAsync("<div id='d' style='width:300pt; text-decoration: underline; text-decoration-inset: 200pt'>abc</div>", "d");

            Assert.Empty(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
        }

        [Fact]
        public async Task AnOverMarkMakesRoomOnATightLine()
        {
            // line-height 1: no leading to hold a mark, so the line has to grow above the baseline by the mark's height.
            var (plain, _) = await PaintAsync("<div id='d' style='font-size:20pt;line-height:1'>ab</div>", "d");
            var (marked, _) = await PaintAsync("<div id='d' style='font-size:20pt;line-height:1;text-emphasis-style:dot'>ab</div>", "d");
            var (under, _) = await PaintAsync("<div id='d' style='font-size:20pt;line-height:1;text-emphasis-style:dot;text-emphasis-position:under right'>ab</div>", "d");

            Assert.True(marked.ActualBottom - marked.Location.Y > plain.ActualBottom - plain.Location.Y + 1);
            Assert.True(under.ActualBottom - under.Location.Y > plain.ActualBottom - plain.Location.Y + 1);
            Assert.True(marked.LineBoxes[0].BaselineExtent!.Value.AboveBaseline > plain.LineBoxes[0].BaselineExtent!.Value.AboveBaseline);
            Assert.Equal(plain.LineBoxes[0].BaselineExtent!.Value.BelowBaseline, marked.LineBoxes[0].BaselineExtent!.Value.BelowBaseline, 3);
        }

        [Fact]
        public async Task LeadingThatAlreadyHoldsTheMarkIsNotAddedTo()
        {
            var (plain, _) = await PaintAsync("<div id='d' style='font-size:10pt;line-height:4'>ab</div>", "d");
            var (marked, _) = await PaintAsync("<div id='d' style='font-size:10pt;line-height:4;text-emphasis-style:dot'>ab</div>", "d");

            Assert.Equal(plain.ActualBottom - plain.Location.Y, marked.ActualBottom - marked.Location.Y, 3);
        }

        [Fact]
        public async Task VerticalText_DrawsTheMarksBesideEachCharacter_OnTheRightByDefault()
        {
            var (_, g) = await PaintAsync(
                "<div id='d' style='writing-mode:vertical-rl;text-orientation:upright;font-size:20pt;text-emphasis-style:circle'>abc</div>", "d");

            var marks = g.DrawStringCalls.Where(c => c.Text == "●").ToList();
            var letters = g.DrawStringCalls.Where(c => c.Text is "a" or "b" or "c").ToList();

            Assert.Equal(3, marks.Count);
            Assert.All(marks, m => Assert.True(m.PaintPoint.X >= letters[0].PaintPoint.X + letters[0].Font.Size / 4));
        }

        [Fact]
        public async Task VerticalText_LeftPutsTheMarksOnTheOtherSide()
        {
            var (_, right) = await PaintAsync(
                "<div id='d' style='writing-mode:vertical-rl;text-orientation:upright;font-size:20pt;text-emphasis-style:circle'>abc</div>", "d");
            var (_, left) = await PaintAsync(
                "<div id='d' style='writing-mode:vertical-rl;text-orientation:upright;font-size:20pt;text-emphasis-style:circle;text-emphasis-position:over left'>abc</div>", "d");

            Assert.True(left.DrawStringCalls.First(c => c.Text == "●").PaintPoint.X < right.DrawStringCalls.First(c => c.Text == "●").PaintPoint.X);
        }

        [Fact]
        public async Task VerticalText_AMarkReservesRoomInTheColumn()
        {
            var (plain, _) = await PaintAsync("<div id='d' style='writing-mode:vertical-rl;font-size:20pt;line-height:1'>abc</div>", "d");
            var (marked, _) = await PaintAsync("<div id='d' style='writing-mode:vertical-rl;font-size:20pt;line-height:1;text-emphasis-style:circle'>abc</div>", "d");

            Assert.True(marked.ActualRight - marked.Location.X > plain.ActualRight - plain.Location.X + 1);
        }

        [Fact]
        public async Task TextShadow_AppliesToTheMarksToo()
        {
            var (_, plain) = await PaintAsync("<div id='d' style='text-emphasis-style:circle'>a</div>", "d");
            var (_, shadowed) = await PaintAsync("<div id='d' style='text-emphasis-style:circle;text-shadow:2pt 2pt 0 #00f'>a</div>", "d");

            Assert.True(shadowed.DrawStringCalls.Count(c => c.Text == "●") > plain.DrawStringCalls.Count(c => c.Text == "●"));
        }

        [Fact]
        public async Task CombineUpright_All_SetsTheRunAsOneEmSquareCell()
        {
            var (_, g) = await PaintAsync("<div id='d' style='writing-mode:vertical-rl;text-orientation:upright;font-size:20pt'>a<span style='text-combine-upright:all'>2024</span>b</div>", "d");

            // One DrawString for the whole run, where upright text is drawn a character at a time.
            Assert.Single(g.DrawStringCalls, c => c.Text == "2024");
        }

        [Fact]
        public async Task CombineUpright_Digits_ComposesOnlyRunsUpToTheLimit()
        {
            var (_, g) = await PaintAsync("<div id='d' style='writing-mode:vertical-rl;text-orientation:upright;font-size:20pt;text-combine-upright:digits 3'>x12 y1234</div>", "d");

            Assert.Contains(g.DrawStringCalls, c => c.Text == "12");
            Assert.DoesNotContain(g.DrawStringCalls, c => c.Text == "1234");
        }

        [Fact]
        public async Task CombineUpright_TakesOneEmAlongTheColumn()
        {
            var (plain, _) = await PaintAsync("<div id='d' style='writing-mode:vertical-rl;text-orientation:upright;font-size:20pt'>2024</div>", "d");
            var (combined, _) = await PaintAsync("<div id='d' style='writing-mode:vertical-rl;text-orientation:upright;font-size:20pt;text-combine-upright:all'>2024</div>", "d");

            Assert.True(combined.ActualBottom - combined.Location.Y < plain.ActualBottom - plain.Location.Y);
            Assert.Equal(20.0, combined.ActualBottom - combined.Location.Y, 0);
        }

        [Fact]
        public async Task CombineUpright_IsInertInHorizontalText()
        {
            var (_, g) = await PaintAsync("<div id='d' style='text-combine-upright:all'>2024</div>", "d");

            Assert.Single(g.DrawStringCalls, c => c.Text == "2024");
        }

        [Fact]
        public async Task SkipSpaces_BreaksTheLineAtTheGapsBetweenWords()
        {
            var (_, plain) = await PaintAsync("<div id='d' style='width:300pt;text-decoration:underline;text-decoration-skip-ink:none'>ab cd</div>", "d");
            var (_, spaces) = await PaintAsync("<div id='d' style='width:300pt;text-decoration:underline;text-decoration-skip-ink:none;text-decoration-skip:spaces'>ab cd</div>", "d");

            Assert.Single(plain.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            Assert.Equal(2, spaces.Log.OfType<TestRecordingGraphics.DrawLineCall>().Count());
        }

        [Fact]
        public async Task SkipNone_DrawsTheLineThroughAnAtomicInline()
        {
            var (_, objects) = await PaintAsync("<div id='d' style='width:300pt;text-decoration:underline;text-decoration-skip-ink:none'>ab <span style='display:inline-block;width:20pt;height:10pt'></span> cd</div>", "d");
            var (_, none) = await PaintAsync("<div id='d' style='width:300pt;text-decoration:underline;text-decoration-skip-ink:none;text-decoration-skip:none'>ab <span style='display:inline-block;width:20pt;height:10pt'></span> cd</div>", "d");

            Assert.True(objects.Log.OfType<TestRecordingGraphics.DrawLineCall>().Count() > none.Log.OfType<TestRecordingGraphics.DrawLineCall>().Count());
        }

        [Fact]
        public async Task SkipEdges_ShortensBothEnds()
        {
            var (_, plain) = await PaintAsync("<div id='d' style='width:300pt;text-decoration:underline'>abc</div>", "d");
            var (_, edges) = await PaintAsync("<div id='d' style='width:300pt;text-decoration:underline;text-decoration-skip:edges'>abc</div>", "d");

            var a = plain.Log.OfType<TestRecordingGraphics.DrawLineCall>().First();
            var b = edges.Log.OfType<TestRecordingGraphics.DrawLineCall>().First();

            Assert.True(b.X1 > a.X1);
            Assert.True(b.X2 < a.X2);
        }

        [Fact]
        public async Task SkipBoxDecoration_LeavesAnInlineBoxsPaddingUndecorated()
        {
            var (_, plain) = await PaintAsync("<div id='d' style='width:300pt;text-decoration:underline;text-decoration-skip-ink:none'>a<span style='padding:0 10pt'>b</span>c</div>", "d");
            var (_, skipped) = await PaintAsync("<div id='d' style='width:300pt;text-decoration:underline;text-decoration-skip-ink:none;text-decoration-skip:box-decoration'>a<span style='padding:0 10pt'>b</span>c</div>", "d");

            Assert.True(skipped.Log.OfType<TestRecordingGraphics.DrawLineCall>().Count() > plain.Log.OfType<TestRecordingGraphics.DrawLineCall>().Count());
        }
    }
}
