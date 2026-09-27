using PeachDrawing.Text.Internal.Fonts.OpenType;
using PeachDrawing.Text.Internal.Hinting;
using PeachDrawing.Text.Internal.Hinting.FreeType;
using PeachDrawing.Text.Outlines;
using PeachPDF.Tests.TestSupport;
using System.Buffers.Binary;

namespace PeachDrawing.Text.Tests.Hinting
{
    /// <summary>
    /// The TrueType glyph loader works in scratch arrays that its thread keeps from glyph to glyph, and the engine makes an outline straight from them:
    /// the outlines must be the ones the copying loader (which the FreeType goldens are compared against) gives, and what a font asks for must not be kept.
    /// </summary>
    public class TrueTypeLoaderScratchTests
    {
        [Theory]
        [InlineData("LiberationSans-Regular.woff")]
        [InlineData("LiberationSerif-Regular.woff")]
        [InlineData("SourceSans3-Regular.ttf")]
        [InlineData("HintingOpcodes.ttf")]
        public void TheOutlinesOfTheEngineAreTheOnesMadeFromTheCopyOfTheLoader(string file)
        {
            // TtGlyphLoader.Load(size, glyph) is what HintingGoldenTests compares with FreeType, point for point; the outline the engine makes from the
            // loader's own arrays must be what a plain conversion of that copy gives (the conversion here is written out again, from the rules of BuildContour)
            var face = HintingFixtures.Face(file);
            var typeface = TypefaceFixtures.FromFile(Path.Combine(AppContext.BaseDirectory, file));
            int compared = 0;

            foreach (int ppem in new[] { 12, 16, 40 })
            {
                var size = TtSize.Create(face, ppem * 64, TtInterpreterVersion.V40, TtRenderMode.Normal);
                var request = new OutlineRequest { PixelsPerEm = ppem, GridFitting = GridFitting.Standard };

                for (int glyph = 0; glyph < face.NumGlyphs; glyph++)
                {
                    TtHintedGlyph hinted;
                    try
                    {
                        hinted = TtGlyphLoader.Load(size, glyph);
                    }
                    catch (HintingException)
                    {
                        continue;
                    }

                    var expected = ReferenceOutline(hinted);
                    bool found = typeface.TryGetOutline((ushort)glyph, request, out var actual);
                    Assert.Equal(expected.Contours.Count > 0, found);
                    if (!found)
                        continue;

                    Assert.Equal(hinted.IsHinted, actual.IsGridFitted);
                    Assert.Equal(hinted.IsHinted ? hinted.Advance / 64.0 : (double?)null, actual.GridFittedAdvance);
                    AssertSameContours(expected, actual, $"{file} glyph {glyph} at {ppem} ppem");
                    compared++;
                }
            }

            Assert.True(compared > 300, $"only {compared} glyphs were compared");
        }

        [Fact]
        public void BuildContourGivesWhatWalkingAllTheContoursPointsGives()
        {
            // random contours, all sorts: no on-curve point at all, one, runs of off-curve points, the first point off-curve, single points
            var random = new Random(42);
            for (int round = 0; round < 3000; round++)
            {
                int n = 1 + random.Next(random.Next(4) == 0 ? 40 : 8);
                double onProbability = random.Next(4) switch { 0 => 0.0, 1 => 0.2, 2 => 0.5, _ => 0.9 };
                var points = new List<GlyphOutlineDecoder.RawPoint>();
                for (int i = 0; i < n; i++)
                    points.Add(new GlyphOutlineDecoder.RawPoint(random.Next(-2000, 2000) / 64.0, random.Next(-2000, 2000) / 64.0, random.NextDouble() < onProbability));

                var expected = ReferenceBuildContour(points)!;
                var actual = GlyphOutlineDecoder.BuildContour(points)!;

                Assert.NotNull(actual);
                Assert.Equal(expected.Start, actual.Start);
                Assert.Equal(expected.SegmentList, actual.SegmentList);
            }

            Assert.Null(GlyphOutlineDecoder.BuildContour([]));
        }

