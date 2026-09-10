using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using PeachPDF;
using PeachPDF.Fonts;
using PeachPDF.PdfSharpCore;
using PeachPDF.Tests.TestSupport;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end tests that a repeated COLR/CPAL glyph's artwork is written into the PDF once, as a
    /// Form XObject, and merely invoked at each later occurrence - and, just as importantly, that the
    /// things which genuinely differ per occurrence (the placement transform, and the invisible
    /// <c>/ActualText</c>-bearing text object that makes a color glyph selectable) stay on the page.
    ///
    /// The assertions are structural rather than "does token X appear": a form that is written but
    /// never correctly referenced still fills a content stream with plausible-looking tokens, so these
    /// count forms against invocations and check that each invocation's <c>cm</c> and <c>Do</c> sit
    /// inside the same <c>q</c>/<c>Q</c> pair. Visual equivalence with the pre-form output was verified
    /// separately by rasterizing through both PDFium and MuPDF.
    /// </summary>
    public class ColorGlyphFormReuseTests
    {
        /// <summary>Matches one whole glyph placement: scale-only cm, then Do, inside one q/Q.</summary>
        private static readonly Regex Placement = new(
            @"q (-?\d*\.?\d+) 0 0 (-?\d*\.?\d+) (-?\d*\.?\d+) (-?\d*\.?\d+) cm (/\w+) Do Q",
            RegexOptions.Compiled);

        private static async Task<string> Render(string fontPath, string body, string extraCss = "")
        {
            var family = TtfFontDescription.LoadDescription(fontPath).FontFamilyInvariantCulture;
            var generator = new PdfGenerator();
            await using (var stream = File.OpenRead(fontPath))
                await generator.AddFontFromStream(stream);

            var html = "<!DOCTYPE html><html><head><style>" +
                       $"body {{ font-family: '{family}'; font-size: 40pt; color: black; }}" +
                       extraCss +
                       "</style></head><body>" + body + "</body></html>";

            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        /// <summary>How many Form XObjects the document contains.</summary>
        private static int FormCount(string pdf) => Regex.Matches(pdf, @"/Subtype /Form").Count;

        /// <summary>Every glyph placement in the document, in order.</summary>
        private static List<(double Scale, double X, double Y, string Name)> Placements(string pdf) =>
            Placement.Matches(pdf)
                .Select(m => (Num(m.Groups[1].Value), Num(m.Groups[3].Value), Num(m.Groups[4].Value), m.Groups[5].Value))
                .ToList();

        private static double Num(string s) => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

        private static int Count(string haystack, string needle)
        {
            int n = 0, i = 0;
            while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
            return n;
        }

        [Fact]
        public async Task RepeatedGlyph_EmbedsArtworkOnce_AndInvokesItPerOccurrence()
        {
            // 'A' in the v0 fixture is a red box under a green triangle - two solid fills per occurrence
            // when the artwork is inlined.
            string pdf = await Render(BundledFonts.ColorV0, "AAAA");

            Assert.Equal(1, FormCount(pdf));

            var placements = Placements(pdf);
            Assert.Equal(4, placements.Count);
            Assert.Single(placements.Select(p => p.Name).Distinct());

            // The artwork itself is written once: one red fill and one green fill in the whole file,
            // not one pair per occurrence. This is the assertion that fails if the form is emitted but
            // the occurrences keep inlining their own copy alongside it.
            Assert.Equal(1, Count(pdf, "1 0 0 rg"));
            Assert.Single(Regex.Matches(pdf, @"0 0\.5\d* 0 rg"));
        }

        [Fact]
        public async Task EachOccurrence_PlacesTheFormAtItsOwnPosition()
        {
            string pdf = await Render(BundledFonts.ColorV0, "AAAA");

            var placements = Placements(pdf);
            Assert.Equal(4, placements.Count);

            // Same artwork, same scale, four distinct pen positions advancing left to right along one
            // baseline - so the form really is being positioned per occurrence, not stacked at one spot.
            Assert.Single(placements.Select(p => p.Scale).Distinct());
            Assert.Single(placements.Select(p => p.Y).Distinct());
            Assert.Equal(4, placements.Select(p => p.X).Distinct().Count());
            Assert.Equal(placements.Select(p => p.X).OrderBy(x => x), placements.Select(p => p.X));
        }

        [Fact]
        public async Task SameGlyphAtDifferentFontSizes_SharesOneForm()
        {
            // The cache key deliberately excludes size: the artwork is drawn at a canonical em size and
            // the placement cm scales it. Two sizes must therefore share one embed.
            string pdf = await Render(BundledFonts.ColorV0,
                "<span class='small'>A</span><span class='big'>A</span>",
                ".small { font-size: 20pt; } .big { font-size: 60pt; }");

            Assert.Equal(1, FormCount(pdf));

            var placements = Placements(pdf);
            Assert.Equal(2, placements.Count);
            Assert.Single(placements.Select(p => p.Name).Distinct());

            // 20pt and 60pt against a canonical 100-unit em.
            Assert.Equal(2, placements.Select(p => p.Scale).Distinct().Count());
            Assert.Equal(3.0, placements.Max(p => p.Scale) / placements.Min(p => p.Scale), 3);
        }

        [Fact]
        public async Task DifferentGlyphs_GetTheirOwnForms()
        {
            // 'A' (red box + green triangle) and 'B' (blue circle) are different artwork.
            string pdf = await Render(BundledFonts.ColorV0, "AB");

            Assert.Equal(2, FormCount(pdf));
            Assert.Equal(2, Placements(pdf).Select(p => p.Name).Distinct().Count());
        }

        [Fact]
        public async Task PlainGlyphInAColorFont_DoesNotShareAFormAcrossTextColors()
        {
            // 'X' has no color record, so it is filled in the text color - which is therefore part of
            // the artwork and must be part of the key. Sharing one form here would silently repaint the
            // second occurrence in the first one's color.
            string pdf = await Render(BundledFonts.ColorV0,
                "<span class='red'>X</span><span class='blue'>X</span>",
                ".red { color: rgb(255, 0, 0); } .blue { color: rgb(0, 0, 255); }");

            Assert.Equal(2, FormCount(pdf));
            Assert.Equal(2, Placements(pdf).Select(p => p.Name).Distinct().Count());
            Assert.Contains("1 0 0 rg", pdf);
            Assert.Contains("0 0 1 rg", pdf);
        }

        [Fact]
        public async Task SameGlyphInTheSameTextColor_StillSharesOneForm()
        {
            // The colour is in the key, but two runs of the same colour must still collapse - otherwise
            // keying on it would have thrown the win away for the ordinary case.
            string pdf = await Render(BundledFonts.ColorV0,
                "<span class='red'>X</span><span class='red'>X</span>",
                ".red { color: rgb(255, 0, 0); }");

            Assert.Equal(1, FormCount(pdf));
            Assert.Equal(2, Placements(pdf).Count);
        }

        [Fact]
        public async Task GlyphRepeatedAcrossAPageBreak_EmbedsArtworkOnce_AndBothPagesReferenceIt()
        {
            // The cache lives on the document, and PdfFormXObjectTable adds one form to every page's
            // resources that invokes it - so a running header's emoji costs one embed, not one per page.
            string pdf = await Render(BundledFonts.ColorV0,
                "<div>A</div><div class='next'>A</div>",
                ".next { page-break-before: always; }");

            Assert.Equal(2, Regex.Matches(pdf, @"/Type /Page[^s]").Count);
            Assert.Equal(1, FormCount(pdf));

            var placements = Placements(pdf);
            Assert.Equal(2, placements.Count);

            // One form object, two page resource dictionaries naming it.
            var formObject = Regex.Match(pdf, @"(\d+) 0 obj(?:(?!\d+ 0 obj).)*?/Subtype /Form", RegexOptions.Singleline)
                .Groups[1].Value;
            Assert.NotEqual(string.Empty, formObject);
            Assert.Equal(2, Regex.Matches(pdf, @"/XObject\s*<<\s*/\w+ " + formObject + @" 0 R").Count);
        }

        [Fact]
        public async Task InvisibleSelectableText_StaysOnThePage_OncePerOccurrence()
        {
            // Only the ink moves into the form. The rendering-mode-3 text object carries per-occurrence
            // /ActualText, so sharing it would either lose selection or duplicate the text layer.
            string pdf = await Render(BundledFonts.ColorV0, "AAAA");

            Assert.Equal(4, Count(pdf, " Tj"));
            Assert.Contains("3 Tr", pdf);

            // No text object at all inside the form's own content stream.
            string formStream = FormContentStream(pdf);
            Assert.DoesNotContain("BT", formStream);
            Assert.DoesNotContain("Tj", formStream);
            Assert.Contains("\nf\n", formStream); // ...but the ink itself is there.
        }

        [Fact]
        public async Task GlyphThatPaintsNothing_ProducesNoForm()
        {
            // ' ' is empty in the fixture: the measure pass finds no ink, so there is nothing to embed
            // and nothing to invoke - and that answer is cached, not re-measured per occurrence.
            string pdf = await Render(BundledFonts.ColorV0, "&#32;&#32;&#32;");

            Assert.Equal(0, FormCount(pdf));
            Assert.Empty(Placements(pdf));
        }

        [Fact]
        public async Task ColrV1GradientGlyph_IsAlsoSharedAcrossOccurrences()
        {
            // The v1 path measures its ink from the glyph clips the paint graph's leaves fill, so a
            // gradient glyph must share too - and its axial shading must be embedded once, not per use.
            string pdf = await Render(BundledFonts.ColorV1, "GGG");

            Assert.Equal(1, FormCount(pdf));
            Assert.Equal(3, Placements(pdf).Count);
            Assert.Single(Regex.Matches(pdf, @"/ShadingType 2"));
        }

        [Fact]
        public async Task RealColorEmoji_RepeatedInText_CollapsesToOneEmbedPerDistinctGlyph()
        {
            // The production case: a real COLR v1 emoji font, one emoji repeated. Also the regression
            // guard for size - each occurrence here is at the same size, so any per-occurrence keying
            // would show up immediately as extra forms.
            string pdf = await Render(BundledFonts.ColorEmoji, "\U0001F600\U0001F600\U0001F600\U0001F600\U0001F600");

            Assert.Equal(1, FormCount(pdf));
            Assert.Equal(5, Placements(pdf).Count);
        }

        // ---- CSS font-palette: the resolved palette is part of the artwork, so part of the key ----
        //
        // A resolved palette's override colors reach the backend as a freshly allocated dictionary per
        // text run, so these prove the key compares them by value: identical overrides must collapse to
        // one form, and any difference - a different color, a different number of entries, or overrides
        // against none at all - must not.

        private static async Task<string> RenderNabla(string body, string paletteValues)
        {
            var family = TtfFontDescription.LoadDescription(BundledFonts.Nabla).FontFamilyInvariantCulture;
            var generator = new PdfGenerator();
            await using (var stream = File.OpenRead(BundledFonts.Nabla))
                await generator.AddFontFromStream(stream);

            var html = "<!DOCTYPE html><html><head><style>" +
                       $"body {{ font-family: '{family}'; font-size: 40pt; color: black; }}" +
                       paletteValues +
                       "</style></head><body>" + body + "</body></html>";

            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        private const string LimeOverride =
            "@font-palette-values --lime { font-family: 'Nabla'; override-colors: 0 rgb(0, 255, 0); }";
        private const string BlueOverride =
            "@font-palette-values --blue { font-family: 'Nabla'; override-colors: 0 rgb(0, 0, 255); }";
        private const string TwoEntryOverride =
            "@font-palette-values --two { font-family: 'Nabla'; override-colors: 0 rgb(0, 255, 0), 4 rgb(0, 255, 0); }";

        [Fact]
        public async Task SameGlyphWithEqualPaletteOverrides_SharesOneForm()
        {
            string pdf = await RenderNabla(
                "<span style='font-palette: --lime'>A</span><span style='font-palette: --lime'>A</span>",
                LimeOverride);

            Assert.Equal(1, FormCount(pdf));
            Assert.Equal(2, Placements(pdf).Count);
        }

        [Fact]
        public async Task SameGlyphWithDifferentOverrideColors_DoesNotShareAForm()
        {
            string pdf = await RenderNabla(
                "<span style='font-palette: --lime'>A</span><span style='font-palette: --blue'>A</span>",
                LimeOverride + BlueOverride);

            Assert.Equal(2, FormCount(pdf));
            Assert.Equal(2, Placements(pdf).Select(p => p.Name).Distinct().Count());
        }

        [Fact]
        public async Task SameGlyphWithDifferentNumbersOfOverriddenEntries_DoesNotShareAForm()
        {
            string pdf = await RenderNabla(
                "<span style='font-palette: --lime'>A</span><span style='font-palette: --two'>A</span>",
                LimeOverride + TwoEntryOverride);

            Assert.Equal(2, FormCount(pdf));
        }

        [Fact]
        public async Task SameGlyphWithAndWithoutPaletteOverrides_DoesNotShareAForm()
        {
            string pdf = await RenderNabla(
                "<span style='font-palette: --lime'>A</span><span>A</span>",
                LimeOverride);

            Assert.Equal(2, FormCount(pdf));
        }

        /// <summary>
        /// The content stream of the first Form XObject in the document. Written uncompressed by these
        /// tests (<see cref="PdfGenerateConfig.CompressContentStreams"/> is false), so it can be read
        /// straight out of the file.
        /// </summary>
        private static string FormContentStream(string pdf)
        {
            var m = Regex.Match(pdf, @"/Subtype /Form.*?stream\r?\n(.*?)\r?\nendstream", RegexOptions.Singleline);
            Assert.True(m.Success, "expected at least one Form XObject with a readable content stream");
            return m.Groups[1].Value;
        }
    }
}
