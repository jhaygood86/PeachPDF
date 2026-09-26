using PeachDrawing.Text.Internal.Hinting;
using PeachDrawing.Text.Internal.Hinting.FreeType;
using PeachDrawing.Text.Outlines;
using System.Buffers.Binary;
using System.Diagnostics;

namespace PeachDrawing.Text.Tests.Hinting
{
    /// <summary>
    /// Fonts are untrusted input, and the bytecode of a font is a program: it can loop for ever, call itself, push without end, or be
    /// nonsense. Whatever it is, hinting has to end in bounded time and answer with a controlled failure (which the public API turns into the
    /// unhinted outline), never with an unexpected exception or a hang.
    /// </summary>
    public class HintingRobustnessTests
    {
        // One 12 ppem size, in 26.6.
        private const int Size = 12 * 64;

        private static TtFace FaceOf(byte[] data)
        {
            var typeface = PeachPDF.Tests.TestSupport.TypefaceFixtures.FromBytes(data);
            return TtFace.TryCreate(typeface.Face.Fontface, typeface.Face.FamilyName, null, null)
                ?? throw new InvalidOperationException("The font has no TrueType outlines.");
        }

        private static void AssertBounded(TimeSpan limit, Action action)
        {
            var watch = Stopwatch.StartNew();
            action();
            Assert.True(watch.Elapsed < limit, $"took {watch.Elapsed.TotalSeconds:F2} s, the limit is {limit.TotalSeconds:F0} s");
        }

        // fpgm/prep programs
        private static readonly byte[] JumpToTheStartForever = [.. HostileFonts.PushWord(-3), 0x1C]; // PUSHW -3, JMPR: back to the PUSHW

        [Fact]
        public void APrepProgramThatLoopsForEverFailsTheSizeAtOnce()
        {
            var font = HostileFonts.WithTable(HostileFonts.Original(), "prep", JumpToTheStartForever);
            var face = FaceOf(font);

            AssertBounded(TimeSpan.FromSeconds(5), () =>
                Assert.Throws<HintingException>(() => TtSize.Create(face, Size, TtInterpreterVersion.V40, TtRenderMode.Normal)));
        }

        [Fact]
        public void AFontProgramThatLoopsForEverFailsTheSizeAtOnce()
        {
            var font = HostileFonts.WithTable(HostileFonts.Original(), "fpgm", JumpToTheStartForever);
            var face = FaceOf(font);

            AssertBounded(TimeSpan.FromSeconds(5), () =>
                Assert.Throws<HintingException>(() => TtSize.Create(face, Size, TtInterpreterVersion.V40, TtRenderMode.Normal)));
        }

        [Fact]
        public void AFunctionThatCallsItselfRunsOutOfCallStackAndFailsTheSize()
        {
            // FDEF 0: CALL 0. ENDF. Then the CVT program calls it.
            byte[] fpgm = [.. HostileFonts.PushWord(0), 0x2C, .. HostileFonts.PushWord(0), 0x2B, 0x2D];
            byte[] prep = [.. HostileFonts.PushWord(0), 0x2B];
            var font = HostileFonts.WithTable(HostileFonts.WithTable(HostileFonts.Original(), "fpgm", fpgm), "prep", prep);
            var face = FaceOf(font);

            AssertBounded(TimeSpan.FromSeconds(5), () =>
                Assert.Throws<HintingException>(() => TtSize.Create(face, Size, TtInterpreterVersion.V40, TtRenderMode.Normal)));
        }

        [Fact]
        public void ALoopCallStormIsStoppedByTheLoopLimits()
        {
            // FDEF 0 does nothing; LOOPCALL it 32767 times, twice over a nested loop
            byte[] fpgm = [.. HostileFonts.PushWord(0), 0x2C, 0x2D];
            byte[] prep = [.. HostileFonts.PushWord(32767), .. HostileFonts.PushWord(0), 0x2A];
            var face = FaceOf(HostileFonts.WithTable(HostileFonts.WithTable(HostileFonts.Original(), "fpgm", fpgm), "prep", prep));

            AssertBounded(TimeSpan.FromSeconds(5), () =>
                Assert.Throws<HintingException>(() => TtSize.Create(face, Size, TtInterpreterVersion.V40, TtRenderMode.Normal)));
        }

