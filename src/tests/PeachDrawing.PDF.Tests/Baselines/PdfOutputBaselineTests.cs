using System.Security.Cryptography;
using System.Text;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachDrawing.PDF.Engine.Internal;
using PeachPDF.Tests.TestSupport;

namespace PeachDrawing.PDF.Tests.Baselines
{
    /// <summary>
    /// Pins the exact bytes the writer produces for a fixed corpus of documents, so a refactor of its internals that is meant to
    /// change nothing can prove it. The randomness and the clock are replaced by <see cref="PdfEntropy"/> so the bytes are
    /// reproducible. Regenerate with <c>PEACH_UPDATE_BASELINE=1</c> and review the diff: a changed hash is a change in what the
    /// library writes and has to be explained, never regenerated blindly.
    /// </summary>
    public class PdfOutputBaselineTests
    {
        private static readonly PaintColor Black = PaintColor.FromArgb(255, 0, 0, 0);
        private static readonly PaintColor Red = PaintColor.FromArgb(255, 255, 0, 0);
        private static readonly PaintColor Blue = PaintColor.FromArgb(255, 0, 0, 255);
        private static readonly DateTimeOffset FixedDate = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

        /// <summary>Counter-mode SHA-256 as "randomness" and a fixed instant as the clock.</summary>
        private sealed class FixedSource : PdfEntropy.Source
        {
            private int _counter;

            public override byte[] GetBytes(int count)
            {
                var result = new byte[count];
                for (var filled = 0; filled < count;)
                {
                    var block = SHA256.HashData(BitConverter.GetBytes(_counter++));
                    var take = Math.Min(block.Length, count - filled);
                    Array.Copy(block, 0, result, filled, take);
                    filled += take;
                }

                return result;
            }

            public override DateTime Now() => new(2026, 1, 2, 3, 4, 5, DateTimeKind.Local);
        }

        private static PdfComposerOptions Options(Action<PdfComposerOptions>? configure = null)
        {
            var options = new PdfComposerOptions
            {
                CompressContentStreams = false,
                CreationDate = FixedDate,
                Title = "Baseline",
                Producer = "Baseline",
                Creator = "Baseline",
            };
            configure?.Invoke(options);
            return options;
        }

        private static async Task<(PdfRenderContext Context, Font Font)> NewContextAsync()
        {
            var context = new PdfRenderContext();
            await using (var stream = File.OpenRead(BundledFonts.Ttf))
            {
                await context.AddFont(stream, "BaselineFont");
            }

            return (context, context.GetFont("BaselineFont", 18, PaintFontStyle.Regular)!);
        }

        private static async Task<byte[]> BuildAsync(PdfComposerOptions options, Action<PdfComposer>? extra = null)
        {
            var (context, font) = await NewContextAsync();
            using var composer = new PdfComposer(context, options);

            for (var i = 0; i < 2; i++)
            {
                var canvas = composer.AddPage(595, 842).Canvas;
                canvas.DrawRectangle(canvas.GetSolidBrush(Red), 50, 60, 200, 100);
                canvas.DrawRectangle(canvas.GetSolidBrush(Blue), 80, 90, 120, 40);
                canvas.DrawString("Hello PDF " + i, font, Black, new PaintPoint(50, 200), new Size(300, 30));
                canvas.AddLink(new Rect(50, 200, 100, 20), "https://example.com/" + i);
            }

            extra?.Invoke(composer);
            using var output = new MemoryStream();
            composer.Save(output);
            return output.ToArray();
        }

        private static IEnumerable<(string Name, Func<Task<byte[]>> Build)> Corpus()
        {
            var encryption = () => new PdfEncryptionOptions { UserPassword = "user", OwnerPassword = "owner" };

            yield return ("plain", () => BuildAsync(Options()));
            yield return ("compressed", () => BuildAsync(Options(o => o.CompressContentStreams = true)));
            yield return ("linearized", () => BuildAsync(Options(o => o.Linearize = true)));
            yield return ("xmp", () => BuildAsync(Options(o => o.WriteXmpMetadata = true)));
            yield return ("pdf20", () => BuildAsync(Options(o => o.Version = PdfVersion.Pdf20)));
            yield return ("pdfa1b", () => BuildAsync(Options(o => { o.Conformance = PdfAConformance.PdfA1B; o.WriteXmpMetadata = true; })));
            yield return ("pdfa2b", () => BuildAsync(Options(o => { o.Conformance = PdfAConformance.PdfA2B; o.WriteXmpMetadata = true; })));
            yield return ("encrypted-aes256", () => BuildAsync(Options(o => o.Encryption = encryption())));
            yield return ("encrypted-linearized", () => BuildAsync(Options(o => { o.Linearize = true; o.Encryption = encryption(); })));
            yield return ("bookmarks", () => BuildAsync(Options(), composer => composer.AddBookmarks(
            [
                new PdfBookmark(1, "Chapter", true) { PageIndex = 0, TopPoints = 10 },
                new PdfBookmark(2, "Section", false) { PageIndex = 1, TopPoints = 50 },
            ])));
            yield return ("named-destination", () => BuildAsync(Options(), composer => composer.AddNamedDestination("target", 1, 40)));
        }

        private static string BaselinePath(bool source) => source
            ? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Baselines", "PdfOutputBaseline.txt"))
            : Path.Combine(AppContext.BaseDirectory, "Baselines", "PdfOutputBaseline.txt");

        private static Dictionary<string, string> LoadBaseline()
        {
            var path = BaselinePath(false);
            if (!File.Exists(path))
                return [];
            return File.ReadAllLines(path)
                .Select(l => l.Split(' ', 2))
                .Where(p => p.Length == 2)
                .ToDictionary(p => p[0], p => p[1]);
        }

        [Fact]
        public async Task TheSameDocumentIsWrittenWithIdenticalBytesEveryTime()
        {
            byte[] first, second;
            using (PdfEntropy.Use(new FixedSource()))
                first = await BuildAsync(Options());
            using (PdfEntropy.Use(new FixedSource()))
                second = await BuildAsync(Options());

            Assert.Equal(first, second);
        }

        [Fact]
        public async Task EveryCorpusDocument_MatchesItsBaselineHash()
        {
            var actual = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var (name, build) in Corpus())
            {
                using (PdfEntropy.Use(new FixedSource()))
                    actual[name] = Convert.ToHexString(SHA256.HashData(await build()));
            }

            if (Environment.GetEnvironmentVariable("PEACH_UPDATE_BASELINE") == "1")
            {
                File.WriteAllText(BaselinePath(true), string.Concat(actual.Select(p => $"{p.Key} {p.Value}\n")), new UTF8Encoding(false));
                return;
            }

            var expected = LoadBaseline();
            var diffs = actual.Where(p => !expected.TryGetValue(p.Key, out var hash) || hash != p.Value)
                .Select(p => $"{p.Key} (expected {(expected.TryGetValue(p.Key, out var known) ? known[..12] : "none")}, wrote {p.Value[..12]})")
                .Concat(expected.Keys.Except(actual.Keys).Select(name => name + " (no longer in the corpus)")).ToList();
            Assert.True(diffs.Count == 0,
                "PDF output baseline drift in " + string.Join(", ", diffs) +
                ". A change in what the writer emits must be explained; documents that embed Flate-compressed streams (compressed, fonts, images) " +
                "can also differ when the runtime's zlib does, which regenerating on one machine would hide. Regenerate with PEACH_UPDATE_BASELINE=1 " +
                "only after checking which of the two it is.");
        }
    }
}
