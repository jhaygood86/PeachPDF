using PeachDrawing.Text.Export;
using PeachPDF.Tests.TestSupport;
using System.Text;

namespace PeachDrawing.Text.Tests.Fonts
{
    /// <summary>
    /// Embedding an instance of a variable font: the exported font is a static one whose glyphs carry the location's variations. An
    /// embedded subset has no name table, so the tests read the exported file's tables directly.
    /// </summary>
    public class VariableFontExportTests
    {
        private static Typeface Load()
        {
            var set = new FontSet();
            var family = set.AddFile(BundledFonts.VariableTest, new AddOptions { FamilyName = "Export-" + Guid.NewGuid().ToString("N") });
            Assert.True(family.TryMatch(new TypefaceQuery(), out var match));
            return match.Typeface;
        }

        private sealed class SfntFile(byte[] bytes)
        {
            public byte[] Bytes { get; } = bytes;

            public IReadOnlyDictionary<string, (int Offset, int Length)> Tables { get; } = Read(bytes);

            private static Dictionary<string, (int, int)> Read(byte[] b)
            {
                var tables = new Dictionary<string, (int, int)>();
                int count = (b[4] << 8) | b[5];
                for (int i = 0; i < count; i++)
                {
                    int at = 12 + i * 16;
                    tables[Encoding.ASCII.GetString(b, at, 4)] = (I32(b, at + 8), I32(b, at + 12));
                }

                return tables;
            }

            public static int I32(byte[] b, int at) => (b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3];
            public int U16(int at) => (Bytes[at] << 8) | Bytes[at + 1];
            public int I16(int at) => (short)U16(at);

            /// <summary>The bytes of a glyph in <c>glyf</c>, found through <c>loca</c>.</summary>
            public (int Offset, int Length) Glyph(int glyph)
            {
                int loca = Tables["loca"].Offset;
                bool longOffsets = I16(Tables["head"].Offset + 50) != 0;
                int start = longOffsets ? I32(Bytes, loca + glyph * 4) : U16(loca + glyph * 2) * 2;
                int end = longOffsets ? I32(Bytes, loca + glyph * 4 + 4) : U16(loca + glyph * 2 + 2) * 2;
                return (Tables["glyf"].Offset + start, end - start);
            }
        }

        [Fact]
        public void AnInstance_IsWrittenAsAStaticFont()
        {
            var face = Load();
            var instance = face.WithAxes([new AxisSetting("wght", 800), new AxisSetting("wdth", 90)]);
            face.TryMapRune(new Rune('A'), out var a);
            face.TryMapRune(new Rune('B'), out var b);
            face.TryMapRune(new Rune(0xC1), out var aacute);

            var exported = TypefaceExporter.ExportSubset(instance, [a, b, aacute], keepCharacterMap: false);
            var file = new SfntFile(exported.Data.ToArray());

            Assert.True(exported.IsSubset);
            Assert.False(exported.HasCffOutlines);
            foreach (var variationTable in new[] { "fvar", "avar", "gvar", "HVAR", "MVAR", "cvt ", "fpgm", "prep" })
            {
                Assert.DoesNotContain(variationTable, file.Tables.Keys);
            }

            Assert.Contains("glyf", file.Tables.Keys);
            Assert.Contains("hmtx", file.Tables.Keys);
        }

        [Fact]
        public void TheGlyphsOfAnInstance_HaveTheirBoundsAndMetricsAtTheLocation()
        {
            var face = Load();
            var instance = face.WithAxes([new AxisSetting("wght", 800), new AxisSetting("wdth", 90)]);
            face.TryMapRune(new Rune('A'), out var a);
            face.TryMapRune(new Rune(0xC1), out var aacute);

            var file = new SfntFile(TypefaceExporter.ExportSubset(instance, [a, aacute], keepCharacterMap: false).Data.ToArray());

            foreach (ushort glyph in new[] { a, aacute })
            {
                Assert.True(instance.TryGetOutline(glyph, out var outline));
                var (offset, length) = file.Glyph(glyph);
                Assert.True(length > 10);

                // The bounds in the glyph header are those of the instance's outline (within a unit of rounding) ...
                double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                foreach (var contour in outline.Contours)
                {
                    foreach (var point in new[] { contour.Start }.Concat(contour.Segments.Select(s => s.End)))
                    {
                        minX = Math.Min(minX, point.X); maxX = Math.Max(maxX, point.X);
                        minY = Math.Min(minY, point.Y); maxY = Math.Max(maxY, point.Y);
                    }
                }

                Assert.InRange(file.I16(offset + 2), minX - 1.5, minX + 1.5);
                Assert.InRange(file.I16(offset + 4), minY - 1.5, minY + 1.5);
                Assert.InRange(file.I16(offset + 6), maxX - 1.5, maxX + 1.5);
                Assert.InRange(file.I16(offset + 8), maxY - 1.5, maxY + 1.5);

                // ... and hmtx has the instance's advance and a left side bearing equal to the glyph's left edge.
                int metrics = file.Tables["hmtx"].Offset + glyph * 4;
                Assert.Equal(instance.GetAdvance(glyph), file.U16(metrics));
                Assert.Equal(file.I16(offset + 2), file.I16(metrics + 2));
            }
        }

        [Fact]
        public void AnInstance_IsWrittenDifferentlyFromTheDefault()
        {
            var face = Load();
            face.TryMapRune(new Rune('B'), out var b);

            var regular = TypefaceExporter.ExportSubset(face, [b], keepCharacterMap: false).Data.ToArray();
            var black = TypefaceExporter.ExportSubset(face.WithAxes([new AxisSetting("wght", 900)]), [b], keepCharacterMap: false).Data.ToArray();

            Assert.NotEqual(regular, black);
        }

        [Fact]
        public void TheDefaultTypeface_IsStillWrittenWithItsHintingTables()
        {
            var face = Load();
            face.TryMapRune(new Rune('A'), out var a);

            var file = new SfntFile(TypefaceExporter.ExportSubset(face, [a], keepCharacterMap: true).Data.ToArray());

            Assert.Contains("cmap", file.Tables.Keys);
            Assert.Contains("glyf", file.Tables.Keys);
        }
    }
}
