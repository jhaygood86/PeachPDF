using PeachDrawing.Text.Internal.Fonts;
using PeachDrawing.Text.Internal.Fonts.OpenType;
using PeachDrawing.Text.Internal.Fonts.OpenType.Variations;
using PeachDrawing.Text.Internal.Hinting;
using PeachDrawing.Text.Internal.Hinting.FreeType;
using PeachDrawing.Text.Outlines;
using PeachPDF.Tests.TestSupport;
using System.Buffers.Binary;

namespace PeachDrawing.Text.Tests.Hinting
{
    /// <summary>
    /// The variation tables of a font (<c>fvar</c>, <c>avar</c>, <c>gvar</c>, <c>cvar</c>, <c>HVAR</c>, <c>VVAR</c>, <c>MVAR</c>) are untrusted input, and hinting a variable font
    /// reads all of them: the deltas of every glyph, the control values, the advances, the ranges of <c>gasp</c>. Whatever the tables say, hinting has to end in bounded time and
    /// space with a controlled failure (which the public API turns into the unhinted outline), never with an unexpected exception or a hang.
    /// </summary>
    public class HintingVariableRobustnessTests
    {
        private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, name));

        private static readonly string[] VariationTables = ["fvar", "avar", "gvar", "cvar", "HVAR", "MVAR"];

        /// <summary>Hints the first glyphs of a font at two sizes and at three locations of it; false when the font is not one this engine hints.</summary>
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
            if (fontface.Variations is { } variations)
            {
                var r = random ?? new Random(7);
                locations.Add(variations.CreateCoordinates(variations.Axes.Select(a => (a.Tag, a.Minimum + r.NextDouble() * (a.Maximum - a.Minimum)))));
                locations.Add(variations.CreateCoordinates(variations.Axes.Select(a => (a.Tag, a.Maximum))));
            }

            // the tables themselves, read directly: an exception is not swallowed here as it is by TtFace.TryCreate
            if (TtVarTables.For(fontface) is { } tables)
            {
                foreach (var location in locations)
                {
                    var ndv = TtVarTables.NormalizedCoordinates(fontface, location);
                    if (ndv is not null && TtBlend.TryCreate(tables, ndv, isCff2: false) is { } blend)
                    {
                        blend.VaryCvt(new int[24]);
                        _ = blend.MvarAdjust(0x67737030, 8);
                        _ = blend.AdjustAdvance(false, 3, 500);
                    }
                }
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
                }
            }

            return hinted;
        }

        [Fact]
        public void EveryByteOfEachVariationTableChangedIsAnsweredWithoutAnUnexpectedException()
        {
            // every byte of the small tables (and of the beginning of the big one, where its header is), flipped with three masks in turn
            var original = Fixture("HintingVariable.ttf");
            long unexpectedBefore = HintingEngine.UnexpectedFailures;

            // (the bound is on each case, not on the sweep: see WorkBounds)
            int hinted = 0, cases = 0;
            foreach (var tag in VariationTables)
            {
                var (_, offset, length) = HostileFonts.Tables(original).Single(t => t.Tag == tag);
                foreach (byte mask in new byte[] { 0xFF, 0x80, 0x01 })
                {
                    for (int at = 0; at < length; at += tag == "gvar" && at > 600 ? 13 : 1)
                    {
                        var font = (byte[])original.Clone();
                        font[offset + at] ^= mask;
                        cases++;
                        if (WorkBounds.Case(() => Hint(font, 3), $"{tag} byte {at} ^ {mask:X2}"))
                            hinted++;
                    }
                }
            }

            Assert.True(cases > 3000, $"only {cases} cases");
            Assert.True(hinted > cases / 2, $"only {hinted} of the {cases} damaged fonts hinted at all");
            Assert.Equal(unexpectedBefore, HintingEngine.UnexpectedFailures);
        }

        [Fact]
        public void RandomDamageToAVariableFontIsAnsweredWithoutAnUnexpectedException()
        {
            var random = new Random(20260926);
            long unexpectedBefore = HintingEngine.UnexpectedFailures;
            int hinted = 0, refused = 0;

            foreach (var file in new[] { "HintingVariable.ttf", "HintingVariableNoHvar.ttf", "HintingVariableVertical.ttf", "HintingVariableAvar2.ttf" })
            {
                var original = Fixture(file);
                for (int iteration = 0; iteration < 500; iteration++)
                {
                    var font = (byte[])original.Clone();
                    var present = VariationTables.Where(t => HostileFonts.Tables(font).Any(x => x.Tag == t)).ToArray();
                    HostileFonts.Scramble(font, present[iteration % present.Length], random, 1 + random.Next(40));

                    if (WorkBounds.Case(() => Hint(font, 6, random), $"{file} damage {iteration}"))
                        hinted++;
                    else
                        refused++;
                }
            }

            Assert.True(hinted > 500, $"only {hinted} damaged fonts hinted");
            Assert.Equal(unexpectedBefore, HintingEngine.UnexpectedFailures);
            _ = refused;
        }

        [Fact]
        public void ATruncatedTableIsNeverAnUnexpectedFailure()
        {
            var original = Fixture("HintingVariableVertical.ttf");
            long unexpectedBefore = HintingEngine.UnexpectedFailures;

            foreach (var tag in new[] { "fvar", "avar", "gvar", "cvar", "HVAR", "MVAR", "VVAR" })
            {
                var table = HostileFonts.TableBytes(original, tag);
                for (int length = 0; length < table.Length; length += length < 120 ? 1 : 61)
                    Hint(HostileFonts.WithTable(original, tag, table[..length]), 3);
            }

            Assert.Equal(unexpectedBefore, HintingEngine.UnexpectedFailures);
        }

        [Fact]
        public void EveryVariantIsReadInBoundedTimeAndSpace()
        {
            // the faults include tables that name 65,535 glyphs, 4,095 tuples, 40,000 regions and offsets and counts of every size
            var golden = HintingGoldenData.Load<VariableVariantFile>("HintingVariableVariants.golden.json.gz");
            var main = Fixture("HintingVariable.ttf");
            long unexpectedBefore = HintingEngine.UnexpectedFailures;

            foreach (var variant in golden.Variants)
            {
                var font = variant.Name == "baseline" ? main : variant.Data is null ? HostileFonts.WithoutTable(main, variant.Table) : HostileFonts.WithTable(main, variant.Table, Convert.FromBase64String(variant.Data));
                long before = GC.GetAllocatedBytesForCurrentThread();

                WorkBounds.Case(() => Hint(font, 40, new Random(1)), variant.Name);

                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.True(allocated < 64L * 1024 * 1024, $"{variant.Name}: allocated {allocated / 1024 / 1024} MB");
            }

            Assert.Equal(unexpectedBefore, HintingEngine.UnexpectedFailures);
        }

        // ---- tables that ask for a lot of work ----------------------------------------------------------------------------------

        private static ushort U16(byte[] data, int at) => BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(at));

        /// <summary>
        /// The main font with the variation data of one glyph replaced by 4,095 tuples that each apply to every point of the glyph (their data is the same bytes every time: their sizes are
        /// 0), which is 4,095 times the points in additions and in reading (FreeType does all of it; the port refuses the glyph).
        /// </summary>
        private static byte[] WithAHostileGlyph(byte[] font, int glyph, int points, int tuples)
        {
            var gvar = HostileFonts.TableBytes(font, "gvar");
            int glyphCount = U16(gvar, 12);
            int dataArray = (int)BinaryPrimitives.ReadUInt32BigEndian(gvar.AsSpan(16));
            int Offset(int g) => U16(gvar, 20 + 2 * g) * 2;

            // what is before the glyph stays; the glyph gets the new data, and the glyphs after it none
            var head = gvar.AsSpan(0, dataArray + Offset(glyph)).ToArray();

            int axes = U16(gvar, 4);
            var block = new List<byte>();
            block.AddRange([(byte)(tuples >> 8), (byte)tuples]);                                  // tuple count, no shared points
            int dataStart = 4 + tuples * (4 + 2 * axes);
            block.AddRange([(byte)(dataStart >> 8), (byte)dataStart]);                            // where the data starts
            for (int t = 0; t < tuples; t++)
            {
                block.AddRange([0, 0]);                                                            // the size of this tuple's data: none, so the next one reads these bytes again
                block.AddRange([0xA0, 0x00]);                                                      // an embedded peak and private point numbers
                for (int a = 0; a < axes; a++)
                    block.AddRange([0x40, 0x00]);                                                  // a peak of 1
            }

            // the data: every point (0), then the deltas of x and of y as runs of zeros
            block.Add(0);
            for (int pass = 0; pass < 2; pass++)
            {
                for (int left = points; left > 0; left -= 64)
                    block.Add((byte)(0x80 | (Math.Min(left, 64) - 1)));
            }

            if (block.Count % 2 != 0)
                block.Add(0);

            var table = new List<byte>(head);
            table.AddRange(block);

            var result = table.ToArray();
            int start = Offset(glyph);
            int end = start + block.Count;
            for (int g = glyph + 1; g <= glyphCount; g++)
                BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(20 + 2 * g), (ushort)(end / 2));

            return HostileFonts.WithTable(font, "gvar", result);
        }

        /// <summary>The font with its last glyph replaced by a simple glyph of one contour of <paramref name="points"/> on-curve points.</summary>
        private static byte[] WithABigLastGlyph(byte[] font, int points)
        {
            var head = HostileFonts.TableBytes(font, "head");
            bool longLoca = BinaryPrimitives.ReadInt16BigEndian(head.AsSpan(50)) != 0;
            var loca = HostileFonts.TableBytes(font, "loca");
            var glyf = HostileFonts.TableBytes(font, "glyf");
            int count = (loca.Length >> (longLoca ? 2 : 1)) - 1;

            int LocaAt(int i) => longLoca ? (int)BinaryPrimitives.ReadUInt32BigEndian(loca.AsSpan(4 * i)) : BinaryPrimitives.ReadUInt16BigEndian(loca.AsSpan(2 * i)) * 2;

            var data = new List<byte>(glyf.AsSpan(0, LocaAt(count - 1)).ToArray());
            data.AddRange([0, 1, 0, 0, 0, 0, 0x03, 0xE8, 0x03, 0xE8]);           // one contour, a box
            data.AddRange([(byte)((points - 1) >> 8), (byte)(points - 1)]);       // its last point
            data.AddRange([0, 0]);                                                 // no instructions
            data.AddRange(Enumerable.Repeat((byte)0x01, points));                  // on-curve points with 16-bit coordinates
            for (int axis = 0; axis < 2; axis++)
            {
                for (int i = 0; i < points; i++)
                    data.AddRange([0, (byte)(i % 3)]);
            }

            while (data.Count % 4 != 0)
                data.Add(0);

            var newLoca = (byte[])loca.Clone();
            if (longLoca)
                BinaryPrimitives.WriteUInt32BigEndian(newLoca.AsSpan(4 * count), (uint)data.Count);
            else
                BinaryPrimitives.WriteUInt16BigEndian(newLoca.AsSpan(2 * count), (ushort)(data.Count / 2));

            return HostileFonts.WithTable(HostileFonts.WithTable(font, "glyf", data.ToArray()), "loca", newLoca);
        }

        [Fact]
        public void AGlyphWhoseTuplesAllApplyToEveryPointIsRefusedNotAllocatedAndAddedThousandsOfTimes()
        {
            var main = Fixture("HintingVariable.ttf");
            int points = 9000;
            int glyph = 100;
            var big = WithABigLastGlyph(main, points);
            var hostile = WithAHostileGlyph(big, glyph, points + 4, 4095);
            var font = new OpenTypeFontface(FontFileData.CreateCompiledFont(hostile));
            var variations = font.Variations!;
            var location = variations.CreateCoordinates(variations.Axes.Select(a => (a.Tag, a.Maximum)));

            long before = GC.GetAllocatedBytesForCurrentThread();
            var engine = new HintingEngine(font, null, location, g => 500);
            var result = WorkBounds.Case(() => engine.Get(glyph, 12 * 64, GridFitting.Standard), "the glyph of 4,095 tuples over 9,000 points");
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            // the glyph is refused (the caller keeps the unhinted outline), without the thousands of arrays that reading its tuples would make, and the neighbours are not touched
            Assert.False(result.Succeeded);
            Assert.True(allocated < 32L * 1024 * 1024, $"allocated {allocated / 1024 / 1024} MB");
            Assert.True(engine.Get(glyph - 1, 12 * 64, GridFitting.Standard).Succeeded);

            // and without the tuples the glyph itself is a glyph that hints
            var plain = new OpenTypeFontface(FontFileData.CreateCompiledFont(big));
            Assert.True(new HintingEngine(plain, null, null, g => 500).Get(glyph, 12 * 64, GridFitting.Standard).Succeeded);
        }

        [Fact]
        public void ACvarTableWhoseTuplesAllApplyToEveryControlValueIsRefused()
        {
            var main = Fixture("HintingVariable.ttf");
            int axes = 2;

            // 4,095 tuples with an embedded peak and private points, each with the data of size 0, so that each reads the same list of deltas: all 24 control values
            var block = new List<byte>([0x00, 0x01, 0x00, 0x00, 0x0F, 0xFF, 0x00, 0x00]);
            int dataStart = 8 + 4095 * (4 + 2 * axes);
            block[6] = (byte)(dataStart >> 8);
            block[7] = (byte)dataStart;
            for (int t = 0; t < 4095; t++)
            {
                block.AddRange([0, 0, 0xA0, 0x00]);
                for (int a = 0; a < axes; a++)
                    block.AddRange([0x40, 0x00]);
            }

            block.Add(0);
            block.Add(0x80 | 23);

            // FreeType wants room in the table for the tuples' headers again after the data offset (a sanity test of the header)
            block.AddRange(new byte[4095 * 4]);

            var fontBytes = HostileFonts.WithTable(main, "cvar", block.ToArray());
            var fontface = new OpenTypeFontface(FontFileData.CreateCompiledFont(fontBytes));
            var tables = TtVarTables.For(fontface)!;
            var blend = TtBlend.TryCreate(tables, new[] { 0x10000, 0x10000 }, isCff2: false)!;

            // 4,095 tuples of 24 control values are within the bound (98,280 additions)
            Assert.Equal(TtVarError.Ok, blend.VaryCvt(new int[24]));

            // and 65,535 control values is not (a table with that many, which FreeType allows, makes 268 million)
            Assert.Equal(TtVarError.WorkLimit, blend.VaryCvt(new int[65535]));
        }

        [Fact]
        public void AnMvarTableOfSixtyThousandValuesIsAnsweredInBoundedTime()
        {
            // 60,000 records of one tag, each with a delta of the store
            var main = Fixture("HintingVariable.ttf");
            var mvar = HostileFonts.TableBytes(main, "MVAR");
            int records = 60000;
            int storeOffset = U16(mvar, 10);

            var table = new List<byte>(mvar[..12]);
            table[8] = (byte)(records >> 8);
            table[9] = (byte)records;
            int newStoreOffset = 12 + 8 * records;
            table[10] = (byte)(newStoreOffset >> 8);
            table[11] = (byte)newStoreOffset;
            for (int i = 0; i < records; i++)
            {
                table.AddRange("hasc"u8.ToArray());
                table.AddRange([0, 0, 0, (byte)(i % 3)]);
            }

            table.AddRange(mvar[storeOffset..]);

            var fontBytes = HostileFonts.WithTable(main, "MVAR", table.ToArray());
            Assert.True(WorkBounds.Case(() => Hint(fontBytes, 4), "60,000 MVAR values"));
        }

        /// <summary>An item variation store of <paramref name="regions"/> regions over two axes and one item, whose delta for every region is 10.</summary>
        private static byte[] StoreOf(int regions)
        {
            var bytes = new List<byte>();
            void U16Add(int v) => bytes.AddRange([(byte)(v >> 8), (byte)v]);
            void U32Add(uint v) => bytes.AddRange([(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v]);

            U16Add(1);                                 // format
            U32Add(12);                                // the region list follows the header
            U16Add(1);                                 // one data subtable
            U32Add((uint)(12 + 4 + 12 * regions));    // its offset
            U16Add(2);
            U16Add(regions);
            for (int r = 0; r < regions; r++)
            {
                for (int axis = 0; axis < 2; axis++)
                {
                    U16Add(0);
                    U16Add(0x4000);
                    U16Add(0x4000);
                }
            }

            U16Add(1);                                 // one item
            U16Add(regions);                           // all of its deltas are words
            U16Add(regions);
            for (int r = 0; r < regions; r++)
                U16Add(r);
            for (int r = 0; r < regions; r++)
                U16Add(10);

            return bytes.ToArray();
        }

        [Fact]
        public void TheStepsOfAnItemDeltaAreBounded()
        {
            var data = StoreOf(2000);
            var store = new TtItemVarStore();
            Assert.Equal(TtVarError.Ok, store.Load(new FtMemStream(data), 0, data, 2));

            int[] location = [0x10000, 0x10000];
            Assert.Equal(20_000, store.GetItemDelta(location, 0, 0));

            // a budget that the regions of the delta (2,000 regions of 2 axes) do not fit in: the delta is not added
            long budget = 3999;
            Assert.Equal(0, store.GetItemDelta(location, 0, 0, ref budget));
            budget = 4000;
            Assert.Equal(20_000, store.GetItemDelta(location, 0, 0, ref budget));
            Assert.Equal(0, budget);
        }

        // ---- the caches, and threads ---------------------------------------------------------------------------------------

        [Fact]
        public void TheCachesOfTwoLocationsOfAFontAreSeparate()
        {
            // one glyph at one size, asked for at two locations in turn, several times: each location keeps its own outline, and they differ
            var typeface = HintingVariableFixtures.Default("HintingVariable.ttf");
            // (at the ends of the width; the ranges of the font's gasp table at other locations do not ask for grid-fitting at this size)
            var narrow = typeface.WithAxes([new AxisSetting("wdth", 75)]);
            var wide = typeface.WithAxes([new AxisSetting("wdth", 150)]);
            var request = new OutlineRequest { PixelsPerEm = 16, GridFitting = GridFitting.Standard };

            int different = 0;
            for (ushort glyph = 1; glyph < 60; glyph++)
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

            Assert.True(different > 20, $"only {different} glyphs differ between the two locations");
        }

        private sealed class ContourComparer : IEqualityComparer<OutlineContour>
        {
            public static readonly ContourComparer Instance = new();

            public bool Equals(OutlineContour? x, OutlineContour? y) =>
                x is not null && y is not null && x.Start == y.Start && x.Segments.Select(s => s.End).SequenceEqual(y.Segments.Select(s => s.End));

            public int GetHashCode(OutlineContour contour) => contour.Start.GetHashCode();
        }

        [Fact]
        public void AVariableTrueTypeFontCanBeHinted()
        {
            var typeface = HintingVariableFixtures.Default("HintingVariable.ttf");
            Assert.True(typeface.Face.Descriptor.Hinting.CanHint);

            var bold = typeface.WithAxes([new AxisSetting("wght", 700)]);
            Assert.True(bold.Face.Descriptor.Hinting.CanHint);
            Assert.True(bold.TryMapRune(new System.Text.Rune('"'), out var glyph));
            Assert.True(bold.TryGetOutline(glyph, new OutlineRequest { PixelsPerEm = 24, GridFitting = GridFitting.Standard }, out var outline));
            Assert.True(outline.IsGridFitted);
            Assert.True(bold.TryGetGridFittedAdvance(glyph, new OutlineRequest { PixelsPerEm = 24, GridFitting = GridFitting.Standard }, out var advance));
            Assert.Equal(Math.Round(advance), advance);
        }

        [Fact]
        public void AnInstanceHintedFromManyThreadsAtOnceGivesWhatOneThreadDoes()
        {
            var request = new OutlineRequest { PixelsPerEm = 17, GridFitting = GridFitting.Standard };
            AxisSetting[] settings = [new AxisSetting("wght", 610.5), new AxisSetting("wdth", 88.5)];

            static string Describe(Typeface typeface, ushort glyph, in OutlineRequest request) =>
                typeface.TryGetOutline(glyph, request, out var outline)
                    ? string.Join(";", outline.Contours.Select(c => string.Join(",", c.Segments.Select(s => $"{s.End.X}/{s.End.Y}")))) + "|" + outline.GridFittedAdvance
                    : "none";

            // each typeface has caches of its own (fonts that nothing else has hinted): one thread hints every glyph, and eight threads hint them in different orders
            var alone = TypefaceFixtures.FromFile(Path.Combine(AppContext.BaseDirectory, "HintingVariable.ttf")).WithAxes(settings);
            var expected = Enumerable.Range(1, 100).Select(g => Describe(alone, (ushort)g, request)).ToArray();

            var shared = TypefaceFixtures.FromFile(Path.Combine(AppContext.BaseDirectory, "HintingVariable.ttf")).WithAxes(settings);
            var results = new string[8][];
            Parallel.For(0, 8, new ParallelOptions { MaxDegreeOfParallelism = 8 }, thread =>
            {
                var mine = new string[100];
                for (int i = 0; i < 100; i++)
                {
                    int g = 1 + (i + thread * 13) % 100;
                    mine[g - 1] = Describe(shared, (ushort)g, request);
                }

                results[thread] = mine;
            });

            foreach (var mine in results)
                Assert.Equal(expected, mine);
        }
    }
}
