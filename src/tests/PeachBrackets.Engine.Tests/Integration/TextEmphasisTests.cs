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
        public async Task Style_IsInherited_ButCombineUprightIsNot()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(Wrap(
                "<div style='text-emphasis-style: dot; text-combine-upright: all; text-decoration-inset: 4pt'><p id='c'>x</p></div>"));
            var child = LayoutHarness.FindById(root, "c")!;

            Assert.Equal("dot", child.TextEmphasisStyle);
            Assert.Equal("none", child.TextCombineUpright);
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
    }
}