        [Fact]
        public void APushThatOverflowsTheStackFailsTheSize()
        {
            // NPUSHW with 255 values, over and over, against a stack of a few hundred elements
            var prep = new List<byte>();
            for (int i = 0; i < 40; i++)
            {
                prep.AddRange([0x41, 255]);
                prep.AddRange(new byte[510]);
            }

            var face = FaceOf(HostileFonts.WithTable(HostileFonts.Original(), "prep", prep.ToArray()));
            AssertBounded(TimeSpan.FromSeconds(5), () =>
                Assert.Throws<HintingException>(() => TtSize.Create(face, Size, TtInterpreterVersion.V40, TtRenderMode.Normal)));
        }

        [Fact]
        public void AGlyphOfThirtyThousandPointsWithSixtyThousandIupsEndsQuicklyAndStillYieldsAnOutline()
        {
            // The interpreter's work is bounded by its instruction count and not by the size of what an instruction works on; a glyph with
            // every point in play and a program of one whole-outline instruction repeated would otherwise cost minutes.
            var original = HostileFonts.Original();
            var head = HostileFonts.TableBytes(original, "head");
            head[51] = 1; // long loca offsets: the glyph is larger than a short loca can address

            const int points = 30000;
            const int instructions = 60000;

            var glyph = new List<byte>();
            glyph.AddRange(Be16(1));                            // one contour
            glyph.AddRange([0, 0, 0, 0, 0, 0, 0, 0]);          // bounding box (not checked)
            glyph.AddRange(Be16(points - 1));                   // its last point
            glyph.AddRange(Be16(instructions));
            glyph.AddRange(Enumerable.Repeat((byte)0x30, instructions)); // IUP[y], 60000 times
            glyph.AddRange(Enumerable.Repeat((byte)0x01, points));       // every point on curve, x and y as 16-bit values
            for (int i = 0; i < points; i++)
                glyph.AddRange(Be16((short)(i % 200 - 100)));   // x deltas
            for (int i = 0; i < points; i++)
                glyph.AddRange(Be16((short)(i % 100 - 50)));    // y deltas
            while (glyph.Count % 4 != 0)
                glyph.Add(0);

            int numGlyphs = BinaryPrimitives.ReadUInt16BigEndian(HostileFonts.TableBytes(original, "maxp").AsSpan(4));
            var loca = new byte[(numGlyphs + 1) * 4];
            // glyph 0 is empty, glyph 1 is the big one (its data starts at offset 0 and ends where loca[2] says), every other glyph is empty
            for (int g = 0; g <= numGlyphs; g++)
                BinaryPrimitives.WriteUInt32BigEndian(loca.AsSpan(g * 4), g <= 1 ? 0u : (uint)glyph.Count);

            var font = HostileFonts.WithTable(HostileFonts.WithTable(HostileFonts.WithTable(original, "head", head), "loca", loca), "glyf", glyph.ToArray());
            var face = FaceOf(font);

            AssertBounded(TimeSpan.FromSeconds(10), () =>
            {
                var size = TtSize.Create(face, Size, TtInterpreterVersion.V35, TtRenderMode.Mono);
                var loaded = TtGlyphLoader.Load(size, 1);

                // the program was stopped by the limit, and what it had done stays
                Assert.Equal(points, loaded.NPoints);
                Assert.Equal(TtError.ExecutionTooLong, loaded.ProgramError);
            });
        }

        [Fact]
        public void ACompositeGlyphThatContainsItselfIsRejected()
        {
            // find a composite glyph of the fixture (numberOfContours < 0) and point its first component at itself
            var font = HostileFonts.Original();
            var (_, glyfOffset, _) = HostileFonts.Tables(font).Single(t => t.Tag == "glyf");
            var loca = HostileFonts.TableBytes(font, "loca");
            bool longLoca = BinaryPrimitives.ReadInt16BigEndian(HostileFonts.TableBytes(font, "head").AsSpan(50)) != 0;
            int numGlyphs = BinaryPrimitives.ReadUInt16BigEndian(HostileFonts.TableBytes(font, "maxp").AsSpan(4));

            int composite = -1;
            for (int g = 0; g < numGlyphs && composite < 0; g++)
            {
                int start = longLoca ? (int)BinaryPrimitives.ReadUInt32BigEndian(loca.AsSpan(g * 4)) : BinaryPrimitives.ReadUInt16BigEndian(loca.AsSpan(g * 2)) * 2;
                int end = longLoca ? (int)BinaryPrimitives.ReadUInt32BigEndian(loca.AsSpan(g * 4 + 4)) : BinaryPrimitives.ReadUInt16BigEndian(loca.AsSpan(g * 2 + 2)) * 2;
                if (end > start && BinaryPrimitives.ReadInt16BigEndian(font.AsSpan(glyfOffset + start)) < 0)
                {
                    composite = g;
                    BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(glyfOffset + start + 12), (ushort)g); // the first component's glyph index
                }
            }

