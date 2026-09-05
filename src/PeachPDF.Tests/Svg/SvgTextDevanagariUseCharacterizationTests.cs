using PeachPDF.Adapters;
using PeachPDF.Fonts.OpenType;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.Svg;
using PeachPDF.Tests.TestSupport;
using PeachPDF.Text;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace PeachPDF.Tests.Svg
{
    /// <summary>
    /// Real-font characterization for SVG <c>&lt;text&gt;</c>'s Devanagari Universal Shaping Engine
    /// (USE) syllable reordering (issue #533) - mirrors
    /// <see cref="PeachPDF.Tests.Html.Core.DevanagariUseCharacterizationTests"/>'s own "prove it isn't
    /// a no-op" standard, applied to SVG's independent pipeline. The core USE algorithm itself
    /// (category classification, syllable scanning, glyph reordering) is exhaustively verified against
    /// real HarfBuzz's own output in <c>DevanagariUseShapingCharacterizationTests</c> and
    /// <c>PeachPDF.Tests.Html.Core.DevanagariUseCharacterizationTests</c>; this file only proves SVG's
    /// own wiring (<c>SvgRenderer.ResolveComplexScriptRuns</c>) reaches it with the right values.
    /// </summary>
    public class SvgTextDevanagariUseCharacterizationTests
    {
        private const string Ka = "क";
        private const string Virama = "्";
        private const string Ssa = "ष";
        private const string VowelSignI = "ि";

        private static readonly PdfSharpAdapter Adapter = new() { PixelsPerPoint = 1.0 };

        private static TestRecordingGraphics.DrawStringCall RenderSingleCall(string body)
        {
            var markup = $$"""
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100">
                  {{body}}
                </svg>
                """;
            var document = SvgTreeBuilder.Build(new XElementSvgSourceNode(XDocument.Parse(markup).Root!), Adapter);
            var g = new TestRecordingGraphics();
            SvgRenderer.RenderInto(g, document, new RRect(0, 0, 200, 100));
            return Assert.Single(g.DrawStringCalls);
        }

        private static OpenTypeDescriptor Descriptor()
        {
            var face = XFontSource.GetOrCreateFrom(File.ReadAllBytes(BundledFonts.Devanagari)).Fontface;
            return new OpenTypeDescriptor("svg-devanagari-test", "svg-devanagari-test", XFontStyle.Regular, face,
                new XPdfFontOptions(PdfFontEncoding.Unicode));
        }

        [Fact]
        public void ConjunctWithMatra_RealFontLigatesTheConjunctInsteadOfFourIndependentGlyphs()
        {
            var draw = RenderSingleCall($"""<text x="10" y="50" font-size="20">{Ka}{Virama}{Ssa}{VowelSignI}</text>""");
            Assert.Equal(4, draw.Features!.Value.UseCategories!.Count);

            var descriptor = Descriptor();
            var shaped = descriptor.Shape(draw.Text, draw.Features.Value);
            var unshaped = descriptor.Shape(draw.Text, TextShapingFeatures.Default);

            // TextShapingFeatures.Default never requests UseCategories, so GsubShaper.ApplyUseShaping
            // (nukt/ccmp/locl/akhn/rphf/half/rkrf/cjct/abvs/blws/pres/psts) never runs for it - it shapes
            // as 4 independent nominal glyphs (KA, VIRAMA, SSA, VOWEL_SIGN_I). The real conjunct
            // ligature (cjct) that SVG's own computed UseCategories requests fuses KA+VIRAMA+SSA into
            // one glyph, so the shaped result must be strictly fewer glyphs - proof the wiring reaches
            // real GSUB substitution, not just that a UseCategories array with the right shape exists.
            Assert.Equal(4, unshaped.Count);
            Assert.True(shaped.Count < unshaped.Count,
                $"shaped.Count={shaped.Count} should be fewer than the unshaped nominal glyph count {unshaped.Count} - cjct conjunct ligation may not have run");
        }

        [Fact]
        public void SimpleSyllable_RealFontReordersThePreBaseMatraBeforeTheConsonant()
        {
            var draw = RenderSingleCall($"""<text x="10" y="50" font-size="20">{Ka}{VowelSignI}</text>""");

            var descriptor = Descriptor();
            var shaped = descriptor.Shape(draw.Text, draw.Features!.Value);
            var logicalOrderOnly = descriptor.Shape(draw.Text, draw.Features.Value with
            {
                UseCategories = null,
            });

            // Without USE reordering, KA (cluster 0) then VOWEL_SIGN_I (cluster 2) shape in that same
            // source order. With it, the pre-base matra moves before the consonant it belongs to - a
            // different glyph-index sequence than the logical-order/no-reorder shape, proving
            // UseReorderer actually ran via exactly the values SVG computed.
            var shapedIds = shaped.Select(sg => sg.GlyphIndex);
            var logicalIds = logicalOrderOnly.Select(sg => sg.GlyphIndex);
            Assert.NotEqual(logicalIds, shapedIds);
        }

        [Fact]
        public void PlainLatinText_ShapesIdenticallyWithOrWithoutTheSvgComputedFeatures()
        {
            var draw = RenderSingleCall("""<text x="10" y="50" font-size="20">Hi</text>""");
            Assert.Null(draw.Features!.Value.UseCategories);

            var descriptor = Descriptor();
            var viaSvg = descriptor.Shape(draw.Text, draw.Features.Value).Select(sg => sg.GlyphIndex);
            var plain = descriptor.Shape(draw.Text, TextShapingFeatures.Default).Select(sg => sg.GlyphIndex);

            Assert.Equal(plain, viaSvg);
        }
    }
}
