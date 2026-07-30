using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Fonts;
using PeachPDF.Fonts.OpenType;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.Tests.TestSupport;
using PeachPDF.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PeachPDF.Tests.Html.Core
{
    /// <summary>
    /// PeachPDF applies GSUB ligature substitution (<c>liga</c>/<c>clig</c>/<c>rlig</c>, see
    /// <see cref="GsubShaper"/>) and a real UAX#9 Unicode Bidi Algorithm (see
    /// <see cref="PeachPDF.Text.Bidi.BidiResolver"/>), but still has no full OpenType Layout
    /// engine: no <c>GPOS</c> (kerning/mark positioning), no contextual substitution (GSUB lookup
    /// types 5-8), no Arabic/Indic complex-script joining. These tests characterize what remains a
    /// <b>known limitation</b> (contextual-forms independence) alongside the ligature and bidi
    /// reordering/mirroring behavior real UAX#9 support added. See docs/html-css-support.md "Text
    /// shaping" for the reader-facing note and .claude/accepted-gaps/no-text-shaping.md for what's
    /// still out of scope.
    /// </summary>
    public class ShapingCharacterizationTests
    {
        private static OpenTypeDescriptor Descriptor(string fontPath)
        {
            var face = XFontSource.GetOrCreateFrom(System.IO.File.ReadAllBytes(fontPath)).Fontface;
            return new OpenTypeDescriptor("shaping-test", "shaping-test", XFontStyle.Regular, face,
                new XPdfFontOptions(PdfFontEncoding.Unicode));
        }

        [Fact]
        public void GsubLigatureSubstitution_FfCollectsLigatureGlyph()
        {
            // Source Sans 3's GSUB `liga` feature ligates "ff"/"ft"/"fft" from a single ('f')
            // coverage glyph (confirmed against the font directly via fontTools) - notably not
            // "fi"/"fl", so this deliberately doesn't use the precomposed U+FB01 "ﬁ" ligature the
            // old (pre-GSUB) version of this test referenced.
            var descriptor = Descriptor(BundledFonts.Ttf); // Source Sans 3
            var f = descriptor.CharCodeToGlyphIndex(new Rune('f'));

            var cmap = new CMapInfo(descriptor);
            cmap.AddShapedText("ff", LigatureFeatures.Default);

            // The merged ligature glyph is not the plain 'f' glyph, and its source text ("ff") is
            // recorded for ToUnicode - a codepoint-keyed CharacterToGlyphIndex entry can't carry it.
            Assert.Single(cmap.LigatureGlyphToText);
            var (ligatureGlyph, text) = cmap.LigatureGlyphToText.Single();
            Assert.NotEqual(f, ligatureGlyph);
            Assert.Equal("ff", text);
            Assert.Contains(ligatureGlyph, cmap.GlyphIndices.Keys);
        }

        [Fact]
        public void GsubLigatureSubstitution_NoneDisablesIt()
        {
            // font-variant-ligatures: none (LigatureFeatures.None) must fully restore the pre-GSUB,
            // 1:1 codepoint-to-glyph behavior - this is what makes turning ligatures off actually work.
            var descriptor = Descriptor(BundledFonts.Ttf);
            var shaped = descriptor.Shape("ff", LigatureFeatures.None);

            Assert.Equal(2, shaped.Count);
        }

        [Fact]
        public void GsubLigatureSubstitution_LongestMatchFirst()
        {
            // The font lists "fft" before "ff" in its LigatureSet for the shared 'f' coverage entry -
            // shaping must try ligatures in the font's own authored order (not merge greedily by
            // twos), so "fft" collects the single 3-glyph ligature, not "ff" + "t".
            var descriptor = Descriptor(BundledFonts.Ttf);
            var shaped = descriptor.Shape("fft", LigatureFeatures.Default);

            Assert.Single(shaped);
            Assert.Equal(0, shaped[0].ClusterStart);
            Assert.Equal(3, shaped[0].ClusterLength);
        }

        [Fact]
        public void AddShapedText_NullText_IsANoOp()
        {
            var descriptor = Descriptor(BundledFonts.Ttf);
            var cmap = new CMapInfo(descriptor);

            cmap.AddShapedText(null!, LigatureFeatures.Default);

            Assert.Empty(cmap.GlyphIndices);
            Assert.Empty(cmap.LigatureGlyphToText);
        }

        [Fact]
        public void GlyphSelectionIsNeighborIndependent_NoContextualForms()
        {
            // No contextual/positional shaping (the Arabic isolated/initial/medial/final family, GSUB
            // lookup types 5-8, etc.): a codepoint not involved in any ligature resolves to the same
            // glyph regardless of the characters around it. ('a'/"cat" don't participate in any of
            // this font's ligatures, so this remains neighbor-independent even with GSUB wired up.)
            var descriptor = Descriptor(BundledFonts.Ttf);

            var alone = new CMapInfo(descriptor);
            alone.AddChars("a");
            var inWord = new CMapInfo(descriptor);
            inWord.AddChars("cat");

            Assert.Equal(alone.CharacterToGlyphIndex['a'], inWord.CharacterToGlyphIndex['a']);
        }

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
