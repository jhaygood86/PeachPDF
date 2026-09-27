using PeachDrawing.Text.Internal.Fonts;
using PeachDrawing.Text.Internal.Fonts.OpenType;
using PeachDrawing.Text.Internal.Hinting;
using PeachDrawing.Text.Internal.Hinting.FreeType;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Serialization;

namespace PeachDrawing.Text.Tests.Hinting
{
    /// <summary>
    /// Variation tables that are wrong in a deliberate way, and tables with a few bytes changed at random, against FreeType 2.14.3
    /// (<c>assets/fonts/generate_hinting_variable_fixtures.py</c>): whatever FreeType does with one (refuse to set a location, refuse a glyph, draw it, move a range of
    /// <c>gasp</c>), the port does. The variants are faults of each variation table (its header, its offsets, its tuples, its stores) in the synthetic font, and the mutants are
    /// that font with a few bytes of one table changed. Each is checked at the default, at a location given by design coordinates, and at raw normalized coordinates, on a
    /// digest of every glyph FreeType was asked for.
    /// </summary>
    public class HintingVariableVariantTests
    {
        private static readonly Lazy<VariableVariantFile> Golden = new(() => HintingGoldenData.Load<VariableVariantFile>("HintingVariableVariants.golden.json.gz"));
        private static readonly Lazy<byte[]> MainFont = new(() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "HintingVariable.ttf")));

        public static TheoryData<int> Variants()
        {
            var data = new TheoryData<int>();
            for (int i = 0; i < Golden.Value.Variants.Count; i++)
                data.Add(i);
            return data;
        }

        [Fact]
        public void TheGoldenDataWasMadeByTheFreeTypeTheVariationCodeWasPortedFrom()
        {
            Assert.Equal("2.14.3", Golden.Value.FreeType.Version);
            Assert.Equal("VER-2-14-3", Golden.Value.FreeType.Tag);
        }

        [Fact]
        public void TheReferenceIsNotVacuous()
        {
            // faults FreeType refuses to set a location for and faults it lives with, glyphs it loads and glyphs it refuses
            int cannotSet = Golden.Value.Variants.SelectMany(v => v.Runs).Count(r => r.Set != 0);
            int canSet = Golden.Value.Variants.SelectMany(v => v.Runs).Count(r => r.Set == 0);
            Assert.True(cannotSet > 25 && canSet > 100, $"{cannotSet} locations of the variants cannot be set and {canSet} can");

            int refusedGlyphs = Golden.Value.Variants.SelectMany(v => v.Runs).Where(r => r.Glyphs is not null).SelectMany(r => r.Glyphs!.Values).Count(d => d.Length != 16);
            int loadedGlyphs = Golden.Value.Variants.SelectMany(v => v.Runs).Where(r => r.Glyphs is not null).SelectMany(r => r.Glyphs!.Values).Count(d => d.Length == 16);
            Assert.True(refusedGlyphs > 20 && loadedGlyphs > 2000, $"{refusedGlyphs} glyphs of the variants are refused and {loadedGlyphs} load");

            Assert.Equal(600, Golden.Value.Mutants.Count);
            Assert.True(Golden.Value.Mutants.SelectMany(m => m.Runs).Count(r => r.Set != 0) > 60 && Golden.Value.Mutants.SelectMany(m => m.Runs).Count(r => r.Set == 0) > 800,
                "the mutants are all refused, or none is");

            // the variants that FreeType lives with draw differently from the base font, so the digests tell them apart
            var baseline = Golden.Value.Variants.Single(v => v.Name == "baseline");
            Assert.True(Golden.Value.Variants.Count(v => v.Runs.Zip(baseline.Runs).Any(p => p.First.Set == 0 && p.Second.Set == 0 && p.First.Glyphs != null && !p.First.Glyphs.SequenceEqual(p.Second.Glyphs!))) > 20);
        }

        [Theory]
        [MemberData(nameof(Variants))]
        public void EveryVariantIsAnsweredAsFreeTypeAnswersIt(int index)
        {
            var variant = Golden.Value.Variants[index];
            var font = variant.Name == "baseline" ? MainFont.Value : WithTable(MainFont.Value, variant.Table, variant.Data is null ? null : Convert.FromBase64String(variant.Data));

            var problems = Compare(font, variant);
            Assert.True(problems.Count == 0, $"{variant.Name}: {problems.Count} difference(s)\n" + string.Join("\n", problems.Take(8)));
        }

        [Fact]
        public void EveryMutantIsAnsweredAsFreeTypeAnswersIt()
        {
            var failures = new List<string>();
            for (int i = 0; i < Golden.Value.Mutants.Count; i++)
            {
                var mutant = Golden.Value.Mutants[i];
                var table = (byte[])Table(MainFont.Value, mutant.Table).Clone();
                foreach (var edit in mutant.Edits)
                    table[edit[0]] = (byte)edit[1];

                var problems = Compare(WithTable(MainFont.Value, mutant.Table, table), mutant);
                if (problems.Count != 0)
                    failures.Add($"mutant {i} ({mutant.Table}, edits {string.Join(", ", mutant.Edits.Select(e => $"[{e[0]}]={e[1]}"))}): {problems[0]}");
            }

            Assert.True(failures.Count == 0, $"{failures.Count} of {Golden.Value.Mutants.Count} mutants differ:\n" + string.Join("\n", failures.Take(15)));
        }

        /// <summary>What the port does differently from FreeType with a font, in words; nothing when the two agree.</summary>
        private static List<string> Compare(byte[] font, VariantBlobGolden reference)
        {
            var problems = new List<string>();

            // not through a font set: its cache of font files is keyed by a checksum that fonts differing by a byte or two can share
            OpenTypeFontface fontface;
            try
            {
                fontface = new OpenTypeFontface(FontFileData.CreateCompiledFont(font));
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or ArgumentException)
            {
                if (reference.Error == 0)
                    problems.Add("the package cannot read a font FreeType opens: " + ex.Message);
                return problems;
            }

            if (reference.Error != 0)
            {
                problems.Add($"FreeType refuses the font with {reference.Error}");
                return problems;
            }

            var tables = TtVarTables.For(fontface);

            for (int r = 0; r < reference.Runs.Count; r++)
            {
                var run = reference.Runs[r];
                var location = Golden.Value.Locations[r];
                string label = location.Design is not null ? "design " + string.Join(",", location.Design) : location.Blend is not null ? "raw " + string.Join(",", location.Blend) : "as it opens";

                // the location the port makes: normalized from the design coordinates (or the raw ones), or none
                int[]? ndv = null;
                bool cannotSet = false;
                if (location.Design is not null || location.Blend is not null)
                {
                    if (tables is null)
                    {
                        cannotSet = true;
                    }
                    else if (location.Design is not null)
                    {
                        var design = new int[tables.NumAxis];
                        for (int i = 0; i < design.Length && i < location.Design.Length; i++)
                            design[i] = (int)Math.Round(location.Design[i] * 65536);
                        ndv = tables.Normalize(design);
                    }
                    else
                    {
                        ndv = new int[tables.NumAxis];
                        for (int i = 0; i < ndv.Length && i < location.Blend!.Length; i++)
                            ndv[i] = location.Blend[i];
                    }
                }

                TtFace? face = null;
                if (!cannotSet)
                {
                    face = TtFace.TryCreate(fontface, fontface.name?.Name, ndv);
                    cannotSet = face is null;
                }

                if (run.Set != 0)
                {
                    if (!cannotSet)
                        problems.Add($"{label}: FreeType cannot set the location ({run.Set}) and the port can");
                    continue;
                }

                if (cannotSet)
                {
                    problems.Add($"{label}: FreeType sets the location and the port refuses it");
                    continue;
                }

                if (run.Ndv is not null && ndv is not null && !ndv.SequenceEqual(run.Ndv))
                    problems.Add($"{label}: the normalized vector is [{string.Join(",", ndv)}], FreeType has [{string.Join(",", run.Ndv)}]");

                // the ranges of gasp at the location
                var gasp = TtGasp.TryRead(fontface)?.AtLocation(face!.Blend);
                for (int ppem = 0; run.Gasp is not null && ppem < run.Gasp.Length; ppem++)
                {
                    int flags = gasp?.GetFlags(ppem) ?? TtGasp.NoTable;
                    if (flags != run.Gasp[ppem])
                    {
                        problems.Add($"{label}: gasp at {ppem} ppem is {flags}, FreeType has {run.Gasp[ppem]}");
                        break;
                    }
                }

                TtSize size;
                try
                {
                    size = TtSize.Create(face!, 16 * 64, TtInterpreterVersion.V40, TtRenderMode.Normal);
                }
                catch (HintingException ex)
                {
                    problems.Add($"{label}: the port cannot make the size ({ex.Message})");
                    continue;
                }

                foreach (var (glyphText, expected) in run.Glyphs!)
                {
                    int glyph = int.Parse(glyphText);
                    string actual;
                    try
                    {
                        actual = Digest(TtGlyphLoader.Load(size, glyph));
                    }
                    catch (HintingException)
                    {
                        actual = "e";
                    }

                    // an error of FreeType's is "e" and its code, and a digest is 16 hex digits; for a glyph FreeType refuses, only that the port refuses too is compared (its codes are its own)
                    bool refuses = expected.Length != 16;
                    if (refuses ? actual != "e" : actual != expected)
                        problems.Add($"{label}: glyph {glyph}: " + (actual == "e" ? "the port refuses it and FreeType loads it" : refuses ? "FreeType refuses it and the port loads it" : "the points differ"));
                }
            }

            return problems;
        }

        /// <summary>A 64-bit FNV-1a digest of what FreeType makes of a glyph, as <c>generate_hinting_variable_fixtures.py</c> computes it.</summary>
        internal static string Digest(TtHintedGlyph glyph)
        {
            ulong h = 0xCBF29CE484222325UL;

            void Add(int value)
            {
                Span<byte> bytes = stackalloc byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
                foreach (byte b in bytes)
                    h = (h ^ b) * 0x100000001B3UL;
            }

            Add(glyph.Advance);
            Add(glyph.ContourEnds.Length);
            foreach (int end in glyph.ContourEnds)
                Add(end);
            Add(glyph.NPoints);
            for (int i = 0; i < glyph.NPoints; i++)
                Add(glyph.X[i]);
            for (int i = 0; i < glyph.NPoints; i++)
                Add(glyph.Y[i]);
            for (int i = 0; i < glyph.NPoints; i++)
                Add(glyph.Tags[i] & 1);

            return h.ToString("x16");
        }

        // ---- the font file -----------------------------------------------------------------------------------------------------

        private static byte[] Table(byte[] font, string tag)
        {
            foreach (var (name, data) in Tables(font))
            {
                if (name == tag)
                    return data;
            }

            throw new InvalidOperationException("no " + tag + " table");
        }

        private static List<(string Tag, byte[] Data)> Tables(byte[] font)
        {
            int count = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
            var tables = new List<(string, byte[])>();
            for (int i = 0; i < count; i++)
            {
                var record = font.AsSpan(12 + i * 16);
                int offset = (int)BinaryPrimitives.ReadUInt32BigEndian(record[8..]);
                int length = (int)BinaryPrimitives.ReadUInt32BigEndian(record[12..]);
                tables.Add((Encoding.ASCII.GetString(record[..4]), font.AsSpan(offset, length).ToArray()));
            }

            return tables;
        }

        /// <summary>The font with a table replaced, or (with null) left out.</summary>
        private static byte[] WithTable(byte[] font, string tag, byte[]? table)
        {
            var tables = Tables(font).Where(t => t.Tag != tag).ToList();
            if (table is not null)
                tables.Add((tag, table));

            tables.Sort((a, b) => string.CompareOrdinal(a.Tag, b.Tag));

            using var stream = new MemoryStream();
            var header = new byte[12 + 16 * tables.Count];
            font.AsSpan(0, 4).CopyTo(header);
            BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4), (ushort)tables.Count);

            int offset = header.Length;
            for (int i = 0; i < tables.Count; i++)
            {
                var record = header.AsSpan(12 + i * 16);
                Encoding.ASCII.GetBytes(tables[i].Tag, record);
                BinaryPrimitives.WriteUInt32BigEndian(record[8..], (uint)offset);
                BinaryPrimitives.WriteUInt32BigEndian(record[12..], (uint)tables[i].Data.Length);
                offset += (tables[i].Data.Length + 3) & ~3;
            }

            stream.Write(header);
            foreach (var (_, data) in tables)
            {
                stream.Write(data);
                stream.Write(new byte[((data.Length + 3) & ~3) - data.Length]);
            }

            return stream.ToArray();
        }
    }

    internal class VariantBlobGolden
    {
        [JsonPropertyName("error")]
        public int Error { get; set; }

        [JsonPropertyName("axes")]
        public int Axes { get; set; }

        [JsonPropertyName("runs")]
        public List<VariantRunGolden> Runs { get; set; } = [];
    }

    internal sealed class VariantRunGolden
    {
        [JsonPropertyName("set")]
        public int Set { get; set; }

        [JsonPropertyName("ndv")]
        public int[]? Ndv { get; set; }

        [JsonPropertyName("gasp")]
        public int[]? Gasp { get; set; }

        [JsonPropertyName("glyphs")]
        public Dictionary<string, string>? Glyphs { get; set; }
    }

    internal sealed class VariantLocationGolden
    {
        [JsonPropertyName("design")]
        public double[]? Design { get; set; }

        [JsonPropertyName("blend")]
        public int[]? Blend { get; set; }
    }

    internal sealed class VariableVariantGolden : VariantBlobGolden
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("table")]
        public string Table { get; set; } = "";

        [JsonPropertyName("data")]
        public string? Data { get; set; }
    }

    internal sealed class VariableMutantGolden : VariantBlobGolden
    {
        [JsonPropertyName("table")]
        public string Table { get; set; } = "";

        [JsonPropertyName("edits")]
        public List<int[]> Edits { get; set; } = [];
    }

    internal sealed class VariableVariantFile
    {
        [JsonPropertyName("freetype")]
        public FreeTypeInfo FreeType { get; set; } = new();

        [JsonPropertyName("locations")]
        public List<VariantLocationGolden> Locations { get; set; } = [];

        [JsonPropertyName("variants")]
        public List<VariableVariantGolden> Variants { get; set; } = [];

        [JsonPropertyName("mutants")]
        public List<VariableMutantGolden> Mutants { get; set; } = [];
    }
}
