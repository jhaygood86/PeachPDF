using System.Collections.Concurrent;
using System.Diagnostics;
using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using PeachDrawing.PDF.PdfiumRunner;

return await Cli.RunAsync(args);

namespace PeachDrawing.PDF.PdfiumRunner
{
    internal static class Cli
    {
        private const string Usage = """
            PeachDrawing.PDF vs PDFium pixel-test runner

              fetch [--cache <dir>]
                  Downloads PDFium's testing/resources at the pinned commit (pdfium/PDFIUM_COMMIT) into <cache>/pdfium/<commit>/ (default ~/.peachdrawing-pdfium).
              run --corpus <dir> [options]
                  --filter <text>      only tests whose id contains <text>
                  --test <id>          one test (for example pixel/bug_1012369), implies --dump
                  --sample <n>         every nth test in id order
                  --parallel <n>       concurrent tests (default: processor count)
                  --timeout <seconds>  per-test limit (default 60)
                  --threshold <n>      a pixel differs when a channel is off by more than n (default 48)
                  --max-diff <f>       a page passes when at most this fraction of its pixels differ (default 0.02)
                  --out <dir>          write <dir>/<commit>/pixel.json
                  --dump <dir>         write both renderings of every page compared
                  --floors <file>      fail (exit 1) when a full run has fewer passes than the committed floor
                  --update-floors      raise the floor in --floors to this run's pass count (never lowers; --allow-lower overrides)
            """;

        public static async Task<int> RunAsync(string[] args)
        {
            if (args.Length == 0 || args[0] is "-h" or "--help")
            {
                Console.WriteLine(Usage);
                return args.Length == 0 ? 2 : 0;
            }

            var options = ParseOptions(args.Skip(1));
            try
            {
                return args[0] switch
                {
                    "fetch" => await FetchAsync(options),
                    "run" => await RunAsync(options),
                    _ => Fail($"unknown command '{args[0]}'"),
                };
            }
            catch (Exception e) when (e is ArgumentException or FileNotFoundException or DirectoryNotFoundException)
            {
                return Fail(e.Message);
            }
        }

        private static int Fail(string message)
        {
            Console.Error.WriteLine(message);
            Console.Error.WriteLine(Usage);
            return 2;
        }

        internal static Dictionary<string, string> ParseOptions(IEnumerable<string> args)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            string? key = null;
            foreach (var a in args)
            {
                if (a.StartsWith("--", StringComparison.Ordinal))
                {
                    if (key is not null)
                        result[key] = "true";
                    key = a[2..];
                }
                else if (key is not null)
                {
                    result[key] = a;
                    key = null;
                }
            }

            if (key is not null)
                result[key] = "true";
            return result;
        }

