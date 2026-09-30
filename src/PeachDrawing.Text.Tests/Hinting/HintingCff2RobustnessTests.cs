using PeachDrawing.Text.Internal.Fonts;
using PeachDrawing.Text.Internal.Fonts.OpenType;
using PeachDrawing.Text.Internal.Fonts.OpenType.Variations;
using PeachDrawing.Text.Internal.Hinting;
using PeachDrawing.Text.Internal.Hinting.FreeType;
using PeachDrawing.Text.Outlines;

namespace PeachDrawing.Text.Tests.Hinting
{
    /// <summary>
    /// A CFF2 table is untrusted input, and its charstrings and Private DICTs are programs and data with sizes of their own. Whatever the
    /// table says, hinting has to end in bounded time and space with a controlled failure (which the public API turns into the unhinted
    /// outline), never with an unexpected exception or a hang.
    /// </summary>
    public class HintingCff2RobustnessTests
    {
        private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, name));

        /// <summary>Hints the first glyphs of a font at two sizes and, when it has axes, at two locations; false when the font is not one this engine hints.</summary>
        private static bool Hint(byte[] font, int glyphs, Random? random = null)
        {
            OpenTypeFontface fontface;
            try
            {
                fontface = new OpenTypeFontface(FontFileData.CreateCompiledFont(font));
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or ArgumentException)
            {
                return false;
            }

            int Advance(int glyph) => 500;

            var locations = new List<VariationCoordinates?> { null };
            if (fontface.Variations is { } variations && random is not null)
            {
                locations.Add(variations.CreateCoordinates(variations.Axes.Select(a => (a.Tag, a.Minimum + random.NextDouble() * (a.Maximum - a.Minimum)))));
                locations.Add(variations.CreateCoordinates(variations.Axes.Select(a => (a.Tag, a.Maximum))));
            }

            bool hinted = false;
            foreach (var location in locations)
            {
                var engine = new HintingEngine(fontface, null, location, Advance);
                if (!engine.CanHint)
                    continue;

                for (int glyph = 0; glyph < glyphs; glyph++)
                {
                    foreach (int ppem in new[] { 9 * 64, 30 * 64 + 21 })
                        hinted |= engine.Get(glyph, ppem, GridFitting.Standard).Succeeded;

                    hinted |= engine.Get(glyph, 12 * 64, GridFitting.Standard, stemDarkening: true).Succeeded;
                }
            }

            return hinted;
        }

        [Fact]
        public void EveryByteOfATableChangedIsAnsweredWithoutAnUnexpectedException()
        {
            // every byte of the CFF2 table of a small font, flipped with three masks in turn
            var original = Fixture("HintingCff2NoAxes.otf");
            int offset = HintingCff2VariantTests.TableOffset(original, "CFF2");
            var table = HostileFonts.TableBytes(original, "CFF2");
            long unexpectedBefore = HintingEngine.UnexpectedFailures;

            // (the bound is on each case, not on the sweep: see WorkBounds)
            int hinted = 0;
            foreach (byte mask in new byte[] { 0xFF, 0x80, 0x01 })
            {
                for (int at = 0; at < table.Length; at++)
                {
                    var font = (byte[])original.Clone();
                    font[offset + at] ^= mask;
                    if (WorkBounds.Case(() => Hint(font, 4), $"byte {at} ^ {mask:X2}"))
                        hinted++;
                }
            }

            Assert.True(hinted > table.Length, $"only {hinted} of the damaged fonts hinted at all");
            Assert.Equal(unexpectedBefore, HintingEngine.UnexpectedFailures);
        }

        [Fact]
        public void RandomDamageToAVariableFontIsAnsweredWithoutAnUnexpectedException()
        {
            var random = new Random(20260926);
            long unexpectedBefore = HintingEngine.UnexpectedFailures;
            int hinted = 0, unhinted = 0;

            foreach (var file in new[] { "HintingCff2.otf", "HintingCff2Single.otf" })
            {
                var original = Fixture(file);
                for (int iteration = 0; iteration < 700; iteration++)
                {
                    var font = (byte[])original.Clone();
                    HostileFonts.Scramble(font, "CFF2", random, 1 + random.Next(60));

                    if (WorkBounds.Case(() => Hint(font, 6, random), $"{file} damage {iteration}"))
                        hinted++;
                    else
                        unhinted++;
                }
            }

            Assert.True(hinted > 100, $"only {hinted} damaged fonts hinted");
            Assert.True(unhinted > 100, $"only {unhinted} damaged fonts were refused");
            Assert.Equal(unexpectedBefore, HintingEngine.UnexpectedFailures);
        }

        [Fact]
        public void ATruncatedTableIsNeverAnUnexpectedFailure()
        {
            var original = Fixture("HintingCff2Single.otf");
            var table = HostileFonts.TableBytes(original, "CFF2");
            long unexpectedBefore = HintingEngine.UnexpectedFailures;

            for (int length = 0; length < table.Length; length += length < 400 ? 1 : 37)
                Hint(HostileFonts.WithTable(original, "CFF2", table[..length]), 3);

            Assert.Equal(unexpectedBefore, HintingEngine.UnexpectedFailures);
        }

        [Fact]
        public void EveryVariantIsReadInBoundedTimeAndSpace()
        {
            // the faults include stores that name 65,535 data sets, regions or axes, 257 Font DICTs, and offsets and counts of every size
            var golden = HintingGoldenData.Load<Cff2VariantFile>("HintingCff2Variants.golden.json.gz");
            long unexpectedBefore = HintingEngine.UnexpectedFailures;

            foreach (var variant in golden.Variants.Concat(golden.Cases))
            {
                var font = Convert.FromBase64String(variant.Font);
                long before = GC.GetAllocatedBytesForCurrentThread();

                WorkBounds.Case(() => Hint(font, 40, new Random(1)), variant.Name);

                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.True(allocated < 64L * 1024 * 1024, $"{variant.Name}: allocated {allocated / 1024 / 1024} MB");
            }

            Assert.Equal(unexpectedBefore, HintingEngine.UnexpectedFailures);
        }

        [Fact]
        public void TheCachesOfTwoLocationsOfAFontAreSeparate()
        {
            // one glyph at one size, asked for at two locations in turn, several times: each location keeps its own outline, and they differ
            var typeface = HintingCff2Fixtures.Default("HintingCff2.otf");
            var narrow = typeface.WithAxes([new AxisSetting("wght", 200)]);
            var wide = typeface.WithAxes([new AxisSetting("wght", 800)]);
            var request = new OutlineRequest { PixelsPerEm = 16, GridFitting = GridFitting.Standard };

            int different = 0;
            for (ushort glyph = 1; glyph < 30; glyph++)
            {
                bool a = narrow.TryGetOutline(glyph, request, out var first);
                bool b = wide.TryGetOutline(glyph, request, out var second);
                bool c = narrow.TryGetOutline(glyph, request, out var third);
                bool d = wide.TryGetOutline(glyph, request, out var fourth);

                Assert.Equal((a, b), (c, d));
                if (!a || !b || !first.IsGridFitted)
                    continue;

                Assert.Same(first, third);
                Assert.Same(second, fourth);
                if (!first.Contours.SequenceEqual(second.Contours, ContourComparer.Instance))
                    different++;
            }

            Assert.True(different > 10, $"only {different} glyphs differ between the two locations");
        }

        private sealed class ContourComparer : IEqualityComparer<OutlineContour>
        {
            public static readonly ContourComparer Instance = new();

            public bool Equals(OutlineContour? x, OutlineContour? y) =>
                x is not null && y is not null && x.Start == y.Start && x.Segments.Select(s => s.End).SequenceEqual(y.Segments.Select(s => s.End));

            public int GetHashCode(OutlineContour contour) => contour.Start.GetHashCode();
        }

        [Fact]
        public void AVariableCff2FontCanBeHinted()
        {
            var typeface = HintingCff2Fixtures.Default("VariableCff2Test.otf");
            Assert.True(typeface.Face.Descriptor.Hinting.CanHint);

            var bold = typeface.WithAxes([new AxisSetting("wght", 900)]);
            Assert.True(bold.Face.Descriptor.Hinting.CanHint);
            Assert.True(bold.TryMapRune(new System.Text.Rune('A'), out var glyph));
            Assert.True(bold.TryGetOutline(glyph, new OutlineRequest { PixelsPerEm = 20, GridFitting = GridFitting.Standard }, out var outline));
            Assert.True(outline.IsGridFitted);
            Assert.True(bold.TryGetGridFittedAdvance(glyph, new OutlineRequest { PixelsPerEm = 20, GridFitting = GridFitting.Standard }, out var advance));
            Assert.Equal(Math.Round(advance), advance);
        }
    }
}