        [Fact]
        public void TheSegmentsOfAContourAreCountedRightBeforeTheyAreMade()
        {
            // the list of a contour's segments is made as big as it will be: not smaller (it would grow) and not bigger (it would waste)
            var random = new Random(7);
            for (int round = 0; round < 1000; round++)
            {
                int n = 1 + random.Next(30);
                var points = Enumerable.Range(0, n)
                    .Select(_ => new GlyphOutlineDecoder.RawPoint(random.Next(100), random.Next(100), random.Next(3) == 0))
                    .ToList();

                var contour = GlyphOutlineDecoder.BuildContour(points)!;
                Assert.Equal(contour.SegmentList.Count, contour.SegmentList.Capacity);
            }
        }

        [Fact]
        public void GlyphsLoadedInAnyOrderAreTheSameWhateverTheScratchHeldBefore()
        {
            // composites and simple glyphs, big and small, one after another through the same scratch, against each glyph loaded on a thread that has loaded nothing else
            var face = HintingFixtures.Face("LiberationSans-Regular.woff");
            var size = TtSize.Create(face, 16 * 64, TtInterpreterVersion.V40, TtRenderMode.Normal);
            var expected = new Dictionary<int, TtHintedGlyph>();
            for (int glyph = 0; glyph < face.NumGlyphs; glyph++)
            {
                TtHintedGlyph? alone = null;
                var thread = new Thread(() =>
                {
                    try
                    {
                        alone = TtGlyphLoader.Load(size, glyph);
                    }
                    catch (HintingException)
                    {
                    }
                });
                thread.Start();
                thread.Join();
                if (alone is not null)
                    expected[glyph] = alone;
            }

            var random = new Random(3);
            for (int i = 0; i < 4000; i++)
            {
                int glyph = random.Next(face.NumGlyphs);
                if (!expected.TryGetValue(glyph, out var want))
                {
                    Assert.ThrowsAny<HintingException>(() => TtGlyphLoader.Load(size, glyph));
                    continue;
                }

                var got = TtGlyphLoader.Load(size, glyph);
                Assert.True(want.X.AsSpan().SequenceEqual(got.X) && want.Y.AsSpan().SequenceEqual(got.Y) && want.Tags.AsSpan().SequenceEqual(got.Tags) &&
                    want.ContourEnds.AsSpan().SequenceEqual(got.ContourEnds) && want.Advance == got.Advance, $"glyph {glyph} differs");
            }
        }

        [Fact]
        public void WhatAHostileFontAsksForIsNotKeptByTheThread()
        {
            // a font that declares 60,000 twilight points and stack elements: a glyph of it is loaded (or refused), and the thread's scratch is not that big afterwards
            var original = HostileFonts.Original();
            var maxp = HostileFonts.TableBytes(original, "maxp");
            BinaryPrimitives.WriteUInt16BigEndian(maxp.AsSpan(16), 60000); // maxTwilightPoints
            BinaryPrimitives.WriteUInt16BigEndian(maxp.AsSpan(24), 60000); // maxStackElements
            var font = HostileFonts.WithTable(original, "maxp", maxp);

            long retained = -1;
            var thread = new Thread(() =>
            {
                var typeface = TypefaceFixtures.FromBytes(font);
                var face = TtFace.TryCreate(typeface.Face.Fontface, typeface.Face.FamilyName, null)!;
                try
                {
                    var size = TtSize.Create(face, 16 * 64, TtInterpreterVersion.V40, TtRenderMode.Normal);
                    for (int glyph = 1; glyph < 60; glyph++)
                        TtGlyphLoader.Load(size, glyph);
                }
                catch (HintingException)
                {
                }

                var exec = TtExecContext.Rent();
                retained = exec.RetainedElements;
                TtExecContext.Return(exec);
            });
            thread.Start();
            thread.Join();

            Assert.InRange(retained, 0, 100_000); // 60,000 twilight points alone are 60,000 elements in each of nine arrays
        }

