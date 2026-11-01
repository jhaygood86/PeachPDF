using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PeachDrawing.Core;
using PeachDrawing.Core.ColorGlyphs;
using PeachDrawing.Text;
using PeachDrawing.Text.Outlines;
using PeachPDF.Tests.TestSupport;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// COLR v1 over CFF outlines, the Porter-Duff composite modes a vector target can express, the concentric radial gradient's
    /// inner radius and repeat extend, and the linear gradient's p2 rotation - all through the shared <see cref="ColorGlyphPainter"/>.
    /// </summary>
    public class ColorGlyphCffAndCompositeTests
    {
        private sealed class Recording : IColorGlyphTarget
        {
            public readonly List<string> Log = [];
            public readonly List<ColorGlyphPaint> Paints = [];

            public void FillOutline(GlyphOutline outline, Affine2x3 transform, PaintColor color) => Log.Add("fill");
            public void PushOutlineClip(GlyphOutline outline, Affine2x3 transform) => Log.Add("clip");
            public bool PushOutlineComplementClip(GlyphOutline outline, Affine2x3 transform, Rect bounds)
            {
                Log.Add("cclip");
                return true;
            }
            public void PopClip() => Log.Add("pop");
            public void FillRegion(Rect region, ColorGlyphPaint paint)
            {
                Log.Add(paint is SolidColorGlyphPaint s ? $"solid{s.Color.R:X2}{s.Color.G:X2}{s.Color.B:X2}" : paint.GetType().Name);
                Paints.Add(paint);
            }
            public void PushBlendMode(PaintBlendMode mode) => Log.Add("blend");
            public void PopBlendMode() => Log.Add("endblend");
        }

        private static Recording Paint(char ch)
        {
            Typeface face = TypefaceFixtures.Shared(BundledFonts.ColorCff);
            var painter = new ColorGlyphPainter(face, 100, PaintColor.FromArgb(255, 0, 0, 0));
            var target = new Recording();
            painter.Paint((ushort)face.GlyphOf(new Rune(ch)), painter.Placement(0, 100), target);
            return target;
        }

        private const string Yellow = "solidFFFF00", Blue = "solid0000FF";

        [Fact]
        public void CffColrFont_IsAColorFont_AndDecodesItsOutlines()
        {
            Typeface face = TypefaceFixtures.Shared(BundledFonts.ColorCff);
            Assert.True(face.HasColorGlyphs);
            Assert.True(face.TryGetOutline((ushort)face.GlyphOf(new Rune('X')), out var outline));
            Assert.False(outline.IsEmpty);
        }

        [Fact]
        public void Dest_PaintsOnlyTheBackdrop() => Assert.Equal(["clip", Yellow, "pop"], Paint('9').Log);

        [Fact]
        public void Clear_PaintsNothing() => Assert.Empty(Paint('7').Log);

        [Fact]
        public void Src_PaintsOnlyTheSource() => Assert.Equal(["clip", Blue, "pop"], Paint('8').Log);

        [Fact]
        public void DestOver_PaintsSourceBeneathBackdrop() =>
            Assert.Equal(["clip", Blue, "pop", "clip", Yellow, "pop"], Paint('6').Log);

        [Fact]
        public void SrcIn_ClipsTheSourceToTheBackdropShape() =>
            Assert.Equal(["clip", "clip", Blue, "pop", "pop"], Paint('3').Log);

        [Fact]
        public void DestIn_ClipsTheBackdropToTheSourceShape() =>
            Assert.Equal(["clip", "clip", Yellow, "pop", "pop"], Paint('4').Log);

        [Fact]
        public void SrcAtop_PaintsBackdropThenSourceWithinIt() =>
            Assert.Equal(["clip", Yellow, "pop", "clip", "clip", Blue, "pop", "pop"], Paint('5').Log);

        [Fact]
        public void SrcOut_ClipsTheSourceToOutsideTheBackdrop() =>
            Assert.Equal(["cclip", "clip", Blue, "pop", "pop"], Paint('A').Log);

        [Fact]
        public void DestOut_ClipsTheBackdropToOutsideTheSource() =>
            Assert.Equal(["cclip", "clip", Yellow, "pop", "pop"], Paint('B').Log);

        [Fact]
        public void Xor_PaintsEachOperandOutsideTheOther() =>
            Assert.Equal(["cclip", "clip", Blue, "pop", "pop", "cclip", "clip", Yellow, "pop", "pop"], Paint('C').Log);

        [Fact]
        public void ComplementModes_FallBackToSourceOver_WhenTheTargetCannotClipToAComplement()
        {
            Typeface face = TypefaceFixtures.Shared(BundledFonts.ColorCff);
            var painter = new ColorGlyphPainter(face, 100, PaintColor.FromArgb(255, 0, 0, 0));
            var target = new NoComplementRecording();
            painter.Paint((ushort)face.GlyphOf(new Rune('A')), painter.Placement(0, 100), target);
            Assert.Equal(["clip", Yellow, "pop", "clip", Blue, "pop"], target.Log);
        }

        [Fact]
        public void Plus_OnATargetWithNoAdditiveBlend_FallsBackToSourceOver() =>
            Assert.Equal(["clip", Yellow, "pop", "clip", Blue, "pop"], Paint('D').Log);

        private sealed class PixelRecording : IColorGlyphTarget
        {
            public readonly List<string> Log = [];
            public readonly List<ColorGlyphPaint> Paints = [];
            public bool SupportsAdditiveComposite => true;
            public bool SupportsPeriodicConeGradients => true;
            public void FillOutline(GlyphOutline outline, Affine2x3 transform, PaintColor color) { }
            public void PushOutlineClip(GlyphOutline outline, Affine2x3 transform) => Log.Add("clip");
            public void PopClip() => Log.Add("pop");
            public void FillRegion(Rect region, ColorGlyphPaint paint)
            {
                Log.Add(paint is SolidColorGlyphPaint s ? $"solid{s.Color.R:X2}{s.Color.G:X2}{s.Color.B:X2}" : paint.GetType().Name);
                Paints.Add(paint);
            }
            public void PushBlendMode(PaintBlendMode mode) => Log.Add("blend:" + mode);
            public void PopBlendMode() => Log.Add("endblend");
        }

        private static PixelRecording PaintOnPixelTarget(char ch)
        {
            Typeface face = TypefaceFixtures.Shared(BundledFonts.ColorCff);
            var painter = new ColorGlyphPainter(face, 100, PaintColor.FromArgb(255, 0, 0, 0));
            var target = new PixelRecording();
            painter.Paint((ushort)face.GlyphOf(new Rune(ch)), painter.Placement(0, 100), target);
            return target;
        }

        [Fact]
        public void Plus_OnATargetWithAnAdditiveBlend_PaintsTheSourceUnderThePlusBlendMode() =>
            Assert.Equal(["clip", Yellow, "pop", "blend:Plus", "clip", Blue, "pop", "endblend"], PaintOnPixelTarget('D').Log);

        [Theory]
        [InlineData('D', true)]  // PLUS
        [InlineData('N', true)]  // cone, repeat
        [InlineData('O', true)]  // cone, reflect
        [InlineData('E', false)] // cone, pad
        [InlineData('3', false)] // SRC_IN
        [InlineData('L', false)] // concentric, reflect: exact in vector
        [InlineData('X', false)] // no color paint at all
        public void RequiresRasterFidelity_IsTrueOnlyForWhatAVectorTargetCannotDraw(char ch, bool expected)
        {
            Typeface face = TypefaceFixtures.Shared(BundledFonts.ColorCff);
            var painter = new ColorGlyphPainter(face, 100, PaintColor.FromArgb(255, 0, 0, 0));
            Assert.Equal(expected, painter.RequiresRasterFidelity((ushort)face.GlyphOf(new Rune(ch))));
        }

        [Fact]
        public void ConeRepeat_OnAPixelTarget_AsksForARepeatingTwoCircleBrush()
        {
            var radial = Assert.IsType<RadialColorGlyphPaint>(Assert.Single(PaintOnPixelTarget('N').Paints));
            Assert.True(radial.Repeating);
            Assert.Equal(10.0, radial.FocalRadius, 6);
            Assert.Equal(30.0, radial.Radius, 6);
            Assert.NotEqual(radial.Center, radial.Focal);

            // Not requested of a target that cannot repeat it: the same glyph pads there.
            Assert.False(Assert.IsType<RadialColorGlyphPaint>(Assert.Single(Paint('N').Paints)).Repeating);
        }

        [Fact]
        public void ConeReflect_OnAPixelTarget_RunsTheRampForwardThenBackOverTwiceTheCircles()
        {
            var radial = Assert.IsType<RadialColorGlyphPaint>(Assert.Single(PaintOnPixelTarget('O').Paints));
            Assert.True(radial.Repeating);

            // Circles at t = 2: center 300 + 2 * 300 = 900 (design), radius 100 + 2 * 200 = 500.
            Assert.Equal(50.0, radial.Radius, 6);
            Assert.Equal(4, radial.Colors.Count);
            Assert.Equal(radial.Colors[0], radial.Colors[3]);
            Assert.Equal(radial.Colors[1], radial.Colors[2]);
            Assert.Equal(0.0, radial.Positions[0]);
            Assert.Equal(1.0, radial.Positions[3], 6);
            for (int i = 1; i < radial.Positions.Count; i++)
                Assert.True(radial.Positions[i] > radial.Positions[i - 1]);
        }

        [Fact]
        public async Task PlusAndConeGlyphs_AreEmbeddedAsPicturesInAPdf_AndOtherGlyphsStayVector()
        {
            var generator = new PdfGenerator();
            await using (var stream = File.OpenRead(BundledFonts.ColorCff))
                await generator.AddFontFromStream(stream);

            string family = TypefaceFixtures.FamilyNameOf(BundledFonts.ColorCff);

            async Task<string> Render(string text)
            {
                string html = $"<!DOCTYPE html><html><head><style>body {{ font-family: '{family}'; font-size: 100pt; }}</style></head><body>{text}</body></html>";
                var doc = await generator.GeneratePdf(html, new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false });
                var ms = new MemoryStream();
                doc.Save(ms);
                return Encoding.Latin1.GetString(ms.ToArray());
            }

            string vector = await Render("3 E");
            Assert.DoesNotContain("/Subtype /Image", vector);

            string raster = await Render("D N O");
            Assert.Contains("/Subtype /Image", raster);
            Assert.DoesNotContain("/BM /Plus", raster);
        }

        [Fact]
        public void RadialWithDifferentCenters_KeepsItsInnerRadius()
        {
            var radial = Assert.IsType<RadialColorGlyphPaint>(Assert.Single(Paint('E').Paints));
            Assert.Equal(10.0, radial.FocalRadius, 6); // 100 design units at 100px / 1000 upem
            Assert.Equal(30.0, radial.Radius, 6);
            Assert.NotEqual(radial.Center, radial.Focal);
        }

        [Fact]
        public void DestAtop_PaintsSourceThenBackdropWithinIt() =>
            Assert.Equal(["clip", Blue, "pop", "clip", "clip", Yellow, "pop", "pop"], Paint('K').Log);

        [Fact]
        public void ShapeOfLayersUnderATranslate_ClipsOncePerLayerGlyph() =>
            Assert.Equal(["clip", "clip", Blue, "pop", "pop", "clip", "clip", Blue, "pop", "pop"], Paint('F').Log);

        [Fact]
        public void ShapeOfAColorGlyphReference_IsTheShapesOfItsOperands()
        {
            // srcAtop is a composite of the box and the triangle, so the triangle is clipped to both, once each.
            var log = Paint('G').Log;
            Assert.Equal(4, log.Count(e => e == "clip"));
            Assert.Equal(2, log.Count(e => e == Blue));
        }

        [Fact]
        public void BareGradientOperand_HasNoShape_SoInPaintsUnclipped_AndOutPaintsNothing()
        {
            Assert.Equal(["clip", Blue, "pop"], Paint('H').Log);
            Assert.Empty(Paint('J').Log);
        }

        [Theory]
        [InlineData('L')]
        [InlineData('M')]
        public void RadialWithInnerRadiusTiledOutward_StartsAtTheCenterWithAnInterpolatedColor(char glyph)
        {
            var radial = Assert.IsType<RadialColorGlyphPaint>(Assert.Single(Paint(glyph).Paints));
            Assert.Equal(0.0, radial.Positions[0]);
            Assert.Equal(1.0, radial.Positions[^1], 6);
            Assert.True(radial.Colors.Count > 3);
            for (int i = 1; i < radial.Positions.Count; i++)
                Assert.True(radial.Positions[i] > radial.Positions[i - 1]);
        }

        private sealed class NoComplementRecording : IColorGlyphTarget
        {
            public readonly List<string> Log = [];
            public void FillOutline(GlyphOutline outline, Affine2x3 transform, PaintColor color) { }
            public void PushOutlineClip(GlyphOutline outline, Affine2x3 transform) => Log.Add("clip");
            public void PopClip() => Log.Add("pop");
            public void FillRegion(Rect region, ColorGlyphPaint paint) =>
                Log.Add(paint is SolidColorGlyphPaint s ? $"solid{s.Color.R:X2}{s.Color.G:X2}{s.Color.B:X2}" : paint.GetType().Name);
            public void PushBlendMode(PaintBlendMode mode) { }
            public void PopBlendMode() { }
        }

        [Fact]
        public void RadialInnerRadius_PadsTheFirstColorInsideIt()
        {
            var radial = Assert.IsType<RadialColorGlyphPaint>(Assert.Single(Paint('0').Paints));

            // Concentric circles: the gradient radiates from the center, and the stops are respaced so the first color holds
            // out to the inner radius (150 of 400) and the last is reached at the outer one.
            Assert.Equal(radial.Center, radial.Focal);
            Assert.Equal(3, radial.Colors.Count);
            Assert.Equal(0.0, radial.Positions[0]);
            Assert.Equal(150.0 / 400.0, radial.Positions[1], 6);
            Assert.Equal(1.0, radial.Positions[2], 6);
            Assert.Equal(radial.Colors[0], radial.Colors[1]);
            Assert.Equal(40.0, radial.Radius, 6); // 400 design units at 100px / 1000 upem
        }

        [Fact]
        public void RadialRepeat_TilesTheStopsOutwardOverTheGlyph()
        {
            var radial = Assert.IsType<RadialColorGlyphPaint>(Assert.Single(Paint('1').Paints));

            // r0 = 0, r1 = 150: the ramp repeats outward until the box's far corner (about 500 units) is covered.
            Assert.True(radial.Colors.Count > 4, "expected the stops to be tiled");
            Assert.True(radial.Radius >= 50.0 - 1e-6);
            Assert.Equal(1.0, radial.Positions[^1], 6);
            for (int i = 1; i < radial.Positions.Count; i++)
                Assert.True(radial.Positions[i] > radial.Positions[i - 1]);
        }

        [Fact]
        public void LinearP2_RotatesTheGradientAxisToBePerpendicularToP0P2()
        {
            var linear = Assert.IsType<LinearColorGlyphPaint>(Assert.Single(Paint('2').Paints));

            // p0 = (100,0), p1 = (900,0), p2 = (500,400): the color lines run along (400,400), so the axis is p0->p1 with its
            // component along that direction removed: (800,0) - 400*(1,1) = (400,-400).
            double dx = linear.End.X - linear.Start.X, dy = linear.End.Y - linear.Start.Y;
            Assert.Equal(40.0, dx, 6);
            Assert.Equal(40.0, dy, 6); // the page's y axis points down
        }

        [Fact]
        public async Task CffColrFont_RendersAsVectorArtworkInAPdf()
        {
            var generator = new PdfGenerator();
            await using (var stream = File.OpenRead(BundledFonts.ColorCff))
                await generator.AddFontFromStream(stream);

            string family = TypefaceFixtures.FamilyNameOf(BundledFonts.ColorCff);
            string html = $"<!DOCTYPE html><html><head><style>body {{ font-family: '{family}'; font-size: 100pt; }}</style></head><body>0 3 B E</body></html>";
            var doc = await generator.GeneratePdf(html, new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false });
            var ms = new MemoryStream();
            doc.Save(ms);
            string pdf = Encoding.Latin1.GetString(ms.ToArray());

            Assert.Contains("/ShadingType 3", pdf); // the radial gradient
            Assert.Matches(@"\b0 0 1 rg", pdf);   // SRC_IN paints only the blue source
            Assert.True(pdf.Split("W n").Length > 3, "glyph clips");
            Assert.Contains("W* n", pdf);           // DEST_OUT: an even-odd clip to the outside of the triangle
        }
    }
}
