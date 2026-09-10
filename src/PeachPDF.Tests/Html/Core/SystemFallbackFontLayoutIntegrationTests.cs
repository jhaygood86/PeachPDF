using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Utils;
using PeachPDF.Tests.TestSupport;
using System;
using System.IO;
using System.Linq;
using System.Text;

using PeachPDF.Fonts;

namespace PeachPDF.Tests.Html.Core
{
    /// <summary>
    /// End-to-end coverage of the last-resort system-fallback step (issue #172): when NO family in a
    /// box's own <c>font-family</c> stack covers a character, PeachPDF now searches every OTHER font
    /// registered with the document (not just what's declared) before giving up to <c>.notdef</c>.
    /// Distinct from <see cref="UnicodeRangeLayoutIntegrationTests"/>, whose "fallback" tests stay
    /// entirely within the declared <c>font-family</c> stack.
    /// </summary>
    public class SystemFallbackFontLayoutIntegrationTests
    {
        private static string B64(string path) => Convert.ToBase64String(File.ReadAllBytes(path));

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

        private static void CollectWords(CssBox box, System.Collections.Generic.List<CssRectWord> words)
        {
            words.AddRange(box.Words.OfType<CssRectWord>().Where(w => w.Text != "\n"));
            foreach (var child in box.Boxes)
                CollectWords(child, words);
        }

        private static System.Collections.Generic.List<CssRectWord> WordsOf(CssBox p)
        {
            var words = new System.Collections.Generic.List<CssRectWord>();
            CollectWords(p, words);
            return words;
        }

        [Fact]
        public async Task UncoveredCharacter_FallsBackToAnOtherRegisteredFont_NotJustTheDeclaredStack()
        {
            // 'Latin' (Source Sans 3) is the ONLY family the box declares, and it has no Arabic glyphs.
            // 'ArabicFallback' is registered on the page but never referenced by any font-family list -
            // the last-resort step must still find and use it. We deliberately do NOT assert exactly
            // WHICH family wins by name: on a host that also has its own Arabic-covering system font
            // (macOS ships several by default), that real font is an equally valid, spec-correct answer.
            // What must always be true is that the character no longer resolves to the declared,
            // non-covering 'Latin' family, and that whatever it DOES resolve to actually has the glyph.
            var latinFamily = TtfFontDescription.LoadDescription(BundledFonts.Ttf).FontFamilyInvariantCulture;

            var html = $@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'Latin'; src: url('data:font/truetype;base64,{B64(BundledFonts.Ttf)}') format('truetype'); }}
@font-face {{ font-family: 'ArabicFallback'; src: url('data:font/truetype;base64,{B64(BundledFonts.Arabic)}') format('truetype'); }}
body {{ font-family: 'Latin'; font-size: 14pt; }}
p {{ width: 400px; }}
</style></head>
<body><p>&#x0628;</p></body>
</html>";

            var p = await LayoutParagraph(html);
            var owner = WordsOf(p)[0].OwnerBox;
            var codepoint = new Rune(0x0628); // ARABIC LETTER BEH

            var resolvedFont = owner.ActualFontForCodepoint(codepoint);

            Assert.NotEqual(latinFamily, ((FontAdapter)resolvedFont).Font.Name);
            Assert.True(resolvedFont.HasGlyph(codepoint));
        }

        [Fact]
        public async Task NoRegisteredFontCoversTheCharacter_StillFallsBackToTheDeclaredFont()
        {
            // U+10FFFF (the very last Unicode codepoint) is a permanently-guaranteed-unassigned
            // noncharacter, in the supplementary plane specifically - not the BMP, where GNU Unifont
            // (bundled on Ubuntu CI runners) turned out to genuinely map a "here's an undefined
            // codepoint" glyph even to a BMP noncharacter like U+FDD0, defeating that as a "nothing
            // covers this" probe. Without any covering font anywhere, resolution must still end up on
            // the box's own declared/default font exactly as it did before this feature existed -
            // proving the last-resort scan doesn't change behavior for a character truly nothing can
            // render.
            var latinFamily = TtfFontDescription.LoadDescription(BundledFonts.Ttf).FontFamilyInvariantCulture;

            var html = $@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'Latin'; src: url('data:font/truetype;base64,{B64(BundledFonts.Ttf)}') format('truetype'); }}
body {{ font-family: 'Latin'; font-size: 14pt; }}
p {{ width: 400px; }}
</style></head>
<body><p>&#x10FFFF;</p></body>
</html>";

            var p = await LayoutParagraph(html);
            var owner = WordsOf(p)[0].OwnerBox;
            var codepoint = new Rune(0x10FFFF);

            var resolvedFont = owner.ActualFontForCodepoint(codepoint);

            Assert.Equal(latinFamily, ((FontAdapter)resolvedFont).Font.Name);
        }
    }
}
