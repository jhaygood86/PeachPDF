namespace PeachBrackets.WptRunner;

/// <summary>What the evaluator needs from a renderer: where a WPT-relative path lives on disk, and its pixels.</summary>
internal interface IReftestRenderer
{
    string FullPath(string wptPath);

    Task<RgbaImage> RenderAsync(string wptPath);
}

internal enum Outcome { Pass, Fail, Blank, Skipped }

internal readonly record struct TestResult(Outcome Outcome, string? Reason = null);

/// <summary>Applies the WPT reftest rules to one test: at least one <c>match</c> reference (and its chain) must be equal within the fuzzy allowance and every <c>mismatch</c> reference must differ.</summary>
internal sealed class ReftestEvaluator(IReftestRenderer renderer, string dumpDirectory = "")
{
    private readonly Dictionary<string, ReftestInfo> _infos = [];

    public async Task<TestResult> EvaluateAsync(string wptPath)
    {
        try
        {
            var info = Info(wptPath);
            if (info.SkipReason is not null)
                return new TestResult(Outcome.Skipped, info.SkipReason);
            if (info.Links.Count == 0)
                return new TestResult(Outcome.Skipped, "no reference");

            // A reference that is missing or needs script makes the whole test unrunnable.
            foreach (var link in info.Links)
            {
                if (!File.Exists(renderer.FullPath(link.Path)))
                    return new TestResult(Outcome.Skipped, $"reference file missing: {link.Path}");
                if (Info(link.Path).SkipReason is { } why)
                    return new TestResult(Outcome.Skipped, why);
            }

            var test = await renderer.RenderAsync(wptPath);
            Dump(wptPath, test);

            var matches = info.Links.Where(l => l.Relation == ReftestRelation.Match).ToList();
            var mismatches = info.Links.Where(l => l.Relation == ReftestRelation.Mismatch).ToList();

            string? failure = null;
            foreach (var link in matches)
            {
                failure = await CheckMatchAsync(info, test, link.Path, [wptPath]);
                if (failure is null)
                    break;
            }

            if (failure is not null)
                return new TestResult(Outcome.Fail, failure);

            foreach (var link in mismatches)
            {
                var reference = await renderer.RenderAsync(link.Path);
                if (test.Compare(reference) is { } d && FuzzyFor(info, link.Path).Allows(d.MaxDifference, d.DifferingPixels))
                    return new TestResult(Outcome.Fail, $"!= {Name(link.Path)} identical");
            }

            return test.IsBlank() ? new TestResult(Outcome.Blank) : new TestResult(Outcome.Pass);
        }
        catch (Exception e)
        {
            // Name the root cause, not the engine's wrapper: the wrapper's message alone says nothing about what to fix.
            var root = e;
            while (root.InnerException is { } inner)
                root = inner;
            var cause = root == e ? string.Empty : $" <- {root.GetType().Name}: {root.Message}";
            return new TestResult(Outcome.Fail, $"error: {e.GetType().Name}: {e.Message}{cause}".ReplaceLineEndings(" "));
        }
    }

    private async Task<string?> CheckMatchAsync(ReftestInfo owner, RgbaImage image, string referencePath, HashSet<string> chain)
    {
        var reference = await renderer.RenderAsync(referencePath);
        Dump(referencePath, reference);

        var fuzzy = FuzzyFor(owner, referencePath);
        if (image.Compare(reference) is not { } d)
            return $"== {Name(referencePath)} differs (size {image.Width}x{image.Height} vs {reference.Width}x{reference.Height})";
        if (!fuzzy.Allows(d.MaxDifference, d.DifferingPixels))
            return $"== {Name(referencePath)} differs (max {d.MaxDifference}, {d.DifferingPixels} px)";

        // The reference's own match links continue the chain; one that holds is enough, and one already being checked counts as holding.
        var next = Info(referencePath).Links.Where(l => l.Relation == ReftestRelation.Match && !chain.Contains(l.Path)).ToList();
        if (next.Count == 0)
            return null;

        string? last = null;
        foreach (var link in next)
        {
            if (!File.Exists(renderer.FullPath(link.Path)))
                continue;
            last = await CheckMatchAsync(Info(referencePath), reference, link.Path, [.. chain, referencePath]);
            if (last is null)
                return null;
        }

        return last;
    }

    private static Fuzzy FuzzyFor(ReftestInfo owner, string referencePath) =>
        owner.Fuzzies.TryGetValue(referencePath, out var f) ? f
        : owner.Fuzzies.TryGetValue(string.Empty, out var all) ? all
        : Fuzzy.None;

    private ReftestInfo Info(string path)
    {
        lock (_infos)
        {
            if (!_infos.TryGetValue(path, out var info))
            {
                info = ReftestParser.Parse(path, File.ReadAllText(renderer.FullPath(path)));
                _infos[path] = info;
            }

            return info;
        }
    }

    private static string Name(string path) => path[(path.LastIndexOf('/') + 1)..];

    private void Dump(string path, RgbaImage image)
    {
        if (dumpDirectory.Length == 0)
            return;
        Directory.CreateDirectory(dumpDirectory);
        File.WriteAllBytes(Path.Combine(dumpDirectory, path.Replace('/', '_') + ".png"), PngWriter.Encode(image));
    }
}