        [Fact]
        public void TheComponentOffsetsOfACompositeAreMovedByTheVariationDeltasOfAnInstance()
        {
            // Aacute of the variable test font is a composite of A and an accent whose offset changes with the weight (gvar deltas on the offsets); the
            // font has a CVT program that does nothing, so the fitted outline is the scaled design outline of the instance, to within the rounding of the loader
            var font = TypefaceFixtures.FromBytes(HostileFonts.WithTable(
                File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "VariableTest.ttf")), "prep", [0xB0, 0x00, 0x21]));
            var bold = font.WithAxes([new AxisSetting(AxisTags.Weight, 800)]);
            Assert.True(bold.TryMapRune(new System.Text.Rune('Á'), out var glyph));

            Assert.True(bold.TryGetOutline(glyph, out var design));
            Assert.True(bold.TryGetOutline(glyph, new OutlineRequest { PixelsPerEm = 30, GridFitting = GridFitting.Standard }, out var fitted));
            Assert.True(font.TryGetOutline(glyph, new OutlineRequest { PixelsPerEm = 30, GridFitting = GridFitting.Standard }, out var regular));
            Assert.True(fitted.IsGridFitted);

            double scale = 30.0 / bold.Metrics.UnitsPerEm;
            var expected = PointsOf(design).Select(p => (X: p.X * scale, Y: p.Y * scale)).ToList();
            var actual = PointsOf(fitted);
            Assert.Equal(expected.Count, actual.Count);
            for (int i = 0; i < expected.Count; i++)
            {
                // (the accent's offset is rounded to a whole pixel, as the component flags of a hinted composite ask: up to half a pixel)
                Assert.InRange(actual[i].X, expected[i].X - 0.5, expected[i].X + 0.5);
                Assert.InRange(actual[i].Y, expected[i].Y - 0.5, expected[i].Y + 0.5);
            }

