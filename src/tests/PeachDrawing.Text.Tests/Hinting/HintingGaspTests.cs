using PeachDrawing.Text.Internal.Fonts;
using PeachDrawing.Text.Internal.Fonts.OpenType;
using PeachDrawing.Text.Internal.Hinting.FreeType;
using PeachDrawing.Text.Outlines;
using PeachPDF.Tests.TestSupport;
using System.Buffers.Binary;
using System.Text.Json.Serialization;

namespace PeachDrawing.Text.Tests.Hinting
{
    /// <summary>
    /// The <c>gasp</c> table against FreeType 2.14.3, and the decision it drives: a size at which the font does not ask for grid-fitting
    /// (<c>FT_GASP_DO_GRIDFIT</c> not set) is not hinted. The reference (<c>assets/fonts/generate_hinting_gasp_fixtures.py</c>) is FreeType's own
    /// <c>FT_Get_Gasp</c> for the same font with ten different <c>gasp</c> tables, and what FreeType's hinter makes of the font's glyphs at each
    /// size (it hints at every size).
    /// </summary>
    public class HintingGaspTests
    {
        private static readonly Lazy<GaspGoldenFile> Golden = new(() => HintingGoldenData.Load<GaspGoldenFile>("HintingGasp.golden.json.gz"));

        public static TheoryData<string> Variants()
        {
            var data = new TheoryData<string>();
            foreach (var variant in Golden.Value.Variants)
                data.Add(variant.Name);
            return data;
        }

        private static GaspGoldenVariant Variant(string name) => Golden.Value.Variants.Single(v => v.Name == name);

        private static OpenTypeFontface Face(byte[] font) => FontFileData.GetOrCreateFrom(font).Fontface;

        private static OutlineRequest Request(double ppem, GridFitting mode = GridFitting.Standard) => new() { PixelsPerEm = ppem, GridFitting = mode };

        [Fact]
        public void TheGoldenDataWasMadeByTheFreeTypeTheEngineWasPortedFrom()
        {
            Assert.Equal("2.14.3", Golden.Value.FreeType.Version);
            Assert.Equal("VER-2-14-3", Golden.Value.FreeType.Tag);
        }

        [Fact]
        public void TheReferenceIsNotVacuous()
        {
            var all = Golden.Value.Variants.SelectMany(v => v.Flags).ToList();

            // no table, every kind of range flag, and enough variants: a comparison over one answer would pass whatever the port does
            Assert.True(Golden.Value.Variants.Count >= 10);
            Assert.Contains(-1, all);
            foreach (int flags in new[] { 0, 1, 2, 3, 12, 15 })
                Assert.Contains(flags, all);
            Assert.Equal(Golden.Value.Ppems.Length, Golden.Value.Variants[0].Flags.Length);
        }

        // ---------------------------------------------------------------------------------------------- the table

        [Theory]
        [MemberData(nameof(Variants))]
        public void TheFlagsOfATableEqualWhatFreeTypeAnswers(string name)
        {
            var variant = Variant(name);
            var gasp = TtGasp.TryRead(Face(Convert.FromBase64String(variant.Font)));

            for (int i = 0; i < Golden.Value.Ppems.Length; i++)
            {
                int ppem = Golden.Value.Ppems[i];
                int expected = variant.Flags[i];
                int actual = gasp?.GetFlags(ppem) ?? TtGasp.NoTable;
                Assert.True(expected == actual, $"{name}: at {ppem} ppem FreeType says {expected} and the port {actual}");
            }
        }

        [Fact]
        public void AFontWithoutATableHasNoRangesAndAVersionThatIsNotSupportedIsIgnored()
        {
            Assert.Null(TtGasp.TryRead(Face(Convert.FromBase64String(Variant("no-table").Font))));
            Assert.Null(TtGasp.TryRead(Face(Convert.FromBase64String(Variant("version-2").Font))));
            Assert.Equal(0, TtGasp.TryRead(Face(Convert.FromBase64String(Variant("no-ranges").Font)))!.RangeCount);
            Assert.Equal(5, TtGasp.TryRead(Face(Convert.FromBase64String(Variant("version-1").Font)))!.RangeCount);
        }

