using PeachDrawing.Text.Internal.Fonts.OpenType;
using PeachDrawing.Text.Outlines;
using PeachPDF.Tests.TestSupport;
using System.Text;
using System.Text.Json;

namespace PeachDrawing.Text.Tests.Fonts
{
    /// <summary>
    /// The variable paints of <c>COLR</c> version 1 (<c>PaintVar*</c>, <c>VarColorLine</c>, <c>VarAffine2x3</c>) and the variable clip boxes,
    /// read at a location of a variable font. The reference (<c>VariableColorTest.golden.json</c>, written by
    /// <c>assets/fonts/generate_variable_color_fixture.py</c>) is the numbers of the masters the font was merged from, interpolated by fontTools'
    /// own <c>VariationModel</c>; the font's store keeps whole-number deltas, so the values agree within a unit for the FWord fields and within
    /// a few 1/16384 for the fixed-point ones.
    /// </summary>
    public class VariableColorFontTests
    {
        // Values written in font units (points, radii, matrix translations) are compared within a unit; the rest (opacities, offsets, angles,
        // matrix coefficients) within a few of the 1/16384 steps the deltas are stored in.
        private const double UnitTolerance = 1.5;
        private const double FixedTolerance = 0.004;

        private static readonly string[] GlyphNames =
            ["solidV", "linearV", "radialV", "sweepV", "transformV", "translateV", "scaleV", "scaleCV", "scaleUV", "scaleUCV", "rotateV", "rotateCV", "skewV", "skewCV"];

        private static Typeface Load(string path)
        {
            var set = new FontSet();
            var family = set.AddFile(path, new AddOptions { FamilyName = "VariableColor-" + Guid.NewGuid().ToString("N") });
            Assert.True(family.TryMatch(new TypefaceQuery(), out var match));
            return match.Typeface;
        }

        private static AxisSetting[] Settings(JsonElement location) =>
            location.EnumerateObject().Select(p => new AxisSetting(p.Name, p.Value.GetDouble())).ToArray();

        private static ushort Glyph(Typeface face, string name)
        {
            Assert.True(face.TryMapRune(new Rune('A' + Array.IndexOf(GlyphNames, name)), out var glyph), name);
            return glyph;
        }

        private static JsonElement Golden() => JsonDocument.Parse(File.ReadAllText(BundledFonts.VariableColorTestGolden)).RootElement;

        /// <summary>The numbers of a paint graph in the order the reference lists them (see the generator).</summary>
        internal static List<double> Walk(ColorPaint? paint)
        {
            var numbers = new List<double>();
            void AddStops(ColorLine line)
            {
                foreach (var stop in line.Stops)
                {
                    numbers.Add(stop.Offset);
                    numbers.Add(stop.Alpha);
                }
            }

            for (int depth = 0; paint is not null && depth < 16; depth++)
            {
                switch (paint)
                {
                    case PaintGlyph glyph:
                        paint = glyph.Paint;
                        continue;
                    case PaintTransform transform:
                        var a = transform.Affine;
                        numbers.AddRange([a.XX, a.YX, a.XY, a.YY, a.DX, a.DY]);
                        paint = transform.Paint;
                        continue;
                    case PaintSolid solid:
                        numbers.Add(solid.Alpha);
                        break;
                    case PaintLinearGradient linear:
                        numbers.AddRange([linear.X0, linear.Y0, linear.X1, linear.Y1, linear.X2, linear.Y2]);
                        AddStops(linear.Line);
                        break;
                    case PaintRadialGradient radial:
                        numbers.AddRange([radial.X0, radial.Y0, radial.R0, radial.X1, radial.Y1, radial.R1]);
                        AddStops(radial.Line);
                        break;
                    case PaintSweepGradient sweep:
                        numbers.AddRange([sweep.CenterX, sweep.CenterY, sweep.StartAngle, sweep.EndAngle]);
                        AddStops(sweep.Line);
                        break;
                }

                break;
            }

            return numbers;
        }

