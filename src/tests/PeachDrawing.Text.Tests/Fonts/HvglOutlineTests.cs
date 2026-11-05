using System.Text;
using PeachDrawing.Text.Export;
using PeachDrawing.Text.Internal.Fonts.OpenType.Hvgl;
using PeachDrawing.Text.Outlines;
using Instruction = PeachDrawing.Text.Internal.Fonts.OpenType.Hvgl.HvfOutlineBuilder.Instruction;
using Action = PeachDrawing.Text.Internal.Fonts.OpenType.Hvgl.HvfOutlineBuilder.Action;

namespace PeachDrawing.Text.Tests.Fonts
{
    /// <summary>
    /// Glyphs drawn from an <c>hvgl</c> table (Apple's hierarchical variable fonts) by Apple's HVF library. The conversion of
    /// what the library draws into an outline is tested on every host with a scripted stream of instructions; the library
    /// itself, and the system font that has such glyphs, only exist on an Apple OS new enough to ship them.
    /// </summary>
    public class HvglOutlineTests
    {
        private static OutlinePoint P(double x, double y) => new(x, y);

        // ---- HvfOutlineBuilder: any host ----

        [Fact]
        public void APathOfLinesAndQuads_BecomesOneContour_StartingAtItsFirstPoint()
        {
            var builder = new HvfOutlineBuilder();
            Assert.Equal(Action.Continue, builder.Handle(Instruction.BeginPart));
            builder.Handle(Instruction.BeginPath);
            builder.Handle(Instruction.AddPoint, 10, 20);
            builder.Handle(Instruction.AddPoint, 110, 20);
            builder.Handle(Instruction.AddQuad, 160, 70, 110, 120);
            builder.Handle(Instruction.ClosePath);
            builder.Handle(Instruction.EndPath);
            builder.Handle(Instruction.EndPart);

            var outline = builder.Finish();

            var contour = Assert.Single(outline.Contours);
            Assert.Equal(P(10, 20), contour.Start);
            Assert.Equal(2, contour.Segments.Count);

            Assert.False(contour.Segments[0].IsCubic);
            Assert.Equal(P(110, 20), contour.Segments[0].End);

            // A quadratic curve is the cubic whose controls are two thirds of the way from each end to the quadratic control point.
            var curve = contour.Segments[1];
            Assert.True(curve.IsCubic);
            Assert.Equal(110 + 2.0 / 3.0 * (160 - 110), curve.Control1.X, 9);
            Assert.Equal(20 + 2.0 / 3.0 * (70 - 20), curve.Control1.Y, 9);
            Assert.Equal(110 + 2.0 / 3.0 * (160 - 110), curve.Control2.X, 9);
            Assert.Equal(120 + 2.0 / 3.0 * (70 - 120), curve.Control2.Y, 9);
            Assert.Equal(P(110, 120), curve.End);
        }

        [Fact]
        public void EveryBeginPath_StartsANewContour_AndAPartsPathsFollowEachOther()
        {
            var builder = new HvfOutlineBuilder();
            builder.Handle(Instruction.BeginPart);
            builder.Handle(Instruction.BeginPart); // a part of the composite
            builder.Handle(Instruction.BeginPath);
            builder.Handle(Instruction.AddPoint, 0, 0);
            builder.Handle(Instruction.AddPoint, 10, 0);
            builder.Handle(Instruction.ClosePath);
            builder.Handle(Instruction.EndPath);
            builder.Handle(Instruction.EndPart);
            builder.Handle(Instruction.BeginPart); // another
            builder.Handle(Instruction.BeginPath);
            builder.Handle(Instruction.AddPoint, 50, 50);
            builder.Handle(Instruction.AddLine, 60, 50);
            builder.Handle(Instruction.EndPath);
            builder.Handle(Instruction.EndPart);
            builder.Handle(Instruction.EndPart);

            var outline = builder.Finish();

            Assert.Equal(2, outline.Contours.Count);
            Assert.Equal(P(0, 0), outline.Contours[0].Start);
            Assert.Equal(P(50, 50), outline.Contours[1].Start);
            Assert.Equal(P(60, 50), outline.Contours[1].Segments[0].End);
        }

        [Fact]
        public void APathLeftOpen_IsEndedWhenTheNextOneBeginsAndWhenTheStreamStops()
        {
            var builder = new HvfOutlineBuilder();
            builder.Handle(Instruction.BeginPath);
            builder.Handle(Instruction.AddPoint, 1, 1);
            builder.Handle(Instruction.AddPoint, 2, 2);
            builder.Handle(Instruction.BeginPath);
            builder.Handle(Instruction.AddPoint, 5, 5);
            builder.Handle(Instruction.AddPoint, 6, 6);
            builder.Handle(Instruction.Stop);

            Assert.Equal(2, builder.Finish().Contours.Count);
        }

