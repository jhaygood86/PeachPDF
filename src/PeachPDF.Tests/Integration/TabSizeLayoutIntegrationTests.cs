using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Verifies <c>tab-size</c> (CSS Text 4 §3.6, https://www.w3.org/TR/css-text-4/#tab-size-property)
    /// actually expands a preserved tab character to the declared tab-stop width, rather than leaving it
    /// as a literal U+0009 that <c>CssBox.MeasureWordsSize</c>/<c>FragmentPainter</c> would otherwise
    /// measure/draw as an ordinary (font-dependent, meaningless) glyph. A tab at the very start of a
    /// <c>&lt;pre&gt;</c> line - the motivating "indented source code" use case - expands to exactly N
    /// literal space characters for a bare-number tab-size, since the target stop is by construction an
    /// exact multiple of the measured space width (see <c>CssBox.ExpandTabs</c>) - that makes the
    /// resulting word's character count a font-independent assertion, unlike its pixel width.
    /// </summary>
    public class TabSizeLayoutIntegrationTests
    {
        [Fact]
        public async Task Pre_LeadingTab_ExpandsToEightSpaces_DefaultTabSize()
        {
            var (root, _) = await BuildAndLayout(Wrap("<pre id='p'>\tA</pre>"));
            var p = FindById(root, "p")!;
            var words = p.LineBoxes[0].Words;

            var tabWord = words.First(w => w.Text != null && w.Text.Length > 0 && w.Text.All(char.IsWhiteSpace) && w.Text != "\n");
            Assert.Equal(8, tabWord.Text!.Length);
        }

        [Fact]
        public async Task Pre_CustomNumericTabSize_ChangesExpandedSpaceCount()
        {
            var (root, _) = await BuildAndLayout(Wrap("<pre id='p' style='tab-size:4'>\tA</pre>"));
            var p = FindById(root, "p")!;
            var words = p.LineBoxes[0].Words;

            var tabWord = words.First(w => w.Text != null && w.Text.Length > 0 && w.Text.All(char.IsWhiteSpace) && w.Text != "\n");
            Assert.Equal(4, tabWord.Text!.Length);
        }

        [Fact]
        public async Task Pre_ExpandedTabWord_NeverContainsLiteralTabCharacter()
        {
            var (root, _) = await BuildAndLayout(Wrap("<pre id='p' style='tab-size:3'>\t\tA</pre>"));
            var p = FindById(root, "p")!;
            var words = p.LineBoxes[0].Words;

            Assert.DoesNotContain(words, w => w.Text != null && w.Text.Contains('\t'));
        }

        [Fact]
        public async Task Pre_TabSizeInherits_FromAncestorPre()
        {
            var (root, _) = await BuildAndLayout(Wrap("<pre style='tab-size:2'><span id='s'>\tA</span></pre>"));
            var span = FindById(root, "s")!;
            var words = span.Boxes[0].Words;

            var tabWord = words.First(w => w.Text != null && w.Text.Length > 0 && w.Text.All(char.IsWhiteSpace) && w.Text != "\n");
            Assert.Equal(2, tabWord.Text!.Length);
        }

        [Fact]
        public async Task Pre_LengthTabSize_WiderLengthProducesWiderTabStop()
        {
            var (smallRoot, _) = await BuildAndLayout(Wrap("<pre id='p' style='tab-size:12pt'>\tA</pre>"));
            var pSmall = FindById(smallRoot, "p")!;
            var smallTabWord = pSmall.LineBoxes[0].Words.First(w => w.Text != null && w.Text.Length > 0 && w.Text.All(char.IsWhiteSpace) && w.Text != "\n");

            var (bigRoot, _) = await BuildAndLayout(Wrap("<pre id='p' style='tab-size:60pt'>\tA</pre>"));
            var pBig = FindById(bigRoot, "p")!;
            var bigTabWord = pBig.LineBoxes[0].Words.First(w => w.Text != null && w.Text.Length > 0 && w.Text.All(char.IsWhiteSpace) && w.Text != "\n");

            Assert.True(bigTabWord.Width > smallTabWord.Width,
                $"expected a 60pt tab-size to produce a wider tab stop than a 12pt one (got {bigTabWord.Width} vs {smallTabWord.Width})");
        }

        [Fact]
        public async Task Pre_LargerNumericTabSize_ProducesWiderTabStop()
        {
            var (smallRoot, _) = await BuildAndLayout(Wrap("<pre id='p' style='tab-size:2'>\tA</pre>"));
            var pSmall = FindById(smallRoot, "p")!;
            var smallTabWord = pSmall.LineBoxes[0].Words.First(w => w.Text != null && w.Text.Length > 0 && w.Text.All(char.IsWhiteSpace) && w.Text != "\n");

            var (bigRoot, _) = await BuildAndLayout(Wrap("<pre id='p' style='tab-size:16'>\tA</pre>"));
            var pBig = FindById(bigRoot, "p")!;
            var bigTabWord = pBig.LineBoxes[0].Words.First(w => w.Text != null && w.Text.Length > 0 && w.Text.All(char.IsWhiteSpace) && w.Text != "\n");

            Assert.True(bigTabWord.Width > smallTabWord.Width,
                $"expected tab-size:16 to produce a wider tab stop than tab-size:2 (got {bigTabWord.Width} vs {smallTabWord.Width})");
        }

        [Fact]
        public async Task Pre_MidLineTab_AdvancesPastPrecedingText()
        {
            // A tab following other content on the same line must still advance to the *next* tab stop
            // from wherever that content ended, not restart from column 0 - "ab" is narrower than one
            // 8-space-wide default tab stop, so the tab after it must still add positive width.
            var (root, _) = await BuildAndLayout(Wrap("<pre id='p'>ab\tc</pre>"));
            var p = FindById(root, "p")!;
            var words = p.LineBoxes[0].Words;

            var tabWord = words.First(w => w.Text != null && w.Text.Length > 0 && w.Text.All(char.IsWhiteSpace) && w.Text != "\n");
            Assert.True(tabWord.Width > 0, "expected a mid-line tab to still occupy positive width");
        }

        [Fact]
        public async Task Pre_ZeroTabSize_CollapsesTabToZeroWidth()
        {
            // tab-size's grammar is <number [0,∞]>, so 0 is spec-legal - every tab must collapse to zero
            // width (ExpandTabs' tabStopWidth<=0 guard) rather than dividing by zero internally.
            var (root, _) = await BuildAndLayout(Wrap("<pre id='p' style='tab-size:0'>a\tb</pre>"));
            var p = FindById(root, "p")!;
            var words = p.LineBoxes[0].Words;

            var tabWord = words.First(w => w.Text != null && w.Text != "\n" && w.Text.All(char.IsWhiteSpace));
            Assert.Equal(0, tabWord.Text!.Length);
            Assert.Equal(0, tabWord.Width);
        }

        [Fact]
        public async Task Pre_FirstLineTextTransformDiffers_StillExpandsTabInFirstLineText()
        {
            // ::first-line applying a different text-transform re-derives its own text from
            // CssRect.OriginalText (see ApplyFirstLineStyleOverride) - which still carries the raw,
            // pre-expansion tab character even after MeasureWordsSize has already expanded this word's
            // own Text. That re-derived FirstLineText must still get its tab expanded rather than
            // leaking a literal U+0009 into what FragmentPainter actually draws for line 1.
            var html = Wrap("<style>#p::first-line { text-transform: uppercase }</style>" +
                             "<pre id='p' style='tab-size:4'>\tabc</pre>");
            var (root, _) = await BuildAndLayout(html);
            var p = FindById(root, "p")!;
            var words = p.LineBoxes[0].Words;

            var tabWord = words.First(w => (w.FirstLineText ?? w.Text) is { Length: > 0 } t && t.All(char.IsWhiteSpace) && t != "\n");
            Assert.NotNull(tabWord.FirstLineText);
            Assert.DoesNotContain('\t', tabWord.FirstLineText!);
            Assert.Equal(4, tabWord.FirstLineText!.Length);
        }

        [Fact]
        public async Task Pre_MixedSpacesAndTabInSameWhitespaceRun_MeasuresPlainSpacesToo()
        {
            // A preserved-whitespace word can hold a run of ordinary spaces and tabs together (e.g. "a
            // \tb" - one space then one tab, both between the same pair of non-whitespace words) -
            // ExpandTabs must still measure the plain-space characters in that run, not just the tabs.
            var (root, _) = await BuildAndLayout(Wrap("<pre id='p' style='tab-size:4'>a \tb</pre>"));
            var p = FindById(root, "p")!;
            var words = p.LineBoxes[0].Words;

            var whitespaceWord = words.First(w => w.Text != null && w.Text != "\n" && w.Text.All(char.IsWhiteSpace));
            // The literal space is measured (not skipped) before the tab is expanded to reach its own
            // stop, so the run ends up at least 2 characters wide with no literal tab left in it.
            Assert.True(whitespaceWord.Text!.Length >= 2,
                $"expected the literal space plus the expanded tab to produce at least 2 characters, got {whitespaceWord.Text.Length}");
            Assert.DoesNotContain('\t', whitespaceWord.Text);
        }

        [Fact]
        public async Task Pre_PlainSpacesTrailingAfterTab_AreAlsoMeasured()
        {
            // The mirror image of the mixed-run case above: literal spaces trailing *after* the last tab
            // in a whitespace run exercise ExpandTabs' final flush of the run following the loop.
            var (root, _) = await BuildAndLayout(Wrap("<pre id='p' style='tab-size:4'>a\t  b</pre>"));
            var p = FindById(root, "p")!;
            var words = p.LineBoxes[0].Words;

            var whitespaceWord = words.First(w => w.Text != null && w.Text != "\n" && w.Text.All(char.IsWhiteSpace));
            Assert.True(whitespaceWord.Text!.Length >= 2,
                $"expected the expanded tab plus the two trailing spaces to produce at least 2 characters, got {whitespaceWord.Text.Length}");
            Assert.DoesNotContain('\t', whitespaceWord.Text);
        }

        [Fact]
        public async Task Pre_FirstLineWithExplicitNewline_ResetsColumnAtLineBreak()
        {
            // Combines ::first-line's own re-measurement pass with an explicit line break, so
            // ApplyFirstLineStyleOverride's own "\n" branch (resetting its local column tracker) runs
            // alongside its tab-expansion branch in the same pass.
            var html = Wrap("<style>#p::first-line { text-transform: uppercase }</style>" +
                             "<pre id='p' style='tab-size:4'>\tA\nB</pre>");
            var (root, _) = await BuildAndLayout(html);
            var p = FindById(root, "p")!;

            Assert.Equal(2, p.LineBoxes.Count);
        }

        [Fact]
        public async Task Pre_HugeTabSize_ClampsExpansionInsteadOfUnboundedAllocation()
        {
            // PeachPDF renders arbitrary (often untrusted) HTML - an absurd tab-size must not let a
            // single tab character balloon into a multi-megabyte string (CssBox.ExpandTabs's
            // MaxTabExpansionSpaces cap).
            var (root, _) = await BuildAndLayout(Wrap("<pre id='p' style='tab-size:100000000'>a\tb</pre>"));
            var p = FindById(root, "p")!;
            var words = p.LineBoxes[0].Words;

            var tabWord = words.First(w => w.Text != null && w.Text != "\n" && w.Text.All(char.IsWhiteSpace));
            Assert.True(tabWord.Text!.Length <= 1000,
                $"expected tab expansion to be capped at 1000 characters, got {tabWord.Text.Length}");
        }

        [Fact]
        public async Task Pre_TabExpansion_ShiftsSubsequentWordLocation()
        {
            // CLAUDE.md's layout-engine testing convention: assert on the actual CssBox/CssRect
            // properties layout produced (Location here), not just that layout completed - a wider
            // tab-size must push the word after the tab further right.
            var (narrowRoot, _) = await BuildAndLayout(Wrap("<pre id='p' style='tab-size:2'>a\tb</pre>"));
            var pNarrow = FindById(narrowRoot, "p")!;
            var bNarrow = pNarrow.LineBoxes[0].Words.First(w => w.Text == "b");

            var (wideRoot, _) = await BuildAndLayout(Wrap("<pre id='p' style='tab-size:16'>a\tb</pre>"));
            var pWide = FindById(wideRoot, "p")!;
            var bWide = pWide.LineBoxes[0].Words.First(w => w.Text == "b");

            Assert.True(bWide.Left > bNarrow.Left,
                $"expected a wider tab-size to push the word after the tab further right (got {bWide.Left} vs {bNarrow.Left})");
        }

        [Fact]
        public async Task Pre_RtlDirection_WithPreservedTab_DoesNotLeakLiteralTabOrCrash()
        {
            // CssRectWord.ReplaceText is also used by bidi mirroring (CssLayoutEngine's per-line L2/L4
            // reordering) - a preserved tab under direction:rtl exercises both mutation paths on the
            // same word type without either corrupting the other's result.
            var (root, _) = await BuildAndLayout(Wrap("<pre id='p' dir='rtl' style='tab-size:4'>\tA</pre>"));
            var p = FindById(root, "p")!;
            var words = p.LineBoxes[0].Words;

            Assert.DoesNotContain(words, w => w.Text != null && w.Text.Contains('\t'));
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
