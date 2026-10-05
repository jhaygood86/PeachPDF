using PeachDrawing.Core;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragmentation;
using PeachPDF.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// css-overflow-3 §3: <c>overflow</c> is a shorthand of <c>overflow-x</c> and <c>overflow-y</c>, and
    /// <c>clip</c> clips like <c>hidden</c> without making a scroll container. A box whose text runs past
    /// its width must show only the part inside it for each of these.
    /// </summary>
    public class OverflowAxisClipTests
    {
        private const string Word = "abcdefghijklmnopqrstuvwxyz";

        private static string Document(string overflow) =>
            "<!DOCTYPE html><html><body style='margin:0;font:12pt Arial'><div><span id='box' style='display:inline-block;width:60pt;"
            + overflow + ";white-space:nowrap;background:#ddd'>" + Word + "</span></div></body></html>";

        [Theory]
        [InlineData("overflow:clip")]
        [InlineData("overflow:hidden")]
        [InlineData("overflow-x:hidden")]
        [InlineData("overflow-x:clip")]
        [InlineData("overflow:hidden visible")]
        public async Task TextRunningPastTheBox_ShowsOnlyThePartInsideIt(string overflow)
        {
            var (_, container) = await PdfGeneratorLayoutHarness.LayoutAsync(Document(overflow), new PdfGenerateConfig { PageSize = PageSize.Letter });

            var fraction = VisibleAreaFraction(container, Word);

            Assert.InRange(fraction, 0.05, 0.6);
        }

        [Fact]
        public async Task TextRunningPastAVisibleBox_IsNotClipped()
        {
            var (_, container) = await PdfGeneratorLayoutHarness.LayoutAsync(
                Document("overflow:visible"), new PdfGenerateConfig { PageSize = PageSize.Letter });

            Assert.InRange(VisibleAreaFraction(container, Word), 0.95, 1.05);
        }

        // §3.2: clip cuts at the overflow clip edge on its own axis only, so beside a visible axis the other
        // direction's overflow still shows. The box is one line tall, so "below" is past its bottom edge.
        [Theory]
        [InlineData("overflow-x:clip", true, false)]
        [InlineData("overflow-y:clip", false, true)]
        [InlineData("overflow:clip", true, true)]
        public async Task AClipOnOneAxis_LeavesTheOtherAxisOpen(string overflow, bool clipsAcross, bool clipsDown)
        {
            var html = "<!DOCTYPE html><html><body style='margin:0;font:12pt Arial'><div style='width:60pt;height:14pt;" + overflow
                + "'><span style='white-space:nowrap'>" + Word + "</span><br>below</div></body></html>";
            var (_, container) = await PdfGeneratorLayoutHarness.LayoutAsync(html, new PdfGenerateConfig { PageSize = PageSize.Letter });

            var across = VisibleAreaFraction(container, Word);
            var down = VisibleAreaFraction(container, "below");

            Assert.True(clipsAcross ? across < 0.6 : across > 0.95, $"the long word shows {across:0.00} of its area");
            Assert.True(clipsDown ? down < 0.1 : down > 0.95, $"the second line shows {down:0.00} of its area");
        }

        [Theory]
        [InlineData("overflow:hidden", "hidden", "hidden")]
        [InlineData("overflow:clip", "clip", "clip")]
        [InlineData("overflow:hidden clip", "hidden", "clip")]
        [InlineData("overflow:clip scroll", "clip", "scroll")]
        [InlineData("overflow-x:auto", "auto", "visible")]
        [InlineData("overflow:hidden;overflow-y:visible", "hidden", "visible")]
        public async Task TheShorthand_SetsBothAxes(string css, string x, string y)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(Document(css), 400, 400);
            var box = LayoutHarness.FindById(root, "box")!;

            Assert.Equal(x, box.OverflowX.ToString());
            Assert.Equal(y, box.OverflowY.ToString());
        }

        // §3.2: visible beside a scroll container computes to auto and clip to hidden, so the box is a
        // scroll container (monolithic) whichever axis asked for it; clip alone is not one.
        [Theory]
        [InlineData("overflow:clip", false)]
        [InlineData("overflow:clip visible", false)]
        [InlineData("overflow:visible", false)]
        [InlineData("overflow-x:hidden", true)]
        [InlineData("overflow:clip hidden", true)]
        [InlineData("overflow:visible scroll", true)]
        public async Task OnlyAHiddenScrollOrAutoAxis_MakesAScrollContainer(string css, bool scrollContainer)
        {
            var html = "<!DOCTYPE html><html><body><div id='box' style='height:50pt;" + css + "'>text</div></body></html>";
            var (root, _) = await LayoutHarness.LayoutAsync(html, 400, 400);
            var box = LayoutHarness.FindById(root, "box")!;

            Assert.Equal(scrollContainer, MonolithicContent.IsScrollContainer(box));
        }

        // The visible area of the drawn string, as a fraction of its own, inside every rectangle clip in force.
        private static double VisibleAreaFraction(HtmlContainerInt container, string text)
        {
            var total = 0.0;

            for (var page = 0; page < container.FragmentTree!.Fragmentainers.Count; page++)
            {
                var recording = new RecordingGraphics(new PeachPDF.Adapters.PdfSharpAdapter());
                FragmentPaintHarness.PaintPage(container, recording, page);

                var clips = new Stack<Rect?>();
                foreach (var op in recording.Log)
                {
                    switch (op.Kind)
                    {
                        case PaintOpKind.PushClip:
                            clips.Push(op.Bounds);
                            break;
                        case PaintOpKind.PushClipPath:
                            clips.Push(null);
                            break;
                        case PaintOpKind.PopClip when clips.Count > 0:
                            clips.Pop();
                            break;
                        case PaintOpKind.DrawString when op.Text == text && op.Bounds.Width > 0 && op.Bounds.Height > 0:
                            var left = op.Bounds.Left;
                            var right = op.Bounds.Right;
                            var top = op.Bounds.Top;
                            var bottom = op.Bounds.Bottom;
                            foreach (var clip in clips.OfType<Rect>())
                            {
                                left = Math.Max(left, clip.Left);
                                right = Math.Min(right, clip.Right);
                                top = Math.Max(top, clip.Top);
                                bottom = Math.Min(bottom, clip.Bottom);
                            }

                            total += Math.Max(0, right - left) * Math.Max(0, bottom - top) / (op.Bounds.Width * op.Bounds.Height);
                            break;
                    }
                }
            }

            return total;
        }
    }
}