        private static void AssertClose(double expected, double actual, string what)
        {
            double tolerance = Math.Abs(expected) >= 5 ? UnitTolerance : FixedTolerance;
            Assert.True(Math.Abs(expected - actual) <= tolerance, $"{what}: {actual}, expected {expected}");
        }

        [Fact]
        public void TheFontHasVariablePaints_OfEveryVariableFormat()
        {
            var face = Load(BundledFonts.VariableColorTest);

            Assert.True(face.IsVariable);
            Assert.True(face.HasColorGlyphs);
            foreach (var name in GlyphNames)
                Assert.NotNull(face.GetColorPaint(Glyph(face, name)));
        }

        [Fact]
        public void EveryVariablePaint_AtEveryLocation_HasTheNumbersTheMastersInterpolateTo()
        {
            var face = Load(BundledFonts.VariableColorTest);

            foreach (var location in Golden().GetProperty("locations").EnumerateArray())
            {
                var instance = face.WithAxes(Settings(location.GetProperty("location")));
                var where = location.GetProperty("location").ToString();

                foreach (var name in GlyphNames)
                {
                    var actual = Walk(instance.GetColorPaint(Glyph(face, name)));
                    var expected = location.GetProperty("values").GetProperty(name).EnumerateArray().Select(v => v.GetDouble()).ToArray();

                    Assert.True(expected.Length == actual.Count, $"{where} {name}: {actual.Count} numbers, expected {expected.Length}");
                    for (int i = 0; i < expected.Length; i++)
                        AssertClose(expected[i], actual[i], $"{where} {name} #{i}");
                }
            }
        }

        [Fact]
        public void EveryClipBox_AtEveryLocation_HasTheNumbersTheMastersInterpolateTo()
        {
            var face = Load(BundledFonts.VariableColorTest);

            foreach (var location in Golden().GetProperty("locations").EnumerateArray())
            {
                var instance = face.WithAxes(Settings(location.GetProperty("location")));
                var where = location.GetProperty("location").ToString();

                foreach (var name in GlyphNames)
                {
                    Assert.True(instance.TryGetColorClipBox(Glyph(face, name), out var box), $"{where} {name}");
                    var expected = location.GetProperty("clips").GetProperty(name).EnumerateArray().Select(v => v.GetDouble()).ToArray();

                    Assert.InRange(box.XMin, expected[0] - UnitTolerance, expected[0] + UnitTolerance);
                    Assert.InRange(box.YMin, expected[1] - UnitTolerance, expected[1] + UnitTolerance);
                    Assert.InRange(box.XMax, expected[2] - UnitTolerance, expected[2] + UnitTolerance);
                    Assert.InRange(box.YMax, expected[3] - UnitTolerance, expected[3] + UnitTolerance);
                }
            }
        }

        [Fact]
        public void TheVariablePaints_MoveWithTheLocation()
        {
            var face = Load(BundledFonts.VariableColorTest);
            var light = face.WithAxes([new AxisSetting("wght", 100)]);
            var heavy = face.WithAxes([new AxisSetting("wght", 900)]);

            foreach (var name in GlyphNames)
            {
                var glyph = Glyph(face, name);
                Assert.NotEqual(Walk(light.GetColorPaint(glyph)), Walk(heavy.GetColorPaint(glyph)));
            }
        }

        [Fact]
        public void AtTheDefaultLocation_TheTypefaceAndItsInstance_ReadTheSameNumbers()
        {
            var face = Load(BundledFonts.VariableColorTest);
            var same = face.WithAxes([new AxisSetting("wght", 400), new AxisSetting("wdth", 100)]);

            foreach (var name in GlyphNames)
                Assert.Equal(Walk(face.GetColorPaint(Glyph(face, name))), Walk(same.GetColorPaint(Glyph(face, name))));
        }

