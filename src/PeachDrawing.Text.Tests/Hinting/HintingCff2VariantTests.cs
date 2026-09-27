using PeachDrawing.Text.Internal.Fonts;
using PeachDrawing.Text.Internal.Fonts.OpenType;
using PeachDrawing.Text.Internal.Hinting;
using PeachDrawing.Text.Internal.Hinting.FreeType;
using System.Buffers.Binary;
using System.Text.Json.Serialization;

namespace PeachDrawing.Text.Tests.Hinting
{
    /// <summary>
    /// CFF2 tables that are wrong in a deliberate way against FreeType 2.14.3 (<c>assets/fonts/generate_hinting_cff2_fixtures.py</c>): whatever
    /// FreeType does with one (refuse the face, refuse a glyph, draw it), the port does. The variants are faults of each part of the table (the
    /// header, the Top DICT, the INDEXes, the variation store, the FDArray, the FDSelect, the Private DICTs), the cases are charstrings that use
    /// <c>vsindex</c> and <c>blend</c> wrongly or stress the stack, and the mutants are the baseline with a few bytes changed at random.
    /// </summary>
    public class HintingCff2VariantTests
    {
        private static readonly Lazy<Cff2VariantFile> Golden = new(() => HintingGoldenData.Load<Cff2VariantFile>("HintingCff2Variants.golden.json.gz"));

        public static TheoryData<int> Variants()
        {
            var data = new TheoryData<int>();
            for (int i = 0; i < Golden.Value.Variants.Count; i++)
                data.Add(i);
            return data;
        }

        [Fact]
        public void TheGoldenDataWasMadeByTheFreeTypeTheEngineWasPortedFrom()
        {
            Assert.Equal("2.14.3", Golden.Value.FreeType.Version);
            Assert.Equal("VER-2-14-3", Golden.Value.FreeType.Tag);
        }

        [Fact]
        public void TheReferenceIsNotVacuous()
        {
            // faults that FreeType refuses and faults it lives with, glyphs it loads and glyphs it refuses
            int refused = Golden.Value.Variants.Count(v => v.Error != 0);
            int opened = Golden.Value.Variants.Count(v => v.Error == 0);
            Assert.True(refused > 40 && opened > 40, $"{refused} variants are refused and {opened} open");

            int glyphsLoaded = Golden.Value.Cases.SelectMany(c => c.Runs).SelectMany(r => r.Glyphs.Values).Count(g => g.Error == 0);
            int glyphsRefused = Golden.Value.Cases.SelectMany(c => c.Runs).SelectMany(r => r.Glyphs.Values).Count(g => g.Error != 0);
            Assert.True(glyphsLoaded > 60 && glyphsRefused > 30, $"{glyphsLoaded} glyphs of the cases load and {glyphsRefused} do not");

            Assert.Equal(400, Golden.Value.Mutants.Count);
            Assert.True(Golden.Value.Mutants.Count(m => m.Error != 0) > 50 && Golden.Value.Mutants.Count(m => m.Error == 0) > 100,
                "the mutants are all refused, or all opened");
        }

        [Theory]
        [MemberData(nameof(Variants))]
        public void EveryVariantIsAnsweredAsFreeTypeAnswersIt(int index)
        {
            var variant = Golden.Value.Variants[index];
            var problems = Compare(Convert.FromBase64String(variant.Font), variant);

            Assert.True(problems.Count == 0, $"{variant.Name}: {problems.Count} difference(s)\n" + string.Join("\n", problems.Take(8)));
        }

        [Fact]
        public void EveryCharstringCaseIsAnsweredAsFreeTypeAnswersIt()
        {
            foreach (var entry in Golden.Value.Cases)
            {
                var problems = Compare(Convert.FromBase64String(entry.Font), entry, entry.Names);
                Assert.True(problems.Count == 0, $"{entry.Name}: {problems.Count} difference(s)\n" + string.Join("\n", problems.Take(12)));
            }
        }

        [Fact]
        public void EveryMutantIsAnsweredAsFreeTypeAnswersIt()
        {
            var bases = Golden.Value.Variants.Concat(Golden.Value.Cases).ToDictionary(v => v.Name, v => Convert.FromBase64String(v.Font));

            var failures = new List<string>();
            for (int i = 0; i < Golden.Value.Mutants.Count; i++)
            {
                var mutant = Golden.Value.Mutants[i];
                var font = (byte[])bases[mutant.Base].Clone();
                int tableOffset = TableOffset(font, "CFF2");
                foreach (var edit in mutant.Edits)
                    font[tableOffset + edit[0]] = (byte)edit[1];

                var problems = Compare(font, mutant);
                if (problems.Count != 0)
                    failures.Add($"mutant {i} (edits {string.Join(", ", mutant.Edits.Select(e => $"[{e[0]}]={e[1]}"))}): {problems[0]}");
            }

            Assert.True(failures.Count == 0, $"{failures.Count} of {Golden.Value.Mutants.Count} mutants differ:\n" + string.Join("\n", failures.Take(15)));
        }