        [Fact]
        public void ATableCutShortIsIgnoredNotReadPastItsEnd()
        {
            var font = Convert.FromBase64String(Variant("version-1").Font);
            var tables = SyntheticUvsFont.ReadTables(font, out var sfntVersion);

            // it says nine ranges and has five (FreeType reads on into whatever follows in the file; the port stays inside the table)
            var declaresTooMany = (byte[])tables["gasp"].Clone();
            BinaryPrimitives.WriteUInt16BigEndian(declaresTooMany.AsSpan(2), 9);
            tables["gasp"] = declaresTooMany;
            Assert.Null(TtGasp.TryRead(Face(SyntheticUvsFont.WriteTables(tables, sfntVersion))));

            // a table that has no room for its own header
            tables["gasp"] = new byte[3];
            Assert.Null(TtGasp.TryRead(Face(SyntheticUvsFont.WriteTables(tables, sfntVersion))));
        }

        [Fact]
        public void GridFittingIsAllowedWhereTheRangeSaysSoAndWhereItSaysNothing()
        {
            var gasp = TtGasp.TryRead(Face(Convert.FromBase64String(Variant("last-range-ends-at-100").Font)))!;

            Assert.True(gasp.AllowsGridFit(12));   // 0x0001
            Assert.False(gasp.AllowsGridFit(13));  // 0x0002: gray only
            Assert.False(gasp.AllowsGridFit(100));
            Assert.True(gasp.AllowsGridFit(101));  // beyond every range: the font says nothing
        }

        // ------------------------------------------------------------------------------ the decision, through the API

        private static List<(long X, long Y)> Points(GlyphOutline outline)
        {
            var points = new List<(long, long)>();
            foreach (var contour in outline.Contours)
            {
                int first = points.Count;
                points.Add(((long)Math.Round(contour.Start.X * 64), (long)Math.Round(contour.Start.Y * 64)));
                foreach (var segment in contour.Segments)
                {
                    Assert.False(segment.IsCubic);
                    points.Add(((long)Math.Round(segment.End.X * 64), (long)Math.Round(segment.End.Y * 64)));
                }

                // the outline closes a contour with a line back to where it began; the points of the font do not repeat it
                if (points.Count - first > 1 && points[^1] == points[first])
                    points.RemoveAt(points.Count - 1);
            }

            return points;
        }

        /// <summary>The design outline scaled to a size, in 26.6.</summary>
        private static List<(long X, long Y)> Scaled(GlyphOutline design, double ppem, int unitsPerEm) =>
            Points(design).Select(p => ((long)Math.Round(p.X / 64.0 * ppem / unitsPerEm * 64), (long)Math.Round(p.Y / 64.0 * ppem / unitsPerEm * 64))).ToList();

