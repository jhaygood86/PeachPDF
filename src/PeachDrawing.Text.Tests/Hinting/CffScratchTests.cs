using PeachDrawing.Text.Internal.Hinting;
using PeachDrawing.Text.Internal.Hinting.FreeType;
using PeachDrawing.Text.Outlines;
using PeachPDF.Tests.TestSupport;

namespace PeachDrawing.Text.Tests.Hinting
{
    /// <summary>
    /// Adobe's CFF engine works in objects that its thread keeps from glyph to glyph (hint maps, masks, the interpreter's stacks, the outline it
    /// builds), and the engine makes an outline from the thread's own arrays: what is loaded must not depend on what was loaded before, and what a
    /// font asks for must not be kept.
    /// </summary>
    public class CffScratchTests
    {
        private sealed record Loaded(int[] X, int[] Y, byte[] Tags, int[] Ends, int Advance, string? Error);

        private static Loaded Load(CffSize size, int glyph)
        {
            try
            {
                var g = CffGlyphLoader.Load(size, glyph);
                return new Loaded(g.X, g.Y, g.Tags, g.ContourEnds, g.Advance, null);
            }
            catch (HintingException ex)
            {
                return new Loaded([], [], [], [], 0, ex.Message);
            }
        }

        private static bool Same(Loaded a, Loaded b) =>
            a.Error == b.Error && a.Advance == b.Advance && a.X.AsSpan().SequenceEqual(b.X) && a.Y.AsSpan().SequenceEqual(b.Y) &&
            a.Tags.AsSpan().SequenceEqual(b.Tags) && a.Ends.AsSpan().SequenceEqual(b.Ends);

        [Theory]
        [InlineData("HintingCff.otf", false)]
        [InlineData("HintingCff.otf", true)]
        [InlineData("HintingCffCid.otf", false)]
        [InlineData("HintingCffIdeo.otf", false)]
        [InlineData("HintingCffMatrix.otf", false)]
        [InlineData("SourceCodePro-Regular.otf", true)]
        public void GlyphsLoadedInAnyOrderAreTheSameWhateverTheThreadsPoolsHeldBefore(string file, bool darkened)
        {
            // each glyph on a thread that has loaded nothing (so nothing is in its pools), against the same glyphs through one thread's pools in random order
            var face = HintingCffFixtures.Face(file);
            var size = new CffSize(face, 16 * 64, darkened);
            int count = Math.Min(face.Font.NumGlyphs, 250);

            var expected = new Loaded[count];
            for (int glyph = 0; glyph < count; glyph++)
            {
                int g = glyph;
                var thread = new Thread(() => expected[g] = Load(size, g));
                thread.Start();
                thread.Join();
            }

            int refused = expected.Count(e => e.Error is not null);
            int loaded = expected.Length - refused;
            Assert.True(loaded > count / 4, $"{file}: only {loaded} of {count} glyphs load");

            var random = new Random(11);
            for (int i = 0; i < 3000; i++)
            {
                int glyph = random.Next(count);
                Assert.True(Same(expected[glyph], Load(size, glyph)), $"{file} glyph {glyph} differs after other glyphs were loaded");
            }
        }

        [Fact]
        public void ManyThreadsLoadCffGlyphsThroughPoolsOfTheirOwn()
        {
            var face = HintingCffFixtures.Face("HintingCff.otf");
            var size = new CffSize(face, 13 * 64);
            var expected = Enumerable.Range(0, face.Font.NumGlyphs).Select(g => Load(size, g)).ToArray();

            var errors = new List<string>();
            var workers = Enumerable.Range(0, 8).Select(t => new Thread(() =>
            {
                var random = new Random(t);
                for (int i = 0; i < 1500; i++)
                {
                    int glyph = random.Next(expected.Length);
                    if (!Same(expected[glyph], Load(size, glyph)))
                        lock (errors) errors.Add($"glyph {glyph}");
                }
            })).ToList();

            workers.ForEach(w => w.Start());
            workers.ForEach(w => w.Join());
            Assert.Empty(errors.Take(10));
        }

        [Fact]
        public void WhatAHostileCffFontAsksForIsNotKeptByTheThread()
        {
            // the hostile font has a glyph of 40,000 points and others with thousands of stem hints: the thread's pools are small once they are loaded
            long retained = -1;
            long duringLargest = 0;
            var thread = new Thread(() =>
            {
                var face = HintingCffFixtures.Face("HintingCffHostile.otf");
                var size = new CffSize(face, 16 * 64);
                for (int glyph = 0; glyph < face.Font.NumGlyphs; glyph++)
                {
                    try
                    {
                        var g = CffGlyphLoader.Load(size, glyph);
                        duringLargest = Math.Max(duringLargest, g.NPoints);
                    }
                    catch (HintingException)
                    {
                    }
                }

                retained = Cf2Pool.RetainedElements;
            });
            thread.Start();
            thread.Join();

            Assert.True(duringLargest > 4096, $"the largest glyph loaded has {duringLargest} points: not a test of dropping big arrays");
            Assert.InRange(retained, 0, 20_000);
        }

        [Theory]
        [InlineData("HintingCff.otf")]
        [InlineData("SourceCodePro-Regular.otf")]
        public void TheSegmentListsOfACffOutlineAreMadeAsBigAsTheyAreNeeded(string file)
        {
            var typeface = TypefaceFixtures.FromFile(Path.Combine(AppContext.BaseDirectory, file));
            var request = new OutlineRequest { PixelsPerEm = 20, GridFitting = GridFitting.Standard };
            int contours = 0;

            for (ushort glyph = 0; glyph < 200; glyph++)
            {
                if (!typeface.TryGetOutline(glyph, request, out var outline) || !outline.IsGridFitted)
                    continue;

                Assert.Equal(outline.ContourList.Count, outline.ContourList.Capacity);
                foreach (var contour in outline.ContourList)
                {
                    Assert.Equal(contour.SegmentList.Count, contour.SegmentList.Capacity);
                    contours++;
                }
            }

            Assert.True(contours > 100, $"only {contours} contours were looked at");
        }
    }
}