        [Fact]
        public void AStraightEdgeDrawnAsACurveWithItsControlAtAnEnd_IsALine()
        {
            var builder = new HvfOutlineBuilder();
            builder.Handle(Instruction.BeginPath);
            builder.Handle(Instruction.AddPoint, 0, 0);
            builder.Handle(Instruction.AddQuad, 0, 0, 100, 0);   // control at the start
            builder.Handle(Instruction.AddQuad, 100, 50, 100, 50); // control at the end
            builder.Handle(Instruction.EndPath);

            var segments = Assert.Single(builder.Finish().Contours).Segments;

            Assert.All(segments, segment => Assert.False(segment.IsCubic));
            Assert.Equal([P(100, 0), P(100, 50)], segments.Select(s => s.End));
        }

        [Fact]
        public void ACubicCurve_IsKeptAsItIs()
        {
            var builder = new HvfOutlineBuilder();
            builder.Handle(Instruction.BeginPath);
            builder.Handle(Instruction.AddPoint, 0, 0);
            builder.Handle(Instruction.AddCubic, 10, 20, 30, 40, 50, 60);

            var segment = Assert.Single(Assert.Single(builder.Finish().Contours).Segments);

            Assert.True(segment.IsCubic);
            Assert.Equal(P(10, 20), segment.Control1);
            Assert.Equal(P(30, 40), segment.Control2);
            Assert.Equal(P(50, 60), segment.End);
        }

        [Fact]
        public void ACurveBeforeAnyPoint_OpensItsContourAtTheCurve_InsteadOfFailing()
        {
            var builder = new HvfOutlineBuilder();
            builder.Handle(Instruction.AddQuad, 1, 1, 5, 5);
            builder.Handle(Instruction.BeginPath);
            builder.Handle(Instruction.AddCubic, 1, 2, 3, 4, 5, 6);

            var outline = builder.Finish();

            Assert.Equal(2, outline.Contours.Count);
        }

        [Fact]
        public void NoPaths_IsAnEmptyOutline()
        {
            var builder = new HvfOutlineBuilder();
            builder.Handle(Instruction.BeginPart);
            builder.Handle(Instruction.EndPart);

            Assert.True(builder.Finish().IsEmpty);
        }

        [Fact]
        public void TheBuilderAnswersContinueToEveryInstruction()
        {
            var builder = new HvfOutlineBuilder();
            foreach (var instruction in Enum.GetValues<Instruction>())
                Assert.Equal(Action.Continue, builder.Handle(instruction));
        }

        // ---- The library and a font that uses it: Apple hosts new enough to have both ----

        private static Typeface? PingFang()
        {
            if (!HvfRenderer.IsAvailable)
                Assert.Skip("Apple's HVF library is not available on this host.");

            var fonts = new FontSet();
            if (fonts.TryFindFamily("PingFang SC", out var family) && family.TryMatch(new TypefaceQuery(), out var match))
                return match.Typeface;

            Assert.Skip("This host has no PingFang SC, the system font that has hvgl glyphs.");
            return null;
        }

        private static (double MinX, double MinY, double MaxX, double MaxY) Bounds(GlyphOutline outline)
        {
            var points = outline.Contours.SelectMany(c => new[] { c.Start }.Concat(c.Segments.Select(s => s.End))).ToList();
            return (points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y));
        }

        [Fact]
        public void AnHvglGlyph_IsDrawnInFontUnits_ThatFitItsAdvance()
        {
            var typeface = PingFang()!;
            Assert.True(typeface.TryMapRune(new Rune('中'), out var glyph));

            Assert.True(typeface.TryGetOutline(glyph, out var outline));

            var bounds = Bounds(outline);
            var upem = typeface.Metrics.UnitsPerEm;
            Assert.True(bounds.MinX >= -upem * 0.1 && bounds.MaxX <= typeface.GetAdvance(glyph) + upem * 0.1, "the glyph sits on its advance");
            Assert.True(bounds.MaxY > upem * 0.5 && bounds.MaxY < upem * 1.2, "the glyph stands above the baseline");
            Assert.True(outline.Contours.Count > 1);
        }

