using PeachDrawing.Text.Internal.Hinting;
using PeachDrawing.Text.Internal.Hinting.FreeType;
using PeachDrawing.Text.Outlines;
using PeachPDF.Tests.TestSupport;
using System.Buffers.Binary;
using System.Diagnostics;

namespace PeachDrawing.Text.Tests.Hinting
{
    /// <summary>
    /// Fonts are untrusted input, and a charstring is a program too. Whatever the CFF table says, hinting has to end in bounded time and
    /// answer with a controlled failure (which the public API turns into the unhinted outline), never with an unexpected exception or a hang.
    /// </summary>
    public class HintingCffRobustnessTests
    {
        private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, name));

        private static OutlineRequest Request(double ppem) => new() { PixelsPerEm = ppem, GridFitting = GridFitting.Standard };

        [Fact]
        public void EveryHostileGlyphIsAnsweredInBoundedTimeWithoutAnUnexpectedException()
        {
            var font = TypefaceFixtures.FromBytes(Fixture("HintingCffHostile.otf"));
            long before = HintingEngine.UnexpectedFailures;

            var watch = Stopwatch.StartNew();
            int refused = 0, fitted = 0;
            for (ushort g = 0; g < 18; g++)
            {
                if (font.TryGetOutline(g, Request(16), out var outline) && outline.IsGridFitted)
                    fitted++;
                else
                    refused++;
            }

            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), $"took {watch.Elapsed.TotalSeconds:F1} s");
            Assert.True(refused > 5, $"{refused} glyphs fell back");
            Assert.True(fitted > 3, $"{fitted} glyphs were fitted");
            Assert.Equal(before, HintingEngine.UnexpectedFailures);
        }

        [Fact]
        public void ACallTreeOfThirtyToTheTwelfthCallsIsStoppedByTheInstructionLimit()
        {
            // glyph 4 of the hostile font: 30^12 subroutine calls; the engine gives up after 20 million instructions, as FreeType does
            var face = HintingCffFixtures.Face("HintingCffHostile.otf");
            var size = new CffSize(face, 16 * 64);

            var watch = Stopwatch.StartNew();
            Assert.Throws<HintingException>(() => CffGlyphLoader.Load(size, 4));
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15), $"took {watch.Elapsed.TotalSeconds:F1} s");
        }

        [Fact]
        public void ScrambledCffTablesNeverThrowAnythingButAreFittedOrFallBack()
        {
            var random = new Random(4321);
            long unexpectedBefore = HintingEngine.UnexpectedFailures;
            int fitted = 0, fallbacks = 0;

            var watch = Stopwatch.StartNew();
            foreach (var file in new[] { "HintingCff.otf", "HintingCffCid.otf" })
            {
                var original = Fixture(file);
                for (int iteration = 0; iteration < 120; iteration++)
                {
                    var font = (byte[])original.Clone();
                    HostileFonts.Scramble(font, "CFF ", random, 1 + random.Next(80));

                    Typeface typeface;
                    try
                    {
                        typeface = TypefaceFixtures.FromBytes(font);
                    }
                    catch (TypefaceFormatException)
                    {
                        continue;
                    }

                    foreach (double ppem in new[] { 9.0, 12.5, 40 })
                    {
                        for (int k = 0; k < 6; k++)
                        {
                            var glyph = (ushort)(1 + random.Next(140));
                            bool found = typeface.TryGetOutline(glyph, Request(ppem), out var outline);
                            if (found && outline.IsGridFitted)
                                fitted++;
                            else
                                fallbacks++;
                        }
                    }
                }
            }

            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(120), $"took {watch.Elapsed.TotalSeconds:F1} s");
            Assert.True(fitted > 200, $"only {fitted} fitted outlines");
            Assert.True(fallbacks > 50, $"only {fallbacks} fallbacks");
            Assert.Equal(unexpectedBefore, HintingEngine.UnexpectedFailures);
        }

        [Fact]
        public void ATruncatedCffTableIsNeverAnUnexpectedFailure()
        {
            var original = Fixture("HintingCff.otf");
            var cff = HostileFonts.TableBytes(original, "CFF ");
            long unexpectedBefore = HintingEngine.UnexpectedFailures;

            foreach (int length in new[] { 0, 3, 4, 10, 100, 1000, 5000, cff.Length / 2, cff.Length - 1 })
            {
                var font = HostileFonts.WithTable(original, "CFF ", cff[..length]);

                Typeface typeface;
                try
                {
                    typeface = TypefaceFixtures.FromBytes(font);
                }
                catch (TypefaceFormatException)
                {
                    continue;
                }

                for (ushort g = 1; g < 30; g++)
                    typeface.TryGetOutline(g, Request(12), out _);
            }

            Assert.Equal(unexpectedBefore, HintingEngine.UnexpectedFailures);
        }

        [Fact]
        public void TheOffsetsOfTheIndexesAreBoundsChecked()
        {
            // every two-byte value of the header area (the counts and offsets of the INDEXes) set to 0xFFFF in turn
            var original = Fixture("HintingCff.otf");
            var cff = HostileFonts.TableBytes(original, "CFF ");
            long unexpectedBefore = HintingEngine.UnexpectedFailures;

            for (int at = 0; at < 90; at += 2)
            {
                var broken = (byte[])cff.Clone();
                BinaryPrimitives.WriteUInt16BigEndian(broken.AsSpan(at), 0xFFFF);

                var font = HostileFonts.WithTable(original, "CFF ", broken);
                Typeface typeface;
                try
                {
                    typeface = TypefaceFixtures.FromBytes(font);
                }
                catch (TypefaceFormatException)
                {
                    continue;
                }

                for (ushort g = 1; g < 20; g++)
                    typeface.TryGetOutline(g, Request(14), out _);
            }

            Assert.Equal(unexpectedBefore, HintingEngine.UnexpectedFailures);
        }

        [Fact]
        public void ALargeCffFontKeepsItsCachesBounded()
        {
            // many more sizes than the cache holds: each is remembered or evicted, and the answers do not change
            var font = TypefaceFixtures.FromBytes(Fixture("SourceCodePro-Regular.otf"));
            Assert.True(font.TryMapRune(new System.Text.Rune('B'), out var glyph));
            Assert.True(font.TryGetOutline(glyph, Request(10), out var before));

            for (int size = 11; size < 60; size++)
                for (ushort g = 1; g < 80; g++)
                    font.TryGetOutline(g, Request(size), out _);

            Assert.True(font.TryGetOutline(glyph, Request(10), out var after));
            Assert.Equal(before.Contours.Count, after.Contours.Count);
            for (int c = 0; c < before.Contours.Count; c++)
                Assert.Equal(before.Contours[c].Segments.Select(s => s.End), after.Contours[c].Segments.Select(s => s.End));
        }
    }
}
