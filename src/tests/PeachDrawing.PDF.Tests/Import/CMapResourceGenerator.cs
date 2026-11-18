using PeachDrawing.PDF.Import;

namespace PeachDrawing.PDF.Tests.Import
{
    /// <summary>
    /// Compiles Adobe's CMap resources (https://github.com/adobe-type-tools/cmap-resources, BSD-3-Clause) into the resource
    /// <c>src/drawing/PeachDrawing.PDF/Import/CMaps/PdfCMaps.bin</c>. It does nothing unless PEACH_REGENERATE_CMAPS names a folder of the
    /// CMap files (the predefined CMaps of ISO 32000-1 table 118, each as a file named for the CMap); run it from src/ with
    /// <c>PEACH_REGENERATE_CMAPS=C:\folder dotnet test --project tests/PeachDrawing.PDF.Tests/PeachDrawing.PDF.Tests.csproj --framework net10.0 --filter-method "*Regenerate*"</c>.
    /// </summary>
    public class CMapResourceGenerator
    {
        private static readonly (string Ordering, string Unicode)[] Collections =
        [
            ("Japan1", "UniJIS-UTF16-H"), ("GB1", "UniGB-UTF16-H"), ("CNS1", "UniCNS-UTF16-H"), ("Korea1", "UniKS-UTF16-H"),
        ];

        /// <summary>Which of several characters one CID maps to is the text: the ordinary character before a compatibility ideograph or a Kangxi radical, then the lowest.</summary>
        private static int Rank(int cp) => cp is >= 0x2E80 and <= 0x2FDF or >= 0xF900 and <= 0xFAFF or >= 0x2F800 and <= 0x2FA1F ? 1 : 0;

        [Fact]
        public void Regenerate()
        {
            string? folder = Environment.GetEnvironmentVariable("PEACH_REGENERATE_CMAPS");
            if (string.IsNullOrEmpty(folder)) return;

            var parsed = new Dictionary<string, PdfCMap>();
            PdfCMap? Load(string name)
            {
                if (parsed.TryGetValue(name, out var done)) return done;
                string path = Path.Combine(folder, name);
                if (!File.Exists(path)) return null;
                return parsed[name] = PdfCMap.Parse(File.ReadAllBytes(path), name, Load);
            }

            var names = Directory.GetFiles(folder).Select(Path.GetFileName).Where(n => n is not null && !n.Contains('.')).Order().ToList();
            foreach (var name in names) Load(name!);

            var unicode = new Dictionary<string, IReadOnlyList<(int Cid, int CodePoint)>>();
            foreach (var (ordering, source) in Collections)
            {
                var map = parsed[source];
                var lowest = new Dictionary<int, int>();
                foreach (int length in new[] { 2, 4 })
                    foreach (var range in map.RangesOfLength(length))
                        for (uint code = range.Low; code <= range.High; code++)
                        {
                            int cp = length == 2 ? (int)code : 0x10000 + ((((int)(code >> 16)) - 0xD800) << 10) + (((int)(code & 0xFFFF)) - 0xDC00);
                            if (cp is >= 0xD800 and <= 0xDFFF || cp > 0x10FFFF) continue;
                            int cid = range.Cid + (int)(code - range.Low);
                            if (!lowest.TryGetValue(cid, out int seen) || Rank(cp) < Rank(seen) || (Rank(cp) == Rank(seen) && cp < seen)) lowest[cid] = cp;
                        }

                unicode[ordering] = lowest.Select(kv => (kv.Key, kv.Value)).ToList();
            }

            var bytes = PdfPredefinedCMaps.Compile([.. parsed.Values.OrderBy(m => m.Name, StringComparer.Ordinal)], unicode);
            string target = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "drawing", "PeachDrawing.PDF", "Import", "CMaps", "PdfCMaps.bin"));
            File.WriteAllBytes(target, bytes);
            Console.WriteLine($"Wrote {parsed.Count} CMaps and {unicode.Count} Unicode tables, {bytes.Length} bytes, to {target}");
        }
    }
}