        private static string PinnedCommit() =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "PDFIUM_COMMIT")).Trim();

        private static async Task<int> FetchAsync(Dictionary<string, string> o)
        {
            var commit = PinnedCommit();
            var cache = o.GetValueOrDefault("cache") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".peachdrawing-pdfium");
            var dest = Path.Combine(cache, "pdfium", commit);
            if (File.Exists(Path.Combine(dest, ".complete")))
            {
                Console.WriteLine(dest);
                return 0;
            }

            var partial = dest + ".partial";
            if (Directory.Exists(partial))
                Directory.Delete(partial, recursive: true);
            Directory.CreateDirectory(partial);

            const string wanted = "testing/resources/";
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
            await using var download = await http.GetStreamAsync($"https://codeload.github.com/chromium/pdfium/tar.gz/{commit}");
            await using var gzip = new GZipStream(download, CompressionMode.Decompress);
            using var tar = new TarReader(gzip);

            var root = Path.GetFullPath(partial);
            while (await tar.GetNextEntryAsync() is { } entry)
            {
                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
                    continue;

                var slash = entry.Name.IndexOf('/');
                var relative = slash < 0 ? entry.Name : entry.Name[(slash + 1)..];
                if (!relative.StartsWith(wanted, StringComparison.Ordinal) || entry.DataStream is null)
                    continue;

                var target = Path.GetFullPath(Path.Combine(root, relative));
                if (!target.StartsWith(root, StringComparison.Ordinal))
                    continue; // never write outside the checkout
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var file = File.Create(target);
                await entry.DataStream.CopyToAsync(file);
            }

            await File.WriteAllTextAsync(Path.Combine(partial, ".complete"), string.Empty);
            if (Directory.Exists(dest))
                Directory.Delete(dest, recursive: true);
            Directory.Move(partial, dest);
            Console.WriteLine(dest);
            return 0;
        }

        private static async Task<int> RunAsync(Dictionary<string, string> o)
        {
            var corpus = o.GetValueOrDefault("corpus") ?? throw new ArgumentException("--corpus <dir> is required (run 'fetch' first).");
            var commit = PinnedCommit();
            var parallel = int.Parse(o.GetValueOrDefault("parallel") ?? Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            var timeout = TimeSpan.FromSeconds(int.Parse(o.GetValueOrDefault("timeout") ?? "60", CultureInfo.InvariantCulture));
            var stride = int.Parse(o.GetValueOrDefault("sample") ?? "1", CultureInfo.InvariantCulture);
            var filter = o.GetValueOrDefault("filter");
            var single = o.GetValueOrDefault("test");
            var dump = o.GetValueOrDefault("dump") ?? (single is null ? "" : "dumps");
            var floorsFile = o.GetValueOrDefault("floors");
            var tolerance = new Tolerance(
                int.Parse(o.GetValueOrDefault("threshold") ?? "48", CultureInfo.InvariantCulture),
                double.Parse(o.GetValueOrDefault("max-diff") ?? "0.02", CultureInfo.InvariantCulture));

            var tests = Corpus.Discover(corpus)
                .Where(t => single is null || t.Id == single)
                .Where(t => filter is null || t.Id.Contains(filter, StringComparison.Ordinal))
                .Where((_, i) => i % stride == 0)
                .ToList();
            if (single is not null && tests.Count == 0)
                throw new ArgumentException("No such test.");

            var evaluator = new Evaluator(tolerance, dump);
            var results = new ConcurrentDictionary<string, TestResult>();
            var clock = Stopwatch.StartNew();
            await Parallel.ForEachAsync(tests, new ParallelOptions { MaxDegreeOfParallelism = parallel }, async (test, _) =>
            {
                results[test.Id] = await EvaluateWithTimeoutAsync(evaluator, test, timeout);
            });

            var result = GroupResult.From(commit, Corpus.Group, results);
            Console.WriteLine($"{result.Group,-8} total {result.Total,5}  pass {result.PassCount,5}  fail {result.Fail.Count,5}  blank {result.Blank.Count,4}  skipped {result.Skipped.Count,4}  rate {result.Rate}  ({clock.Elapsed.TotalSeconds:0.0}s)");
            if (single is not null)
            {
                foreach (var (id, r) in results)
                    Console.WriteLine($"  {id}: {r.Outcome} {r.Reason}");
            }

            if (o.GetValueOrDefault("out") is { } outDir)
                result.Write(outDir);

            var gateable = single is null && filter is null && stride == 1;
            var regressions = 0;
            if (gateable && floorsFile is not null)
            {
                var floors = Floors.Load(floorsFile);
                floors.Groups.TryGetValue(result.Group, out var floor);
                if (result.PassCount < floor)
                {
                    regressions++;
                    Console.Error.WriteLine($"REGRESSION: {result.Group} passes {result.PassCount}, below its floor of {floor}.");
                }
                else if (result.PassCount > floor && !o.ContainsKey("update-floors"))
                {
                    Console.WriteLine($"  note: {result.Group} passes {result.PassCount} above its floor of {floor}; raise it with --update-floors.");
                }

                if (o.ContainsKey("update-floors") && (result.PassCount > floor || o.ContainsKey("allow-lower")))
                {
                    floors.Groups[result.Group] = result.PassCount;
                    floors.PdfiumCommit = commit;
                    floors.Save(floorsFile);
                    Console.WriteLine($"floors written to {floorsFile}");
                }
            }

            return regressions == 0 ? 0 : 1;
        }

        private static async Task<TestResult> EvaluateWithTimeoutAsync(Evaluator evaluator, CorpusTest test, TimeSpan timeout)
        {
            var work = Task.Run(() => evaluator.Evaluate(test));
            if (await Task.WhenAny(work, Task.Delay(timeout)) == work)
                return await work;
            return new TestResult(Outcome.Fail, $"error: timeout after {timeout.TotalSeconds:0}s");
        }
    }
}
