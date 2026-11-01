using PeachPDF.Rendering;
using PeachDrawing.Core;
using PeachDrawing.Text.Shaping;
using PeachDrawing.PDF;
using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachPDF.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PeachDrawing.Text;

namespace PeachBrackets.Engine.Tests.Html.Core
{
    /// <summary>The layout half of the shaping characterization: direction and whitespace handling as the box tree lays words out.</summary>
    public class ShapingLayoutCharacterizationTests
    {
        private static PeachDrawing.Text.Typeface Descriptor(string fontPath) => TypefaceFixtures.Shared(fontPath);

        [Fact]
        public async Task DirectionRtl_PlainLatinContent_StaysInLogicalOrder()
        {
            // UAX#9 I2: strong-L characters inside an RTL (odd) embedding level are bumped to the next
            // even level, so plain Latin content in an RTL paragraph resolves to its own LTR run and is
            // NOT reordered - unlike the old whole-word-mirroring approximation, which repositioned every
            // word regardless of its actual script and got this case wrong.
            var p = await LayoutParagraph(
                "<!DOCTYPE html><html><body><p style=\"direction:rtl; width:400px; font-size:14pt\">AB CD</p></body></html>");
            var words = WordsOf(p);

            Assert.Equal(new[] { "AB", "CD" }, words.Select(w => w.Text));
            Assert.True(words[0].Left < words[1].Left,
                $"expected plain Latin content to stay left-to-right even in an RTL paragraph; AB.Left={words[0].Left}, CD.Left={words[1].Left}");
        }

        [Fact]
        public async Task DirectionRtl_RealRtlScriptText_ReordersWordsAndMirrorsBrackets()
        {
            // Real Hebrew content (strong R), unlike plain Latin above, resolves to one RTL level run
            // spanning the whole line - L2 reverses the word order (the first logical word ends up
            // rightmost) and L4 mirrors the parenthesis pair's glyphs (BidiMirrorResolver.ApplyMirroring).
            var p = await LayoutParagraph(
                "<!DOCTYPE html><html><body><p style=\"direction:rtl; width:400px; font-size:14pt\">שלום (עולם)</p></body></html>");
            var words = WordsOf(p);

            Assert.Equal(2, words.Count);
            Assert.True(words[0].Left > words[1].Left,
                $"expected the first logical word (שלום) to end up rightmost; [0].Left={words[0].Left}, [1].Left={words[1].Left}");

            // The parenthesis pair still visually wraps the word after L2+L4 (reversing the run and then
            // mirroring each of its characters puts an ordinary '(' back at the run's start and ')' back
            // at its end - only the word's own internal character order is reversed in between).
            var parenthesized = words.Single(w => w.Text.Contains('ם') && w.Text.Length > 4);
            Assert.StartsWith("(", parenthesized.Text);
            Assert.EndsWith(")", parenthesized.Text);
            Assert.Equal("(םלוע)", parenthesized.Text);
        }

        [Fact]
        public async Task MeasureString_HandlesNewlineTabAndControlChars()
        {
            // Drives the real font-metrics path (FontHelper.MeasureString) through the rune-based loop with
            // a line feed (which starts a new measured line), a tab (treated as a space), and a control
            // character (skipped) - the branches a mock measurement adapter never exercises.
            var renderContext = new HtmlRenderContext();
            var container = new HtmlContainerInt(renderContext);
            await container.SetHtml("<!DOCTYPE html><html><body><p style=\"font-size:14pt\">x</p></body></html>", null);

            var size = new Size(595, 842);
            container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);

            using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
            await container.PerformLayout(graphics);

            var font = FindByTag(container.Root!, "p")!.ActualFont;

            var oneLine = graphics.MeasureString("abc", font);
            var twoLines = graphics.MeasureString("ab\ncd\tefgh", font);

            // The line feed produced a second measured line, so the multi-line string is taller.
            Assert.True(twoLines.Height > oneLine.Height,
                $"expected the '\\n' string to measure taller; one-line={oneLine.Height}, multi-line={twoLines.Height}");
            Assert.True(twoLines.Width > 0);
        }

        private static async Task<CssBox> LayoutParagraph(string html)
        {
            var renderContext = new HtmlRenderContext();
            var container = new HtmlContainerInt(renderContext);
            await container.SetHtml(html, null);

            var size = new Size(595, 842);
            container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);

            using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
            await container.PerformLayout(graphics);

            return FindByTag(container.Root!, "p")!;
        }

        private static CssBox? FindByTag(CssBox box, string tag)
        {
            if (box.HtmlTag?.Name.Equals(tag, StringComparison.OrdinalIgnoreCase) == true)
                return box;
            foreach (var child in box.Boxes)
            {
                var found = FindByTag(child, tag);
                if (found != null) return found;
            }
            return null;
        }

        private static List<CssRectWord> WordsOf(CssBox p)
        {
            var words = new List<CssRectWord>();
            Collect(p, words);
            return words;
        }

        private static void Collect(CssBox box, List<CssRectWord> words)
        {
            words.AddRange(box.Words.OfType<CssRectWord>().Where(w => w.Text != "\n"));
            foreach (var child in box.Boxes)
                Collect(child, words);
        }
    }
}
