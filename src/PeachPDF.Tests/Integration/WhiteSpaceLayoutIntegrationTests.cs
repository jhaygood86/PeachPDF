using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Verifies <c>white-space</c> actually affects whitespace-collapsing and line-wrapping - it was
    /// fully implemented already (<c>CssBox.ParseToWords</c>/<c>CssLayoutEngine.FlowBox</c>) but had
    /// zero dedicated tests anywhere in the suite before this batch.
    /// </summary>
    public class WhiteSpaceLayoutIntegrationTests
    {
        [Fact]
        public async Task Pre_PreservesMultipleConsecutiveSpacesAsLiteralWord()
        {
            var (root, _) = await BuildAndLayout(Wrap("<p id='p' style='white-space:pre'>A     B</p>"));
            var p = FindById(root, "p")!;
            var words = p.LineBoxes[0].Words;

            Assert.Contains(words, w => w.Text == "     ");
        }

        [Fact]
        public async Task Normal_CollapsesConsecutiveSpaces_NoLiteralSpaceWord()
        {
            var (root, _) = await BuildAndLayout(Wrap("<p id='p'>A     B</p>"));
            var p = FindById(root, "p")!;
            var words = p.LineBoxes[0].Words;

            Assert.DoesNotContain(words, w => w.Text != null && w.Text.Length > 0 && w.Text.All(char.IsWhiteSpace));
        }

        [Fact]
        public async Task Pre_TreatsExplicitNewlineAsForcedLineBreak()
        {
            var (root, _) = await BuildAndLayout(Wrap("<p id='p' style='white-space:pre'>A\nB</p>"));
            var p = FindById(root, "p")!;

            Assert.Equal(2, p.LineBoxes.Count);
        }

        [Fact]
        public async Task Normal_IgnoresEmbeddedNewline_NoForcedBreak()
        {
            var (root, _) = await BuildAndLayout(Wrap("<p id='p'>A\nB</p>"));
            var p = FindById(root, "p")!;

            Assert.Single(p.LineBoxes);
        }

        [Fact]
        public async Task NoWrap_PreventsWrapping_EvenWhenNarrowerThanContent()
        {
            var html = Wrap("<p id='p' style='white-space:nowrap; width:50px'>a long run of unwrapped text here</p>");
            var (root, _) = await BuildAndLayout(html);
            var p = FindById(root, "p")!;

            Assert.Single(p.LineBoxes);
        }

        [Fact]
        public async Task Normal_WrapsAtNarrowWidth_ForContrastWithNoWrap()
        {
            var html = Wrap("<p id='p' style='width:50px'>a long run of unwrapped text here</p>");
            var (root, _) = await BuildAndLayout(html);
            var p = FindById(root, "p")!;

            Assert.True(p.LineBoxes.Count > 1);
        }

        // ─── &nbsp; (U+00A0) is significant, non-collapsible, non-breaking content - unlike ordinary
        // whitespace, which stays collapsible/breakable (CSS2.1 §16.4.1) ───────────

        [Fact]
        public async Task Nbsp_OnlyContent_ProducesNonZeroHeight_MatchingRealText()
        {
            var (nbspRoot, _) = await BuildAndLayout(Wrap("<div id='b'>&nbsp;</div>"));
            var (textRoot, _) = await BuildAndLayout(Wrap("<div id='b'>A</div>"));
            var nbspBox = FindById(nbspRoot, "b")!;
            var textBox = FindById(textRoot, "b")!;

            var nbspHeight = nbspBox.ActualBottom - nbspBox.Location.Y;
            var textHeight = textBox.ActualBottom - textBox.Location.Y;

            Assert.True(nbspHeight > 0, $"Expected non-zero height for nbsp-only content, got {nbspHeight}");
            Assert.InRange(nbspHeight, textHeight - 1, textHeight + 1);
        }

        [Fact]
        public async Task OrdinaryWhitespaceOnlyContent_StillProducesZeroHeight_NoRegression()
        {
            var (root, _) = await BuildAndLayout(Wrap("<div id='b'>   </div>"));
            var box = FindById(root, "b")!;

            Assert.InRange(box.ActualBottom - box.Location.Y, 0, 0.5);
        }

        [Fact]
        public async Task Nbsp_BetweenTokens_PreventsLineWrap_ContrastOrdinarySpace()
        {
            // Narrow enough that an ordinary space between "10" and "km" wraps to two lines, but a
            // non-breaking space between them must never be treated as a break opportunity.
            // "10 km" as one unbreakable unit is ~30pt wide (measured separately) - 35pt comfortably
            // fits it on one line without any wrap. 15pt is narrower than that but still wider than "10"
            // alone (~12pt), so the breakable version must wrap after "10".
            var (nbspRoot, _) = await BuildAndLayout(Wrap("<p id='p' style='width:35pt'>10&nbsp;km</p>"));
            var pNbsp = FindById(nbspRoot, "p")!;

            var (spaceRoot, _) = await BuildAndLayout(Wrap("<p id='p' style='width:15pt'>10 km</p>"));
            var pSpace = FindById(spaceRoot, "p")!;

            Assert.Single(pNbsp.LineBoxes);
            Assert.True(pSpace.LineBoxes.Count > 1,
                "expected ordinary space to still allow wrapping, for contrast with nbsp");
        }

        // ─── word-break: break-all forces a mid-word break normal cannot find ──────

        [Fact]
        public async Task BreakAll_ForcesMidWordBreak_ContrastNormal()
        {
            // A single unbroken run with no space anywhere: "normal" has no break opportunity at all
            // and must lay the whole word out on one (overflowing) line, while "break-all" must wrap it.
            const string longWord = "abcdefghijklmnopqrstuvwxyz";

            var (normalRoot, _) = await BuildAndLayout(Wrap($"<p id='p' style='width:50pt'>{longWord}</p>"));
            var pNormal = FindById(normalRoot, "p")!;

            var (breakAllRoot, _) = await BuildAndLayout(
                Wrap($"<p id='p' style='width:50pt; word-break:break-all'>{longWord}</p>"));
            var pBreakAll = FindById(breakAllRoot, "p")!;

            // An overflowing word can push a leading empty line box ahead of it regardless of
            // word-break - count only the lines that actually carry part of the word.
            static int LinesWithWordContent(CssBox box) =>
                box.LineBoxes.Count(lb => lb.Words.Any(w => !string.IsNullOrEmpty(w.Text)));

            Assert.Equal(1, LinesWithWordContent(pNormal));
            Assert.True(LinesWithWordContent(pBreakAll) > 1, "expected break-all to force a mid-word break");
        }

        // ─── Phase II: a collapsible space at the beginning of a line is removed ──

        [Fact]
        public async Task CollapsibleWhitespaceAfterForcedBreak_DoesNotIndentTheLineItOpens()
        {
            // The newline between the <br> and the <span> after it is collapsible white space that
            // begins the line the <br> opened, so css-text-3 phase II removes it: formatting the
            // source across lines must not indent the rendered content (issue #1087).
            var (root, _) = await BuildAndLayout(
                Wrap("<p id='p'><span>A</span><br>\n<span>B</span></p>"));
            var p = FindById(root, "p")!;

            var a = WordNamed(p, "A");
            var b = WordNamed(p, "B");

            Assert.True(b.Top > a.Top, "expected B on the line the <br> opened");
            Assert.Equal(a.Left, b.Left, 3);
        }

        [Fact]
        public async Task CollapsibleWhitespaceAfterForcedBreak_MatchesTheSameMarkupMinified()
        {
            // The same document with and without the source newline after each <br> must lay out
            // identically - the multiline form used to shift every post-<br> line right by one space.
            const string row1 = "<div style='display:inline-block;width:450px'>Field name:</div>"
                                + "<div style='display:inline-block'>short</div>";
            const string row2 = "<div style='display:inline-block;width:450px'>Long field name:</div>"
                                + "<div style='display:inline-block'>long</div>";

            var (multilineRoot, _) = await BuildAndLayout(Wrap($"{row1}\n<br>\n{row2}"));
            var (minifiedRoot, _) = await BuildAndLayout(Wrap($"{row1}<br>{row2}"));

            // Both boxes of the second row - the label the line opens with, and the value 450px
            // past it - must sit exactly where the minified form puts them.
            Assert.Equal(WordNamed(minifiedRoot, "Long").Left, WordNamed(multilineRoot, "Long").Left, 3);
            Assert.Equal(WordNamed(minifiedRoot, "long").Left, WordNamed(multilineRoot, "long").Left, 3);
            Assert.Equal(WordNamed(multilineRoot, "Field").Left, WordNamed(multilineRoot, "Long").Left, 3);
        }

        [Fact]
        public async Task LeadingWhitespaceInsideAnInlineAfterForcedBreak_IsRemoved()
        {
            // The same collapsed space, in the other shape it takes: inside the following inline
            // rather than in a white-space-only box of its own.
            var (root, _) = await BuildAndLayout(Wrap("<p id='p'><span>A</span><br><span> B</span></p>"));
            var p = FindById(root, "p")!;

            Assert.Equal(WordNamed(p, "A").Left, WordNamed(p, "B").Left, 3);
        }

        [Fact]
        public async Task RemovedLineStartWhitespace_IsNotAJustificationOpportunity()
        {
            // A space that isn't rendered isn't a word separator either - it must not hand
            // text-align: justify an expansion opportunity at the head of the line.
            var (root, _) = await BuildAndLayout(
                Wrap("<p id='p'><span>A</span><br>\n<span>B</span></p>"));
            var p = FindById(root, "p")!;

            Assert.False(WordNamed(p, "B").PrecededByWordSeparator);
        }

        [Fact]
        public async Task AForcedBreak_IsNotAJustificationOpportunity_SoTheLineItOpensStartsFlush()
        {
            // The positional counterpart of the assertion above, and the one that says the whole
            // removal actually reaches the page. Two separate rules meet here: the removed space is
            // not a word separator, and the <br>'s own marker word - whose text is a newline, so
            // CssRect.IsSpaces is true of it - is not one either. Either one alone still expands the
            // head of the line the <br> opens, which indents it with no source white space at all.
            var html = Wrap("<p id='p' style='width:300pt; text-align:justify; font-size:12pt'>"
                            + "first line here<br>\nalpha beta gamma delta epsilon zeta eta theta "
                            + "iota kappa</p>");
            var (root, _) = await BuildAndLayout(html);
            var p = FindById(root, "p")!;

            // Line 2 is a justified (non-final) line, so it is stretched - it must still begin flush
            // with line 1 and end flush at the measure.
            Assert.Equal(WordNamed(p, "first").Left, WordNamed(p, "alpha").Left, 3);
            Assert.True(WordNamed(p, "alpha").Top > WordNamed(p, "first").Top,
                "expected alpha on the line the <br> opened");
            Assert.True(WordNamed(p, "kappa").Top > WordNamed(p, "alpha").Top,
                "expected line 2 to be a stretched, non-final line");
        }

        [Fact]
        public async Task CollapsibleWhitespaceBetweenTwoInlines_StillSeparatesThemMidLine()
        {
            // The counterpart the removal must not reach: the same white-space-only box, this time
            // between two words already on the line, is a real word separator.
            var (root, _) = await BuildAndLayout(Wrap("<p id='p'><span>AA</span> <span>BB</span></p>"));
            var p = FindById(root, "p")!;

            var aa = WordNamed(p, "AA");
            var bb = WordNamed(p, "BB");

            Assert.True(bb.Left > aa.Left + aa.Width,
                "expected a rendered gap between two inlines separated by source white space");
            Assert.True(bb.PrecededByWordSeparator);
        }

        [Fact]
        public async Task CollapsibleWhitespaceAfterAnAtomicInline_StillSeparatesIt()
        {
            // An atomic inline-level box contributes no word to the line, so "has anything been
            // placed here yet" has to consult the line's rectangles as well - otherwise the space
            // after one reads as line-leading and disappears.
            var (root, _) = await BuildAndLayout(Wrap(
                "<p id='p'><span id='a' style='display:inline-block'><div>AA</div></span> <span>BB</span></p>"));
            var p = FindById(root, "p")!;
            var a = FindById(root, "a")!;

            Assert.True(WordNamed(p, "BB").Left > a.ActualRight,
                "expected the space after the inline-block to still be rendered");
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

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

        /// <summary>
        /// The first word in tree order whose text is <paramref name="text"/>, anywhere under
        /// <paramref name="box"/> - so a fixture that asserts on one must not repeat that token.
        /// </summary>
        private static CssRect WordNamed(CssBox box, string text)
        {
            var found = FindWord(box, text);
            Assert.NotNull(found);
            return found!;
        }

        private static CssRect? FindWord(CssBox box, string text)
        {
            foreach (var word in box.Words)
            {
                if (word.Text == text) return word;
            }

            foreach (var child in box.Boxes)
            {
                if (FindWord(child, text) is { } found) return found;
            }

            return null;
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