        [Fact]
        public void APaintIsMadeOncePerLocation()
        {
            var face = Load(BundledFonts.VariableColorTest);
            var bold = face.WithAxes([new AxisSetting("wght", 700)]);
            var glyph = Glyph(face, "linearV");

            Assert.Same(bold.GetColorPaint(glyph), bold.GetColorPaint(glyph));
            Assert.NotSame(face.GetColorPaint(glyph), bold.GetColorPaint(glyph));
        }

        [Fact]
        public void ALayerListPaint_FollowsTheLocationToo()
        {
            // The layer list of a font whose colour glyph is PaintColrLayers is read at the instance's location as well.
            var face = Load(BundledFonts.VariableColorTest);

            Assert.Null(face.WithAxes([new AxisSetting("wght", 700)]).GetColorLayerPaint(0));
        }

        [Fact]
        public void AFontThatIsNotVariable_ReadsItsPaintsAsBefore_AndGivesNoClipBoxWithoutAClipList()
        {
            var face = Load(BundledFonts.ColorV1);

            Assert.False(face.IsVariable);
            face.TryMapRune(new Rune('A'), out var glyph);
            Assert.NotNull(face.GetColorPaint(glyph));
            Assert.False(face.TryGetColorClipBox(glyph, out _));
            Assert.Same(face.GetColorPaint(glyph), face.WithAxes([]).GetColorPaint(glyph));
        }

        [Fact]
        public void AFontWithoutColrV1_HasNoClipBox()
        {
            var face = Load(BundledFonts.VariableTest);

            Assert.False(face.TryGetColorClipBox(2, out var box));
            Assert.Equal(default, box);
        }

        [Fact]
        public void AGlyphOutsideTheClipList_HasNoClipBox()
        {
            var face = Load(BundledFonts.VariableColorTest);

            Assert.False(face.TryGetColorClipBox(1, out _));
            Assert.False(face.TryGetColorClipBox(ushort.MaxValue, out _));
        }

        // ---- damaged fonts -------------------------------------------------------------------------------------------------------------

        private static int RecordOf(byte[] font, string tag)
        {
            int count = (font[4] << 8) | font[5];
            for (int i = 0; i < count; i++)
            {
                int record = 12 + i * 16;
                if (Encoding.ASCII.GetString(font, record, 4) == tag)
                    return record;
            }

            throw new InvalidOperationException(tag);
        }

        private static Typeface LoadBytes(byte[] font)
        {
            var set = new FontSet();
            var family = set.AddData(font, new AddOptions { FamilyName = "Damaged-" + Guid.NewGuid().ToString("N") });
            Assert.True(family.TryMatch(new TypefaceQuery(), out var match));
            return match.Typeface;
        }

