using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A <c>display: none</c> sibling generates no box (CSS Display 3 §2.5), so it must not change how the
    /// inline content around it is laid out. The parser builds no implied <c>&lt;html&gt;/&lt;head&gt;/&lt;body&gt;</c>
    /// - a fragment is one flat list under a synthetic root - so head content such as
    /// <c>&lt;style&gt;</c> is a <c>display: none</c> <i>sibling</i> of the bare text after it.
    /// <c>DomUtils.ContainsInlinesOnly</c> counted that sibling as block-level content, so the root took the
    /// block-children path, whose bare text box is never measured or placed: the text vanished (with a
    /// float before it, and blank pages when it was long, since #1205 stopped a float from forcing an
    /// anonymous wrapper that used to hide this).
    /// </summary>
    /// <remarks>
    /// None of these fixtures use <see cref="LayoutHarness.Wrap"/>, which puts the content under an
    /// explicit <c>&lt;body&gt;</c> and so never builds the tree shape that fails.
    /// </remarks>
    public class DisplayNoneAmongInlineContentTests
    {
        private const string Float = "<div id='f' style='float:left;width:80pt;height:40pt'>FLOAT</div>";
        private const string Text = "alpha bravo charlie delta echo foxtrot";

        private static readonly string[] TextWords = Text.Split(' ');

        private static List<CssRect> WordsOf(CssBox root) =>
            LayoutHarness.Descendants(root).SelectMany(b => b.Words).ToList();

        private static CssRect Word(CssBox root, string text) =>
            WordsOf(root).Single(w => w.Text?.Trim() == text);

        private static async Task<List<string>> DrawnTextsAsync(string html, double pageWidth = 595, double pageHeight = 842)
        {
            var (_, container) = await LayoutHarness.LayoutAsync(html, pageWidth, pageHeight);
            var g = new TestRecordingGraphics();

            for (var page = 0; page < container.FragmentTree!.Fragmentainers.Count; page++)
            {
                FragmentPaintHarness.PaintPage(container, g, page);
            }

            return g.DrawStringCalls.Select(c => c.Text.Trim()).ToList();
        }

        [Theory]
        [InlineData("<style>p{margin:0}</style>")]
        [InlineData("<meta charset='utf-8'>")]
        [InlineData("<link rel='stylesheet' href='x.css'>")]
        [InlineData("<head></head>")]
        [InlineData("<head><title>t</title></head>")]
        public async Task FloatThenBareText_AfterHeadContentWithNoBodyTag_FlowsBesideTheFloat(string head)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(head + Float + Text);

            var floatBox = LayoutHarness.FindById(root, "f")!;
            var floatRight = floatBox.ActualRight + floatBox.ActualMarginRight;

            foreach (var expected in TextWords)
            {
                var word = Word(root, expected);
                Assert.NotNull(word.Line);
                Assert.True(word.Width > 0, $"'{expected}' was never measured");
            }

            // The first word shares the float's line and starts at its right edge (CSS 2.1 §9.5, §9.4.2),
            // it is not dropped and it is not pushed under the float.
            var alpha = Word(root, "alpha");
            Assert.True(alpha.Rectangle.Left >= floatRight - 0.5,
                $"'alpha' (X={alpha.Rectangle.Left}) should start at or after the float's right edge ({floatRight})");
            Assert.True(alpha.Rectangle.Top < floatBox.ActualBottom,
                "'alpha' should sit beside the float, not below it");

            Assert.Single(container.FragmentTree!.Fragmentainers);
        }

        [Fact]
        public async Task FloatThenBareText_AfterHeadContentWithNoBodyTag_IsDrawn()
        {
            var drawn = await DrawnTextsAsync("<style>p{margin:0}</style>" + Float + Text);

            Assert.Contains("FLOAT", drawn);
            foreach (var expected in TextWords)
                Assert.Contains(expected, drawn);
        }

        [Fact]
        public async Task LongBareText_AfterHeadContentAndFloat_PaginatesLikeTheSameDocumentWithABodyTag()
        {
            var words = string.Join(' ', Enumerable.Range(1, 400).Select(i => $"w{i}"));

            var (_, withoutBody) = await LayoutHarness.LayoutAsync(
                "<style>p{margin:0}</style>" + Float + words, pageWidth: 200, pageHeight: 150, margin: 10);
            var (_, withBody) = await LayoutHarness.LayoutAsync(
                "<style>p{margin:0}</style><body style='margin:0'>" + Float + words + "</body>", pageWidth: 200, pageHeight: 150, margin: 10);

            // The text used to be dropped, leaving the float and a run of blank pages. The control's body
            // takes margin:0 because a document that omits <body> gets no body rules at all (the UA sheet's
            // own 8px body margin included), so the two only agree once the control gives that margin up.
            Assert.True(withBody.FragmentTree!.Fragmentainers.Count > 1, "the control must paginate");
            Assert.Equal(withBody.FragmentTree!.Fragmentainers.Count, withoutBody.FragmentTree!.Fragmentainers.Count);
        }

        [Fact]
        public async Task LongBareText_AfterHeadContentAndFloat_EveryWordIsDrawnOnce()
        {
            var words = Enumerable.Range(1, 200).Select(i => $"w{i}").ToList();
            var drawn = await DrawnTextsAsync(
                "<style>p{margin:0}</style>" + Float + string.Join(' ', words), pageWidth: 200, pageHeight: 150);

            foreach (var expected in words)
                Assert.Equal(1, drawn.Count(t => t == expected));
        }

        [Fact]
        public async Task BareText_AfterHeadContent_WithNoFloat_IsPlaced()
        {
            // The same defect with no float involved: it predates #1205 and was hidden only when something
            // else in the run happened to force an anonymous wrapper.
            var drawn = await DrawnTextsAsync("<style>p{margin:0}</style>" + Text);

            foreach (var expected in TextWords)
                Assert.Contains(expected, drawn);
        }

        [Fact]
        public async Task BareText_AfterHeadContent_ThenParagraphThenMoreText_KeepsAllThree()
        {
            var drawn = await DrawnTextsAsync("<style>x{}</style>alpha<p>para</p>bravo");

            Assert.Contains("alpha", drawn);
            Assert.Contains("para", drawn);
            Assert.Contains("bravo", drawn);
        }

        [Theory]
        [InlineData("<script>var x = 1;</script>")]
        [InlineData("<span style='display:none;margin:0 50pt;padding:0 50pt;border:5pt solid'>hidden</span>")]
        public async Task HiddenElementBetweenTwoTextRuns_DoesNotSplitOrSpaceTheLine(string hidden)
        {
            // No white space in the source between "alpha", the hidden element and "bravo": the two runs are
            // adjacent, so one line, no break opportunity, and none of the hidden element's own margin,
            // border or padding may advance the cursor between them.
            var (root, _) = await LayoutHarness.LayoutAsync($"<div id='d'>alpha{hidden}bravo</div>");

            var alpha = Word(root, "alpha");
            var bravo = Word(root, "bravo");

            // Placed at all: a word that was never flowed sits at the origin with no width and no line, which
            // would satisfy the adjacency checks below vacuously.
            foreach (var word in new[] { alpha, bravo })
            {
                Assert.NotNull(word.Line);
                Assert.True(word.Width > 0, $"'{word.Text}' was never measured");
            }

            Assert.Equal(LayoutHarness.LineTopOf(root, alpha), LayoutHarness.LineTopOf(root, bravo), 3);
            Assert.True(bravo.Rectangle.Left - alpha.Rectangle.Right < 1,
                $"the hidden element left a {bravo.Rectangle.Left - alpha.Rectangle.Right}pt gap between the runs");
        }

        [Fact]
        public async Task AbsolutelyPositionedBox_AfterHeadContent_DoesNotDropTheBareTextAfterIt()
        {
            var drawn = await DrawnTextsAsync(
                "<style>p{margin:0}</style><div style='position:absolute;left:100pt'>ABS</div>" + Text);

            Assert.Contains("ABS", drawn);
            foreach (var expected in TextWords)
                Assert.Contains(expected, drawn);
        }

        [Fact]
        public async Task BlockInsideInline_AfterHeadContent_StillCorrectsTheTreeAndKeepsBothRuns()
        {
            // [style, span[div], #text] newly reaches the block-inside-inline correction now that the style
            // no longer makes the root look block-level. It must neither throw nor lose either run.
            var drawn = await DrawnTextsAsync("<style>x{}</style><span><div>blk</div></span>alpha bravo");

            Assert.Contains("blk", drawn);
            Assert.Contains("alpha", drawn);
            Assert.Contains("bravo", drawn);
        }

        [Theory]
        [InlineData("<body>{0}{1}</body>")]
        [InlineData("<style>p{{margin:0}}</style><body>{0}{1}</body>")]
        [InlineData("<style>p{{margin:0}}</style><p>{1}</p>{0}")]
        public async Task Controls_WithBodyOrWrappedText_StillDrawEveryWord(string template)
        {
            var drawn = await DrawnTextsAsync(string.Format(template, Float, Text));

            foreach (var expected in TextWords)
                Assert.Contains(expected, drawn);
        }

        [Fact]
        public async Task ContainsInlinesOnly_TreatsADisplayNoneChildAsCompatible()
        {
            var (root, _) = await LayoutHarness.LayoutAsync("<style>p{margin:0}</style>alpha bravo");

            Assert.True(DomUtils.ContainsInlinesOnly(root));
        }

        [Fact]
        public async Task ContainsInlinesOnly_StillFalseForARealBlockChildNextToADisplayNoneOne()
        {
            var (root, _) = await LayoutHarness.LayoutAsync("<style>p{margin:0}</style><div>block</div>alpha");

            Assert.False(DomUtils.ContainsInlinesOnly(root));
        }
    }
}