        internal static int TableOffset(byte[] font, string tag)
        {
            int tables = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
            for (int i = 0; i < tables; i++)
            {
                var record = font.AsSpan(12 + i * 16);
                if (System.Text.Encoding.ASCII.GetString(record[..4]) == tag)
                    return (int)BinaryPrimitives.ReadUInt32BigEndian(record[8..]);
            }

            throw new InvalidOperationException("no " + tag + " table");
        }

        // the reason the port gives for not hinting with a font, for the message of a test that fails
        private static string WhyRefused(OpenTypeFontface fontface, int[]? ndv)
        {
            var table = fontface.TableDictionary["CFF2"];
            try
            {
                CffFont.Load(fontface.FontSource.Bytes, table.Offset, fontface.head.unitsPerEm, true, ndv);
                return "(it loads: the face was refused for another reason)";
            }
            catch (HintingException ex)
            {
                return ex.Message;
            }
        }

        /// <summary>What the port does differently from FreeType with a font, in words; nothing when the two agree.</summary>
        private static List<string> Compare(byte[] font, Cff2BlobGolden reference, string[]? names = null)
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

            Func<int, int> advance = glyph => fontface.hmtx.Metrics[Math.Min(glyph, fontface.hhea.numberOfHMetrics - 1)].advanceWidth;

            if (reference.Error != 0)
            {
                // FreeType refuses the face: the port has no face to hint with (the location makes no difference)
                if (CffFace.TryCreate(fontface, advance, null) is not null)
                    problems.Add($"FreeType refuses the face with {reference.Error} and the port opens it");
                return problems;
            }

            foreach (var run in reference.Runs)
            {
                var face = CffFace.TryCreate(fontface, advance, run.Ndv);
                if (face is null)
                {
                    problems.Add("FreeType opens the face and the port refuses it: " + WhyRefused(fontface, run.Ndv));
                    break;
                }

                var size = new CffSize(face, run.Size);
                foreach (var (glyphText, expected) in run.Glyphs)
                {
                    int glyph = int.Parse(glyphText);
                    string label = (names is not null && glyph < names.Length ? names[glyph] : "glyph " + glyph) + $" at {(run.Ndv is null ? "the default" : string.Join(",", run.Ndv))}";

                    if (expected.Error != 0)
                    {
                        try
                        {
                            CffGlyphLoader.Load(size, glyph);
                            problems.Add($"{label}: FreeType fails with {expected.Error} and the port loads it");
                        }
                        catch (HintingException)
                        {
                        }

                        continue;
                    }

                    CffHintedGlyph actual;
                    try
                    {
                        actual = CffGlyphLoader.Load(size, glyph);
                    }
                    catch (HintingException ex)
                    {
                        problems.Add($"{label}: the port fails ({ex.Message}) and FreeType loads it");
                        continue;
                    }

                    var one = new List<string>();
                    HintingCffGoldenTests.Compare(one, glyph, expected, actual);
                    problems.AddRange(one.Select(p => label + ": " + p));
                }
            }

            return problems;
        }
    }

    internal class Cff2BlobGolden
    {
        [JsonPropertyName("error")]
        public int Error { get; set; }

        [JsonPropertyName("axes")]
        public int Axes { get; set; }

        [JsonPropertyName("runs")]
        public List<Cff2BlobRun> Runs { get; set; } = [];
    }

    internal sealed class Cff2BlobRun
    {
        [JsonPropertyName("ndv")]
        public int[]? Ndv { get; set; }

        [JsonPropertyName("size")]
        public int Size { get; set; }

        [JsonPropertyName("glyphs")]
        public Dictionary<string, GlyphGolden> Glyphs { get; set; } = [];
    }

    internal sealed class Cff2VariantGolden : Cff2BlobGolden
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("font")]
        public string Font { get; set; } = "";

        [JsonPropertyName("names")]
        public string[] Names { get; set; } = [];
    }

    internal sealed class Cff2MutantGolden : Cff2BlobGolden
    {
        [JsonPropertyName("base")]
        public string Base { get; set; } = "";

        [JsonPropertyName("edits")]
        public List<int[]> Edits { get; set; } = [];
    }

    internal sealed class Cff2VariantFile
    {
        [JsonPropertyName("freetype")]
        public FreeTypeInfo FreeType { get; set; } = new();

        [JsonPropertyName("cases")]
        public List<Cff2VariantGolden> Cases { get; set; } = [];

        [JsonPropertyName("variants")]
        public List<Cff2VariantGolden> Variants { get; set; } = [];

        [JsonPropertyName("mutants")]
        public List<Cff2MutantGolden> Mutants { get; set; } = [];
    }
}
