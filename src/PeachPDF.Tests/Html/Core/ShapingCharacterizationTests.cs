using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Fonts;
using PeachPDF.PdfSharpCore.Fonts.OpenType;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PeachPDF.Tests.Html.Core
{
    /// <summary>
    /// These tests characterize <b>known limitations</b>, not desired output: PeachPDF has no text-shaping
    /// engine - no OpenType Layout (GSUB/GPOS), no Unicode Bidi Algorithm, no Arabic joining. Text maps
    /// 1:1 from codepoint to glyph. They lock that behavior in so the Rune/format-12 pipeline changes can't
    /// silently regress it; a real shaping engine (tracked separately) would update them. See
    /// docs/html-css-support.md "Text shaping" for the reader-facing note.
    /// </summary>
    public class ShapingCharacterizationTests
    {
        private const int FiLigature = 0xFB01; // ﬁ LATIN SMALL LIGATURE FI

        private static OpenTypeDescriptor Descriptor(string fontPath)
        {
            var face = XFontSource.GetOrCreateFrom(System.IO.File.ReadAllBytes(fontPath)).Fontface;
            return new OpenTypeDescriptor("shaping-test", "shaping-test", XFontStyle.Regular, face,
                new XPdfFontOptions(PdfFontEncoding.Unicode));
        }

        [Fact]
        public void NoGsubLigatureSubstitution_FiCollectsSeparateFAndIGlyphs()
        {
            var descriptor = Descriptor(BundledFonts.Ttf); // Source Sans 3
            var f = descriptor.CharCodeToGlyphIndex(new Rune('f'));
            var i = descriptor.CharCodeToGlyphIndex(new Rune('i'));
            var ligature = descriptor.CharCodeToGlyphIndex(new Rune(FiLigature));

            // The font DOES contain the ﬁ ligature glyph, but it is reachable only through its own
            // codepoint (a designer-invented ligature with no codepoint would be unreachable entirely)...
            Assert.NotEqual(0, ligature);
            Assert.NotEqual(ligature, f);
            Assert.NotEqual(ligature, i);

            // ...and the text "fi" collects the separate f and i glyphs - there is no GSUB pass to merge
            // them into the ligature.
            var cmap = new CMapInfo(descriptor);
            cmap.AddChars("fi");
            Assert.Equal(f, cmap.CharacterToGlyphIndex['f']);
            Assert.Equal(i, cmap.CharacterToGlyphIndex['i']);
            Assert.DoesNotContain(ligature, cmap.CharacterToGlyphIndex.Values);
        }

        [Fact]
        public void GlyphSelectionIsNeighborIndependent_NoContextualForms()
        {
            // No contextual/positional shaping (the Arabic isolated/initial/medial/final family, etc.):
            // a codepoint resolves to the same glyph regardless of the characters around it.
            var descriptor = Descriptor(BundledFonts.Ttf);

            var alone = new CMapInfo(descriptor);
            alone.AddChars("a");
            var inWord = new CMapInfo(descriptor);
            inWord.AddChars("cat");

            Assert.Equal(alone.CharacterToGlyphIndex['a'], inWord.CharacterToGlyphIndex['a']);
        }

        [Fact]
        public async Task DirectionRtl_MirrorsWordOrder_ButKeepsEachWordLogical()
        {
            // direction: rtl only mirrors the visual x-order of whole word boxes (CssLayoutEngine
            // .ApplyRightToLeft); there is no Unicode Bidi Algorithm and no per-character reordering, so
            // each word's own text is untouched.
            var p = await LayoutParagraph(
                "<!DOCTYPE html><html><body><p style=\"direction:rtl; width:400px; font-size:14pt\">AB CD</p></body></html>");
            var words = WordsOf(p);

            Assert.Equal(new[] { "AB", "CD" }, words.Select(w => w.Text));   // text stays logical, not reversed
            Assert.True(words[0].Left > words[1].Left,                        // "AB" (logical first) sits to the right
                $"expected RTL to place the first logical word to the right; AB.Left={words[0].Left}, CD.Left={words[1].Left}");
        }

        [Fact]
        public async Task MeasureString_HandlesNewlineTabAndControlChars()
        {
            // Drives the real font-metrics path (FontHelper.MeasureString) through the rune-based loop with
            // a line feed (which starts a new measured line), a tab (treated as a space), and a control
            // character (skipped) - the branches a mock measurement adapter never exercises.
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml("<!DOCTYPE html><html><body><p style=\"font-size:14pt\">x</p></body></html>", null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
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
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
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
