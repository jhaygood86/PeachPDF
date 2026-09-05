using PeachPDF.Adapters;
using PeachPDF.Fonts.OpenType;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.Svg;
using PeachPDF.Tests.TestSupport;
using PeachPDF.Text;
using PeachPDF.Text.Shaping.Arabic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace PeachPDF.Tests.Svg
{
    /// <summary>
    /// Real-font characterization for SVG <c>&lt;text&gt;</c>'s Arabic-family joining (issue #533) -
    /// mirrors <see cref="PeachPDF.Tests.Html.Core.ArabicJoiningCharacterizationTests"/>'s own "prove
    /// it isn't a no-op" standard against real font data, applied to SVG's independent pipeline:
    /// renders through the real SVG pipeline with a <see cref="TestRecordingGraphics"/> mock to
    /// capture exactly the <c>(text, TextShapingFeatures)</c> pair <c>SvgRenderer.PaintGlyphs</c>
    /// actually hands to <see cref="RGraphics.DrawString"/>, then re-shapes that exact pair through a
    /// real <see cref="OpenTypeDescriptor"/> (the same bundled Noto Sans Arabic/Aref Ruqaa subsets
    /// HTML's own characterization tests use) to confirm real GSUB/GPOS substitution/positioning
    /// actually happens - not just that a correctly-shaped <see cref="TextShapingFeatures"/> value got
    /// built and never used.
    /// </summary>
    public class SvgTextArabicJoiningCharacterizationTests
    {
        private const string Beh = "ب";
        private const string Yeh = "ي";
        private const string Teh = "ت";
        private const string Alef = "ا";
        private const string Lam = "ل";

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

        private static OpenTypeDescriptor Descriptor(string path)
        {
            var face = XFontSource.GetOrCreateFrom(File.ReadAllBytes(path)).Fontface;
            return new OpenTypeDescriptor("svg-arabic-test", "svg-arabic-test", XFontStyle.Regular, face,
                new XPdfFontOptions(PdfFontEncoding.Unicode));
        }

        [Fact]
        public void ThreeLetterWord_RealFontJoinedFormsDifferFromIsolatedForms()
        {
            var draw = RenderSingleCall($"""<text x="10" y="50" font-size="20">{Beh}{Yeh}{Teh}</text>""");

            var descriptor = Descriptor(BundledFonts.Arabic);
            var joined = descriptor.Shape(draw.Text, draw.Features!.Value).Select(sg => sg.GlyphIndex).ToArray();
            var isolated = descriptor.Shape(draw.Text, draw.Features.Value with
            {
                JoiningForms = new[] { ArabicJoiningForm.Isol, ArabicJoiningForm.Isol, ArabicJoiningForm.Isol },
            }).Select(sg => sg.GlyphIndex).ToArray();

            // Same ccmp decomposition shape (base + combining-mark glyph per dotted letter, per
            // ArabicJoiningCharacterizationTests' own remarks), but the real per-position substitution
            // driven by exactly what SvgRenderer computed differs from all-isolated forms - not a
            // no-op.
            Assert.Equal(isolated.Length, joined.Length);
            Assert.NotEqual(isolated, joined);
        }

        [Fact]
        public void LamAlef_RealFontFiresRligLigatureViaSvgComputedFeatures()
        {
            var draw = RenderSingleCall($"""<text x="10" y="50" font-size="20">{Lam}{Alef}</text>""");

            var descriptor = Descriptor(BundledFonts.Arabic);
            var withRlig = descriptor.Shape(draw.Text, draw.Features!.Value).Select(sg => sg.GlyphIndex).ToArray();
            var positionalOnly = descriptor.Shape(draw.Text, draw.Features.Value with { Ligatures = LigatureFeatures.None })
                .Select(sg => sg.GlyphIndex).ToArray();

            Assert.Equal(2, withRlig.Length);
            Assert.Equal(2, positionalOnly.Length);
            Assert.NotEqual(positionalOnly, withRlig);
        }

        [Fact]
        public void RtlWord_ReverseForDisplayActuallyChangesTheShapedGlyphOrder()
        {
            var draw = RenderSingleCall($"""<text x="190" y="50" font-size="20" direction="rtl">{Beh}{Yeh}{Teh}</text>""");
            Assert.True(draw.Features!.Value.ReverseForDisplay);

            var descriptor = Descriptor(BundledFonts.Arabic);
            var displayed = descriptor.Shape(draw.Text, draw.Features.Value).Select(sg => sg.GlyphIndex).ToArray();
            var logicalOnly = descriptor.Shape(draw.Text, draw.Features.Value with { ReverseForDisplay = false })
                .Select(sg => sg.GlyphIndex).ToArray();

            // Same glyph set (reversal never changes which glyphs GSUB/GPOS produced), but a genuinely
            // different order - proving ReverseForDisplay is a real, load-bearing request reaching the
            // shaper via exactly the value SvgRenderer computed, not a flag nobody reads.
            Assert.Equal(logicalOnly.Length, displayed.Length);
            Assert.NotEqual(logicalOnly, displayed);
        }

        [Fact]
        public void CursiveAttachment_RealFontProducesPlausiblePositiveWidth_NotCollapsedByBadFormula()
        {
            // Mirrors PeachPDF.Tests.Html.Core.ArabicCursiveAttachmentCharacterizationTests' own
            // regression signature for GposPositioner.TryApplyCursivePair against a font whose Arabic
            // joining actually relies on GPOS cursive attachment (curs), not only positional
            // substitution - a wrong formula previously collapsed a connected word's measured width to
            // roughly zero.
            var draw = RenderSingleCall($"""<text x="10" y="50" font-size="20">{Teh}{Beh}</text>""");

            var descriptor = Descriptor(BundledFonts.ArabicCursive);
            var shaped = descriptor.Shape(draw.Text, draw.Features!.Value);

            double totalAdvance = 0;
            foreach (var sg in shaped)
                totalAdvance += descriptor.GlyphIndexToWidth(sg.GlyphIndex) + sg.XAdvanceDelta;

            // 300 design units, the same generous floor
            // ArabicCursiveAttachmentCharacterizationTests uses (real single Arabic letters in this
            // font measure several hundred design units wide alone) - totalAdvance is already in this
            // font's own design units, so no unitsPerEm scaling is needed here.
            const double minPlausibleWidth = 300.0;
            Assert.True(totalAdvance > minPlausibleWidth,
                $"totalAdvance={totalAdvance} design units is implausibly small for a 2-letter cursively-connected word (floor {minPlausibleWidth})");
        }

        [Fact]
        public void PlainLatinText_ShapesIdenticallyWithOrWithoutTheSvgComputedFeatures()
        {
            // Regression: this whole feature must be a complete no-op for ordinary (non-Arabic-family)
            // text - confirmed here by proving the SVG-computed features (which are just the run's
            // ordinary ShapingFeatures, no JoiningForms) shape identically to the plain default.
            var draw = RenderSingleCall("""<text x="10" y="50" font-size="20">Hi</text>""");
            Assert.Null(draw.Features!.Value.JoiningForms);

            var descriptor = Descriptor(BundledFonts.Arabic);
            var viaSvg = descriptor.Shape(draw.Text, draw.Features.Value).Select(sg => sg.GlyphIndex).ToArray();
            var plain = descriptor.Shape(draw.Text, TextShapingFeatures.Default).Select(sg => sg.GlyphIndex).ToArray();

            Assert.Equal(plain, viaSvg);
        }
    }
}