        [Fact]
        public void AnHvglGlyph_WithNoOutline_IsNoOutline()
        {
            var typeface = PingFang()!;
            Assert.True(typeface.TryMapRune(new Rune(' '), out var space));

            Assert.False(typeface.TryGetOutline(space, out var outline));
            Assert.True(outline.IsEmpty);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(int.MaxValue)]
        public void AGlyphTheFontDoesNotHave_IsNoOutline_AndNotAFailure(int glyph)
        {
            var renderer = PingFang()!.Face.Fontface.Hvf;
            if (renderer is null)
                Assert.Skip("This host's PingFang has no hvgl table.");

            Assert.False(renderer.TryRender(glyph, default, out var outline));
            Assert.True(outline.IsEmpty);
        }

        [Fact]
        public void TheAxesOfAnHvglFont_ChangeTheOutline()
        {
            var typeface = PingFang()!;
            Assert.True(typeface.TryMapRune(new Rune('中'), out var glyph));
            Assert.NotEmpty(typeface.Axes);

            var weight = typeface.Axes.Single(a => a.Tag == "wght");
            Assert.True(typeface.WithAxes([new AxisSetting("wght", weight.Minimum)]).TryGetOutline(glyph, out var light));
            Assert.True(typeface.WithAxes([new AxisSetting("wght", weight.Maximum)]).TryGetOutline(glyph, out var heavy));
            Assert.True(typeface.TryGetOutline(glyph, out var normal));

            var (lightMin, _, lightMax, _) = Bounds(light);
            var (heavyMin, _, heavyMax, _) = Bounds(heavy);
            Assert.True(heavyMax - heavyMin > lightMax - lightMin, "a heavier weight is wider");

            // The typeface a query for a normal weight matches is the font at weight 400, and asking for that weight says the same.
            Assert.True(typeface.WithAxes([new AxisSetting("wght", 400)]).TryGetOutline(glyph, out var atNormalWeight));
            Assert.Equal(Bounds(normal), Bounds(atNormalWeight));
        }

        [Fact]
        public void AnHvglFont_CanBeDrawnFromSeveralThreadsAtOnce()
        {
            var typeface = PingFang()!;
            Assert.True(typeface.TryMapRune(new Rune('国'), out var glyph));
            Assert.True(typeface.TryGetOutline(glyph, out var expected));
            var expectedBounds = Bounds(expected);

            Parallel.For(0, 64, _ =>
            {
                Assert.True(typeface.TryGetOutline(glyph, out var outline));
                Assert.Equal(expectedBounds, Bounds(outline));
            });
        }

        [Fact]
        public void AnHvglFont_ExportsAsAStaticFontWithCffOutlines_ThatDrawsTheSameGlyph()
        {
            var typeface = PingFang()!;
            Assert.True(typeface.TryMapRune(new Rune('中'), out var glyph));
            Assert.True(typeface.TryGetOutline(glyph, out var original));

            var exported = TypefaceExporter.ExportSubset(typeface, [glyph], keepCharacterMap: true);

            Assert.True(exported.IsSubset);
            Assert.True(exported.HasCffOutlines);
            Assert.True(exported.Data.Length < typeface.Face.Fontface.FontSource.Bytes.Length / 20, "only the glyph asked for is written");
            Assert.True(TypefaceExporter.ExportSubset(typeface, [glyph], keepCharacterMap: false).Data.Length < exported.Data.Length, "without its character map the font is smaller");

            Assert.True(new FontSet().AddData(exported.Data).TryMatch(new TypefaceQuery(), out var match));
            var reloaded = match.Typeface;
            Assert.True(reloaded.TryMapRune(new Rune('中'), out var reloadedGlyph));
            Assert.True(reloaded.TryGetOutline(reloadedGlyph, out var written));

            // The charstring has whole-number coordinates, so the outline is the original to within a unit.
            var (ox0, oy0, ox1, oy1) = Bounds(original);
            var (wx0, wy0, wx1, wy1) = Bounds(written);
            Assert.InRange(wx0, ox0 - 1, ox0 + 1);
            Assert.InRange(wy0, oy0 - 1, oy0 + 1);
            Assert.InRange(wx1, ox1 - 1, ox1 + 1);
            Assert.InRange(wy1, oy1 - 1, oy1 + 1);
            Assert.Equal(original.Contours.Count, written.Contours.Count);
        }

        [Fact]
        public void TheLibraryIsNotAvailableOffAnApplePlatform()
        {
            if (OperatingSystem.IsMacOS() || OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst() || OperatingSystem.IsTvOS())
                Assert.Skip("This host is an Apple platform.");

            Assert.False(HvfRenderer.IsAvailable);
            Assert.Null(HvfRenderer.TryCreate(new byte[64], default));
        }
    }
}