            Assert.NotEqual(PointsOf(regular), actual); // the instance is not the default one: the accent moved
        }

        private static List<(double X, double Y)> PointsOf(GlyphOutline outline)
        {
            var points = new List<(double, double)>();
            foreach (var contour in outline.Contours)
            {
                points.Add((contour.Start.X, contour.Start.Y));
                foreach (var segment in contour.Segments)
                {
                    if (segment.IsCubic)
                    {
                        points.Add((segment.Control1.X, segment.Control1.Y));
                        points.Add((segment.Control2.X, segment.Control2.Y));
                    }

                    points.Add((segment.End.X, segment.End.Y));
                }
            }

            return points;
        }

        [Fact]
        public void ManyThreadsLoadGlyphsThroughScratchOfTheirOwn()
        {
            var face = HintingFixtures.Face("LiberationSerif-Regular.woff");
            var size = TtSize.Create(face, 13 * 64, TtInterpreterVersion.V40, TtRenderMode.Normal);
            var expected = new TtHintedGlyph?[face.NumGlyphs];
            for (int glyph = 0; glyph < expected.Length; glyph++)
            {
                try
                {
                    expected[glyph] = TtGlyphLoader.Load(size, glyph);
                }
                catch (HintingException)
                {
                }
            }

            var errors = new List<string>();
            var workers = Enumerable.Range(0, 8).Select(t => new Thread(() =>
            {
                var random = new Random(t);
                for (int i = 0; i < 1500; i++)
                {
                    int glyph = random.Next(expected.Length);
                    TtHintedGlyph? got = null;
                    try
                    {
                        got = TtGlyphLoader.Load(size, glyph);
                    }
                    catch (HintingException)
                    {
                    }

                    var want = expected[glyph];
                    bool same = (want is null && got is null) ||
                        (want is not null && got is not null && want.X.AsSpan().SequenceEqual(got.X) && want.Y.AsSpan().SequenceEqual(got.Y) && want.Advance == got.Advance);
                    if (!same)
                        lock (errors) errors.Add($"glyph {glyph}");
                }
            })).ToList();

            workers.ForEach(w => w.Start());
            workers.ForEach(w => w.Join());
            Assert.Empty(errors.Take(10));
        }

        // ---- the reference conversion: the copy of a hinted glyph to an outline, and BuildContour as it was written before it walked the points in place

        private static GlyphOutline ReferenceOutline(TtHintedGlyph hinted)
        {
            var outline = new GlyphOutline { IsGridFitted = hinted.IsHinted };
            int start = 0;
            foreach (int end in hinted.ContourEnds)
            {
                var points = new List<GlyphOutlineDecoder.RawPoint>();
                for (int i = start; i <= end && i < hinted.NPoints; i++)
                    points.Add(new GlyphOutlineDecoder.RawPoint(hinted.X[i] / 64.0, hinted.Y[i] / 64.0, (hinted.Tags[i] & FtTag.On) != 0));

                var contour = ReferenceBuildContour(points);
                if (contour is not null)
                    outline.ContourList.Add(contour);

                start = end + 1;
            }

            return outline;
        }

        private static void AssertSameContours(GlyphOutline expected, GlyphOutline actual, string what)
        {
            Assert.True(expected.ContourList.Count == actual.ContourList.Count, $"{what}: contour count");
            for (int c = 0; c < expected.ContourList.Count; c++)
            {
                var e = expected.ContourList[c];
                var a = actual.ContourList[c];
                Assert.True(e.Start.Equals(a.Start), $"{what}: start of contour {c}");
                Assert.True(e.SegmentList.SequenceEqual(a.SegmentList), $"{what}: segments of contour {c}");
            }
        }

        private static OutlineContour? ReferenceBuildContour(List<GlyphOutlineDecoder.RawPoint> points)
        {
            int n = points.Count;
            if (n == 0)
                return null;

            int firstOn = -1;
            for (int i = 0; i < n; i++)
            {
                if (points[i].OnCurve)
                {
                    firstOn = i;
                    break;
                }
            }

            OutlinePoint startPoint;
            var sequence = new List<GlyphOutlineDecoder.RawPoint>(n + 1);
            if (firstOn < 0)
            {
                startPoint = Midpoint(points[0], points[n - 1]);
                for (int i = 0; i < n; i++)
                    sequence.Add(points[i]);
                sequence.Add(new GlyphOutlineDecoder.RawPoint(startPoint.X, startPoint.Y, true));
            }
            else
            {
                startPoint = new OutlinePoint(points[firstOn].X, points[firstOn].Y);
                for (int i = 1; i <= n; i++)
                    sequence.Add(points[(firstOn + i) % n]);
            }

            var contour = new OutlineContour(startPoint);
            OutlinePoint current = startPoint;
            GlyphOutlineDecoder.RawPoint? pendingControl = null;

            foreach (var p in sequence)
            {
                if (p.OnCurve)
                {
                    var end = new OutlinePoint(p.X, p.Y);
                    if (pendingControl is { } ctrl)
                    {
                        contour.SegmentList.Add(QuadraticToCubic(current, ctrl, end));
                        pendingControl = null;
                    }
                    else
                    {
                        contour.SegmentList.Add(OutlineSegment.Line(end));
                    }

                    current = end;
                }
                else if (pendingControl is { } ctrl)
                {
                    OutlinePoint mid = Midpoint(ctrl, p);
                    contour.SegmentList.Add(QuadraticToCubic(current, ctrl, mid));
                    current = mid;
                    pendingControl = p;
                }
                else
                {
                    pendingControl = p;
                }
            }

            return contour;
        }

        private static OutlineSegment QuadraticToCubic(OutlinePoint start, GlyphOutlineDecoder.RawPoint control, OutlinePoint end)
        {
            var c1 = new OutlinePoint(start.X + 2.0 / 3.0 * (control.X - start.X), start.Y + 2.0 / 3.0 * (control.Y - start.Y));
            var c2 = new OutlinePoint(end.X + 2.0 / 3.0 * (control.X - end.X), end.Y + 2.0 / 3.0 * (control.Y - end.Y));
            return OutlineSegment.Cubic(c1, c2, end);
        }

        private static OutlinePoint Midpoint(GlyphOutlineDecoder.RawPoint a, GlyphOutlineDecoder.RawPoint b) => new((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0);
    }
}
