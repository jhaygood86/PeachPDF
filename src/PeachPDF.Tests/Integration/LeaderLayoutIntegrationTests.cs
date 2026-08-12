using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Layout-level tests for <c>leader()</c> (css-content-3 §6) - asserts <see cref="CssRectLeader"/>/
    /// neighboring-word <c>Left</c>/<c>Width</c> after real layout, per this repo's layout-engine testing
    /// convention (assert box/rect properties, not just "layout completes"). Uses the same lightweight
    /// <c>HtmlContainerInt</c> + <c>PdfSharpAdapter</c> harness as <c>FlexboxIntegrationTests</c>/
    /// <c>MulticolLayoutIntegrationTests</c> - real layout, no full PDF generation needed. Plain HTML text
    /// content (e.g. "Chapter One"/"12") lives on an anonymous child text box, not the element's own
    /// <see cref="CssBox.Words"/> - <see cref="CssBox.FirstWordOccurence"/> (the same helper
    /// <see cref="CssLineBox.SetBaseLine"/> itself uses) finds it either way, unlike CSS-generated
    /// <c>content:</c> (including <c>leader()</c>), which - having no DOM text node to represent - sets
    /// <see cref="CssBox.Words"/> directly on the box itself.
    /// </summary>
    public class LeaderLayoutIntegrationTests
    {
        [Fact]
        public async Task Leader_Alone_ClaimsFullRemainingWidth()
        {
            var html = Wrap("""
                <div id="line" style="width:300pt;">
                    <span id="leader" style="content: leader(dotted);"></span>
                </div>
                """);

            var (root, _) = await BuildAndLayout(html);
            var lineBox = FindById(root, "line")!;
            var leaderBox = FindById(root, "leader")!;

            var leader = Assert.IsType<CssRectLeader>(Assert.Single(leaderBox.Words));
            Assert.Equal(lineBox.ClientRectangle.Width, leader.Width, 0.5);
        }

        [Fact]
        public async Task Leader_WithTrailingTextInSameBox_ClaimsWidthMinusTrailingText()
        {
            var html = Wrap("""
                <div id="line" style="width:300pt;">
                    <span id="fill" style='content: "Chapter One" leader(dotted) "12";'></span>
                </div>
                """);

            var (root, _) = await BuildAndLayout(html);
            var lineBox = FindById(root, "line")!;
            var fillBox = FindById(root, "fill")!;

            // "Chapter One" is one CSS string-literal content-list item, but still splits into two
            // ordinary words at the space - same as any other text - so this box's own Words holds
            // ["Chapter", "One", leader, "12"], all four set directly on it (a leader-bearing box's
            // content is fully resolved onto its own Words, never a child text box).
            Assert.Equal(4, fillBox.Words.Count);
            var chapterWord = fillBox.Words[0];
            var oneWord = fillBox.Words[1];
            var leader = Assert.IsType<CssRectLeader>(fillBox.Words[2]);
            var pageNumber = fillBox.Words[3];

            var titleWidth = chapterWord.FullWidth + oneWord.FullWidth;
            var expectedLeaderWidth = lineBox.ClientRectangle.Width - titleWidth - pageNumber.FullWidth;
            Assert.Equal(expectedLeaderWidth, leader.Width, 0.5);

            // The trailing word must sit flush against the leader's own resolved right edge, not the
            // zero-width position it flowed at before the fill pass ran.
            Assert.Equal(leader.Left + leader.Width, pageNumber.Left, 0.5);
        }

        [Fact]
        public async Task Leader_WithTrailingContentInSiblingBox_SeesWholeLineNotJustOwnBox()
        {
            // The confirmed full-scope case: leader() lives in one element, the trailing content that
            // must land flush against its resolved right edge lives in a completely different sibling
            // element on the same line - the realistic two-element TOC-entry authoring pattern.
            var html = Wrap("""
                <div id="line" style="width:300pt;">
                    <span id="title">ChapterOne</span><span id="leader" style="content: leader(dotted);"></span><span id="pagenum">12</span>
                </div>
                """);

            var (root, _) = await BuildAndLayout(html);
            var lineBox = FindById(root, "line")!;
            var titleBox = FindById(root, "title")!;
            var leaderBox = FindById(root, "leader")!;
            var pageNumBox = FindById(root, "pagenum")!;

            var titleWord = CssBox.FirstWordOccurence(titleBox, lineBox.LineBoxes[0])!;
            var leader = Assert.IsType<CssRectLeader>(Assert.Single(leaderBox.Words));
            var pageNumWord = CssBox.FirstWordOccurence(pageNumBox, lineBox.LineBoxes[0])!;

            var expectedLeaderWidth = lineBox.ClientRectangle.Width - titleWord.FullWidth - pageNumWord.FullWidth;
            Assert.Equal(expectedLeaderWidth, leader.Width, 0.5);
            Assert.Equal(leader.Left + leader.Width, pageNumWord.Left, 0.5);
        }

        [Fact]
        public async Task TwoLeaders_OnOneLine_ShareRemainingWidthEqually()
        {
            var html = Wrap("""
                <div id="line" style="width:300pt;">
                    <span id="l1" style="content: leader(dotted);"></span><span id="mid">X</span><span id="l2" style="content: leader(dotted);"></span>
                </div>
                """);

            var (root, _) = await BuildAndLayout(html);
            var lineBox = FindById(root, "line")!;
            var l1Box = FindById(root, "l1")!;
            var midBox = FindById(root, "mid")!;
            var l2Box = FindById(root, "l2")!;

            var leader1 = Assert.IsType<CssRectLeader>(Assert.Single(l1Box.Words));
            var midWord = CssBox.FirstWordOccurence(midBox, lineBox.LineBoxes[0])!;
            var leader2 = Assert.IsType<CssRectLeader>(Assert.Single(l2Box.Words));

            var expectedEach = (lineBox.ClientRectangle.Width - midWord.FullWidth) / 2;
            Assert.Equal(expectedEach, leader1.Width, 0.5);
            Assert.Equal(expectedEach, leader2.Width, 0.5);

            // Never a wrap point - both leaders and the middle word stay on the one line.
            Assert.Single(lineBox.LineBoxes);
        }

        [Fact]
        public async Task Leader_OnFirstLineWithFirstLineRule_UsesFirstLineFont()
        {
            // Exercises CssBox.ApplyFirstLineStyleOverride's leader branch: a leader sitting on a
            // block's first formatted line, under a ::first-line rule, must use that rule's own font
            // for its Height (the same as any other word on the line), not the box's own.
            var html = Wrap("""
                <style>#line::first-line { font-size: 24pt; }</style>
                <div id="line" style="width:300pt; font-size:10pt;">
                    <span id="leader" style="content: leader(dotted);"></span>
                </div>
                """);

            var (root, _) = await BuildAndLayout(html);
            var leaderBox = FindById(root, "leader")!;

            var leader = Assert.IsType<CssRectLeader>(Assert.Single(leaderBox.Words));
            Assert.NotNull(leader.FirstLineStyle);
            Assert.Equal(leader.FirstLineStyle!.ActualFont.Height, leader.Height, 0.01);
        }

        private static string Wrap(string body) =>
            $"<!DOCTYPE html><html><head></head><body>{body}</body></html>";

        private static async Task<(CssBox root, HtmlContainerInt container)> BuildAndLayout(string html)
        {
            var adapter = new PdfSharpAdapter();
            adapter.PixelsPerPoint = 1.0;
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return (container.Root!, container);
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            var val = box.HtmlTag?.TryGetAttribute("id", "");
            if (val != null && val.Equals(id, System.StringComparison.OrdinalIgnoreCase))
                return box;
            foreach (var child in box.Boxes)
            {
                var found = FindById(child, id);
                if (found != null) return found;
            }
            return null;
        }
    }
}