        [Fact]
        public void AnyOneDamagedByteInTheColrTable_NeverMakesReadingAnInstanceThrow()
        {
            var original = File.ReadAllBytes(BundledFonts.VariableColorTest);
            int record = RecordOf(original, "COLR");
            int offset = (original[record + 8] << 24) | (original[record + 9] << 16) | (original[record + 10] << 8) | original[record + 11];
            int length = (original[record + 12] << 24) | (original[record + 13] << 16) | (original[record + 14] << 8) | original[record + 15];

            for (int at = offset; at < offset + length; at++)
            {
                foreach (byte value in new byte[] { 0x00, 0xFF, 0x80 })
                {
                    var font = (byte[])original.Clone();
                    font[at] = value;

                    Typeface face;
                    try
                    {
                        face = LoadBytes(font);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
                    {
                        // A header the font loader itself refuses (the table's own counts) is not what is under test.
                        continue;
                    }

                    var instance = face.WithAxes([new AxisSetting("wght", 850), new AxisSetting("wdth", 90)]);
                    for (ushort glyph = 0; glyph < 20; glyph++)
                    {
                        Walk(instance.GetColorPaint(glyph));
                        instance.GetColorLayerPaint(glyph);
                        instance.TryGetColorClipBox(glyph, out _);
                    }
                }
            }
        }

        // ---- the variation data ------------------------------------------------------------------------------------------------------

        private sealed class Writer
        {
            private readonly List<byte> _bytes = [];

            public Writer U8(int v) { _bytes.Add((byte)v); return this; }
            public Writer U16(int v) { _bytes.Add((byte)(v >> 8)); _bytes.Add((byte)v); return this; }
            public Writer U32(uint v) { U16((int)(v >> 16)); U16((int)(v & 0xFFFF)); return this; }
            public Writer F2Dot14(double v) => U16((short)Math.Round(v * 16384));
            public byte[] ToArray() => _bytes.ToArray();
        }

        /// <summary>A store with one axis and one region (0, 1, 1), one data set of three items whose one delta each is 100, -50 and 7.</summary>
        private static byte[] BuildStore()
        {
            int header = 2 + 4 + 2 + 4;
            var regionList = new Writer().U16(1).U16(1).F2Dot14(0).F2Dot14(1).F2Dot14(1).ToArray();
            var data = new Writer().U16(3).U16(1).U16(1).U16(0).U16(100).U16(unchecked((ushort)-50)).U16(7).ToArray();
            return new Writer().U16(1).U32((uint)header).U16(1).U32((uint)(header + regionList.Length)).ToArray()
                .Concat(regionList).Concat(data).ToArray();
        }

        [Fact]
        public void ColrVariations_WithoutAMap_TakesTheOuterIndexFromTheHighBits()
        {
            var variations = ColrVariations.TryParse(BuildStore(), 0, 0)!;

            Assert.Null(variations);

            variations = ColrVariations.TryParse(BuildStore(), 0, 1);
            Assert.Null(variations);   // an offset of 1 is inside the table but is not a store

            var table = new byte[4].Concat(BuildStore()).ToArray();
            variations = ColrVariations.TryParse(table, 0, 4);
            Assert.NotNull(variations);
            // Item 0, 1 and 2 of data set 0 through the variation indices 0, 1 and 2; the location half way to the peak halves each delta.
            Assert.Equal(50, variations.GetDelta(0, 0, [0.5]));
            Assert.Equal(-25, variations.GetDelta(0, 1, [0.5]));
            Assert.Equal(3.5, variations.GetDelta(2, 0, [0.5]));
            // Past the items, or in another data set, or at the marker for no variation: nothing.
            Assert.Equal(0, variations.GetDelta(3, 0, [0.5]));
            Assert.Equal(0, variations.GetDelta(1 << 16, 0, [0.5]));
            Assert.Equal(0, variations.GetDelta(ColrVariations.NoVariation, 0, [0.5]));
            Assert.Equal(0, variations.GetDelta(ColrVariations.NoVariation - 1, 1, [0.5]));
        }

        [Fact]
        public void ColrVariations_WithAMap_MapsTheVariationIndex()
        {
            // A format 0 map of two entries with one-byte entries and two inner bits: index 0 -> item 2, index 1 -> item 0.
            var map = new Writer().U8(0).U8(0x01).U16(2).U8(2).U8(0).ToArray();
            var store = BuildStore();
            var table = new byte[4].Concat(store).Concat(map).ToArray();

            var variations = ColrVariations.TryParse(table, (uint)(4 + store.Length), 4);

            Assert.NotNull(variations);
            Assert.Equal(3.5, variations.GetDelta(0, 0, [0.5]));
            Assert.Equal(50, variations.GetDelta(0, 1, [0.5]));
            // An index past the end of the map uses its last entry.
            Assert.Equal(50, variations.GetDelta(9, 0, [0.5]));
        }

        [Fact]
        public void ColrVariations_WithADamagedMap_IsNotUsed()
        {
            var store = BuildStore();
            var table = new byte[4].Concat(store).ToArray();

            Assert.Null(ColrVariations.TryParse(table, (uint)table.Length + 10, 4));
            Assert.Null(ColrVariations.TryParse(table, (uint)table.Length - 1, 4));   // the last byte is not a map format
        }
    }
}