            Assert.True(composite > 0);

            var face = FaceOf(font);
            var size = TtSize.Create(face, Size, TtInterpreterVersion.V40, TtRenderMode.Normal);
            AssertBounded(TimeSpan.FromSeconds(5), () => Assert.Throws<HintingException>(() => TtGlyphLoader.Load(size, composite)));
        }

        [Fact]
        public void ScrambledFontsNeverThrowAnythingButAreHintedOrFallBack()
        {
            var original = HostileFonts.Original();
            var random = new Random(1234);
            var tables = new[] { "fpgm", "prep", "cvt ", "glyf", "glyf", "glyf", "maxp" };
            int hinted = 0, unhinted = 0;
            long unexpectedBefore = HintingEngine.UnexpectedFailures;

            AssertBounded(TimeSpan.FromSeconds(120), () =>
            {
                for (int iteration = 0; iteration < 250; iteration++)
                {
                    var font = (byte[])original.Clone();
                    string table = tables[random.Next(tables.Length)];
                    if (table == "maxp")
                    {
                        // only the fields hinting reads: twilight points, storage, function and instruction definitions, stack
                        var (_, offset, _) = HostileFonts.Tables(font).Single(t => t.Tag == "maxp");
                        for (int i = 0; i < 4; i++)
                            BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(offset + 16 + 2 * random.Next(5)), (ushort)random.Next(0x10000));
                    }
                    else
                    {
                        HostileFonts.Scramble(font, table, random, 1 + random.Next(60));
                    }

                    Typeface typeface;
                    try
                    {
                        typeface = PeachPDF.Tests.TestSupport.TypefaceFixtures.FromBytes(font);
                    }
                    catch (TypefaceFormatException)
                    {
                        continue;
                    }

                    foreach (var mode in new[] { GridFitting.Standard, GridFitting.Monochrome })
                    {
                        foreach (double ppem in new[] { 9.0, 12.5, 40 })
                        {
                            for (int k = 0; k < 6; k++)
                            {
                                var glyph = (ushort)(1 + random.Next(390));
                                bool found = typeface.TryGetOutline(glyph, new OutlineRequest { PixelsPerEm = ppem, GridFitting = mode }, out var outline);
                                if (found && outline.IsGridFitted)
                                    hinted++;
                                else
                                    unhinted++;
                            }
                        }
                    }
                }
            });

            // the fuzz did both: hinted glyphs and glyphs that fell back
            Assert.True(hinted > 100, $"only {hinted} hinted outlines");
            Assert.True(unhinted > 10, $"only {unhinted} fallbacks");

            // and every fall back was a controlled one: the interpreter never tripped over its own arrays
            Assert.Equal(unexpectedBefore, HintingEngine.UnexpectedFailures);
        }

        [Fact]
        public void RandomBytesAsEveryProgramOfAFontNeverEscapeAsUnexpectedExceptions()
        {
            var original = HostileFonts.Original();
            var random = new Random(99);
            int failures = 0;

            AssertBounded(TimeSpan.FromSeconds(120), () =>
            {
                for (int iteration = 0; iteration < 120; iteration++)
                {
                    var font = HostileFonts.WithTable(HostileFonts.WithTable(original, "fpgm", RandomProgram(random, 300)), "prep", RandomProgram(random, 300));
                    var face = FaceOf(font);
                    try
                    {
                        var size = TtSize.Create(face, Size + 64 * random.Next(30), random.Next(2) == 0 ? TtInterpreterVersion.V40 : TtInterpreterVersion.V35,
                            random.Next(2) == 0 ? TtRenderMode.Normal : TtRenderMode.Mono);
                        for (int g = 1; g <= 40; g++)
                            TtGlyphLoader.Load(size, g);
                    }
                    catch (HintingException)
                    {
                        failures++;
                    }
                }
            });

            Assert.True(failures > 20);
        }

        private static byte[] RandomProgram(Random random, int length)
        {
            var bytes = new byte[length];
            random.NextBytes(bytes);
            return bytes;
        }

        private static byte[] Be16(int value) => [(byte)(value >> 8), (byte)value];
    }
}
