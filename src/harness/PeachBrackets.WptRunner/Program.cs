using System.Collections.Concurrent;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using PeachBrackets.WptRunner;

return await Cli.RunAsync(args);

namespace PeachBrackets.WptRunner
{
    internal static class Cli
    {
        private const string Usage = """
            PeachBrackets WPT reftest runner

              fetch [--cache <dir>]
                  Downloads the pinned web-platform-tests commit (wpt/WPT_COMMIT) into <cache>/wpt/<commit>/ (default ~/.peachbrackets-wpt).
              run --wpt <dir> [options]
                  --module <id>        run one module (default: every module in modules.json)
                  --filter <text>      only tests whose path contains <text>
                  --test <path>        one test (WPT-relative path), implies --dump
                  --sample <n>         every nth test in path order
                  --parallel <n>       concurrent tests (default: processor count)
                  --timeout <seconds>  per-test limit (default 60)
                  --out <dir>          write <dir>/<commit>/<module>.json and summary.md
                  --dump <dir>         write PNGs of every image compared
                  --floors <file>      fail (exit 1) when a full module run has fewer passes than its committed floor
                  --update-floors      raise the floors in --floors to this run's pass counts (never lowers; --allow-lower overrides)
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
                    "run" => await RunModulesAsync(options),
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

        private static Dictionary<string, string> ParseOptions(IEnumerable<string> args)
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
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "WPT_COMMIT")).Trim();

        private static async Task<int> FetchAsync(Dictionary<string, string> o)
        {
            var commit = PinnedCommit();
            var cache = o.GetValueOrDefault("cache") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".peachbrackets-wpt");
            var dest = Path.Combine(cache, "wpt", commit);
            if (File.Exists(Path.Combine(dest, ".complete")))
            {
                Console.WriteLine(dest);
                return 0;
            }

            var partial = dest + ".partial";
            if (Directory.Exists(partial))
                Directory.Delete(partial, recursive: true);
            Directory.CreateDirectory(partial);

            string[] wanted = ["css/", "fonts/", "images/", "common/", "resources/"];
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
            await using var download = await http.GetStreamAsync($"https://codeload.github.com/web-platform-tests/wpt/tar.gz/{commit}");
            await using var gzip = new GZipStream(download, CompressionMode.Decompress);
            using var tar = new TarReader(gzip);

            var root = Path.GetFullPath(partial);
            while (await tar.GetNextEntryAsync() is { } entry)
            {
                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
                    continue;

                var slash = entry.Name.IndexOf('/');
                var relative = slash < 0 ? entry.Name : entry.Name[(slash + 1)..];
                if (!wanted.Any(w => relative.StartsWith(w, StringComparison.Ordinal)) || entry.DataStream is null)
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

        private static async Task<int> RunModulesAsync(Dictionary<string, string> o)
        {
            var wpt = o.GetValueOrDefault("wpt") ?? throw new ArgumentException("--wpt <dir> is required (run 'fetch' first).");
            var commit = PinnedCommit();
            var parallel = int.Parse(o.GetValueOrDefault("parallel") ?? Environment.ProcessorCount.ToString(), System.Globalization.CultureInfo.InvariantCulture);
            var timeout = TimeSpan.FromSeconds(int.Parse(o.GetValueOrDefault("timeout") ?? "60", System.Globalization.CultureInfo.InvariantCulture));
            var stride = int.Parse(o.GetValueOrDefault("sample") ?? "1", System.Globalization.CultureInfo.InvariantCulture);
            var filter = o.GetValueOrDefault("filter");
            var single = o.GetValueOrDefault("test");
            var dump = o.GetValueOrDefault("dump") ?? (single is null ? "" : "dumps");
            var outDir = o.GetValueOrDefault("out");
            var floorsFile = o.GetValueOrDefault("floors");

            var modules = Modules.Load(Path.Combine(AppContext.BaseDirectory, "modules.json"));
            if (o.GetValueOrDefault("module") is { } id)
                modules = [.. modules.Where(m => m.Id == id)];
            if (modules.Count == 0)
                throw new ArgumentException("No such module.");

            var renderer = new WptRenderer(wpt);
            var evaluator = new ReftestEvaluator(renderer, dump);
            var floors = floorsFile is null ? new Floors() : Floors.Load(floorsFile);
            var gateable = single is null && filter is null && stride == 1;
            var regressions = 0;
            var total = Stopwatch.StartNew();

            foreach (var module in modules)
            {
                var discovered = single is null ? Modules.Discover(wpt, module) : [single];
                var selected = discovered
                    .Where(p => filter is null || p.Contains(filter, StringComparison.Ordinal))
                    .Where((_, i) => i % stride == 0)
                    .ToList();

                var results = new ConcurrentDictionary<string, TestResult>();
                var clock = Stopwatch.StartNew();
                await Parallel.ForEachAsync(selected, new ParallelOptions { MaxDegreeOfParallelism = parallel }, async (path, _) =>
                {
                    results[path] = await EvaluateWithTimeoutAsync(evaluator, path, timeout);
                });

                var result = ModuleResult.From(commit, module.Id, discovered.Count, stride, results);
                Console.WriteLine($"{module.Id,-18} total {result.Counts.Total,5}  pass {result.Counts.Pass,5}  fail {result.Counts.Fail,5}  blank {result.Counts.Blank,4}  skipped {result.Counts.Skipped,5}  rate {result.Rate}  ({clock.Elapsed.TotalSeconds:0.0}s)");
                if (single is not null)
                {
                    foreach (var (p, r) in results)
                        Console.WriteLine($"  {p}: {r.Outcome} {r.Reason}");
                }

                if (outDir is not null)
                    ResultsWriter.Write(outDir, result);

                if (gateable && floorsFile is not null)
                {
                    floors.Modules.TryGetValue(module.Id, out var floor);
                    if (result.Counts.Pass < floor)
                    {
                        regressions++;
                        Console.Error.WriteLine($"REGRESSION: {module.Id} passes {result.Counts.Pass}, below its floor of {floor}.");
                    }
                    else if (result.Counts.Pass > floor && !o.ContainsKey("update-floors"))
                    {
                        Console.WriteLine($"  note: {module.Id} passes {result.Counts.Pass} above its floor of {floor}; raise it with --update-floors.");
                    }

                    if (o.ContainsKey("update-floors") && (result.Counts.Pass > floor || o.ContainsKey("allow-lower")))
                        floors.Modules[module.Id] = result.Counts.Pass;
                }
            }

            if (outDir is not null)
                ResultsWriter.WriteSummary(outDir, commit);

            if (floorsFile is not null && o.ContainsKey("update-floors") && gateable)
            {
                floors.WptCommit = commit;
                floors.Save(floorsFile);
                Console.WriteLine($"floors written to {floorsFile}");
            }

            Console.WriteLine($"done in {total.Elapsed.TotalSeconds:0.0}s");
            return regressions == 0 ? 0 : 1;
        }

        private static async Task<TestResult> EvaluateWithTimeoutAsync(ReftestEvaluator evaluator, string path, TimeSpan timeout)
        {
            var work = Task.Run(() => evaluator.EvaluateAsync(path));
            if (await Task.WhenAny(work, Task.Delay(timeout)) == work)
                return await work;
            return new TestResult(Outcome.Fail, $"error: timeout after {timeout.TotalSeconds:0}s");
        }
    }
}