        [Theory]
        [MemberData(nameof(Variants))]
        public void ASizeIsFittedExactlyWhereTheTableAllowsItAndIsFreeTypesOutlineThere(string name)
        {
            var variant = Variant(name);
            var typeface = TypefaceFixtures.FromBytes(Convert.FromBase64String(variant.Font));
            typeface.TryMapRune(new System.Text.Rune('A'), out var a);
            typeface.TryMapRune(new System.Text.Rune('B'), out var b);

            int allowedSizes = 0, refusedSizes = 0, movedByHinting = 0;
            foreach (var (glyph, gid) in new[] { (a, "1"), (b, "2") })
            {
                Assert.True(typeface.TryGetOutline(glyph, out var design));
                foreach (var size in Golden.Value.Sizes)
                {
                    int flags = variant.Flags[Array.IndexOf(Golden.Value.Ppems, size)];
                    bool allowed = flags == -1 || (flags & 1) != 0;
                    var expected = Golden.Value.Glyphs[gid][size.ToString()];

                    Assert.True(typeface.TryGetOutline(glyph, Request(size), out var outline));

                    if (allowed)
                    {
                        allowedSizes++;
                        Assert.True(outline.IsGridFitted, $"{name}: glyph {gid} at {size} ppem (gasp {flags}) is not fitted");
                        Assert.Equal(expected.X.Zip(expected.Y, (x, y) => ((long)x, (long)y)).ToList(), Points(outline));
                        Assert.Equal(expected.Advance / 64.0, outline.GridFittedAdvance);
                        Assert.True(typeface.TryGetGridFittedAdvance(glyph, Request(size), out var advance));
                        Assert.Equal(expected.Advance / 64.0, advance);

                        // the fitting moved something: the size is not one where the scaled design happens to be on the grid already
                        if (!Scaled(design, size, typeface.Metrics.UnitsPerEm).SequenceEqual(Points(outline)))
                            movedByHinting++;
                    }
                    else
                    {
                        refusedSizes++;
                        Assert.False(outline.IsGridFitted, $"{name}: glyph {gid} at {size} ppem (gasp {flags}) is fitted");
                        Assert.Equal(size, outline.PixelsPerEm);
                        Assert.Null(outline.GridFittedAdvance);
                        Assert.False(typeface.TryGetGridFittedAdvance(glyph, Request(size), out _));

                        // what it gives instead is the design, scaled
                        Assert.Equal(Scaled(design, size, typeface.Metrics.UnitsPerEm), Points(outline));
                    }
                }
            }

            // each variant of the table has sizes it allows and sizes it does not, or the table is one of those that says the same everywhere
            Assert.True(allowedSizes + refusedSizes == Golden.Value.Sizes.Length * 2);
            if (allowedSizes > 0)
                Assert.True(movedByHinting > 0, $"{name}: hinting did not change any outline, so the comparison would not show a difference");
        }

        [Fact]
        public void TheTableOfTheFontIsWhatDecidesForFractionalSizesToo()
        {
            var typeface = TypefaceFixtures.FromFile(Path.Combine(AppContext.BaseDirectory, "HintingGasp.ttf"));
            typeface.TryMapRune(new System.Text.Rune('A'), out var glyph);

            // the font asks for whole pixels per em, so a size is the nearest whole one: 8 (no fitting) up to 8.49, 9 (fitting) from 8.5
            Assert.False(typeface.TryGetOutline(glyph, Request(8.4), out var eight) && eight.IsGridFitted);
            Assert.True(typeface.TryGetOutline(glyph, Request(8.5), out var nine) && nine.IsGridFitted);

            // 20 is the last size of a range that has the flag, and 21 the first of one that has not
            Assert.True(typeface.TryGetOutline(glyph, Request(20.4), out var twenty) && twenty.IsGridFitted);
            Assert.False(typeface.TryGetOutline(glyph, Request(20.6), out var twentyOne) && twentyOne.IsGridFitted);
        }

        [Fact]
        public void BothModesFollowTheTable()
        {
            var typeface = TypefaceFixtures.FromFile(Path.Combine(AppContext.BaseDirectory, "HintingGasp.ttf"));
            typeface.TryMapRune(new System.Text.Rune('A'), out var glyph);

            foreach (var mode in new[] { GridFitting.Standard, GridFitting.Monochrome })
            {
                Assert.True(typeface.TryGetOutline(glyph, Request(12, mode), out var fitted) && fitted.IsGridFitted);
                Assert.False(typeface.TryGetOutline(glyph, Request(6, mode), out var unfitted) && unfitted.IsGridFitted);
            }
        }
    }

    internal sealed class GaspGoldenFile
    {
        [JsonPropertyName("freetype")]
        public FreeTypeInfo FreeType { get; set; } = new();

        [JsonPropertyName("ppems")]
        public int[] Ppems { get; set; } = [];

        [JsonPropertyName("sizes")]
        public int[] Sizes { get; set; } = [];

        [JsonPropertyName("variants")]
        public List<GaspGoldenVariant> Variants { get; set; } = [];

        [JsonPropertyName("glyphs")]
        public Dictionary<string, Dictionary<string, GlyphGolden>> Glyphs { get; set; } = [];
    }

    internal sealed class GaspGoldenVariant
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("font")]
        public string Font { get; set; } = "";

        [JsonPropertyName("flags")]
        public int[] Flags { get; set; } = [];
    }
}
