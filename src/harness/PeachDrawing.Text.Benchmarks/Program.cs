using BenchmarkDotNet.Running;
using PeachDrawing.Text;
using PeachDrawing.Text.Benchmarks;
using PeachDrawing.Text.Outlines;

// Run from src/ (a Release build; BenchmarkDotNet refuses a Debug one):
//
//   dotnet run -c Release --project PeachDrawing.Text.Benchmarks -- --filter "*HintingCold*"
//   dotnet run -c Release --project PeachDrawing.Text.Benchmarks -- --filter "*HintingHot*"
//
// With no --filter the switcher lists the benchmarks and asks which to run. Do not run one while the test suite, a coverage
// run or another build is going: the numbers are only worth comparing when the machine is otherwise quiet.
//
//   dotnet run -c Release --project PeachDrawing.Text.Benchmarks -- --fonts
//
// prints what each benchmarked font gives: its glyph count and how many of its glyphs are grid-fitted at each size, which is
// what tells a benchmark that measures hinting from one that quietly measures the fallback to the scaled design.
if (args.Length == 1 && args[0] == "--fonts")
{
    foreach (string font in HintingColdBenchmarks.Fonts)
    {
        int glyphs = FontFiles.GlyphCount(font);
        Console.Write($"{font}: {glyphs} glyphs, grid-fitted at");
        foreach (int ppem in HintingColdBenchmarks.Sizes)
        {
            var typeface = FontFiles.Load(font);
            var request = new OutlineRequest { PixelsPerEm = ppem, GridFitting = GridFitting.Standard };
            int fitted = 0;
            for (int glyph = 0; glyph < glyphs; glyph++)
            {
                if (typeface.TryGetOutline((ushort)glyph, request, out var outline) && outline.IsGridFitted)
                    fitted++;
            }

            Console.Write($" {ppem} ppem: {fitted};");
        }

        Console.WriteLine();
    }

    return;
}

if (args.Length == 1 && args[0] == "--quick")
{
    // A short version of the cold benchmark, for comparing two builds when the machine is not quiet: the least of many runs is what other work on the machine
    // disturbs least, and the allocations are exact. Each run loads every glyph of a font into a hinting cache that has nothing in it.
    Console.WriteLine("font: glyphs, least ms / median ms per font at 16 ppem, KB allocated per font");
    foreach (string font in HintingColdBenchmarks.Fonts)
    {
        int glyphs = FontFiles.GlyphCount(font);
        var request = new OutlineRequest { PixelsPerEm = 16, GridFitting = GridFitting.Standard };
        var milliseconds = new List<double>();
        long allocated = 0;

        for (int run = 0; run < 45; run++)
        {
            var typeface = FontFiles.Load(font);
            long before = GC.GetAllocatedBytesForCurrentThread();
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            for (int glyph = 0; glyph < glyphs; glyph++)
                typeface.TryGetOutline((ushort)glyph, request, out _);
            double elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            if (run >= 5) // the first runs are the JIT's
                milliseconds.Add(elapsed);
        }

        milliseconds.Sort();
        Console.WriteLine($"{font}: {glyphs}, {milliseconds[0]:F2} / {milliseconds[milliseconds.Count / 2]:F2} ms, {allocated / 1024.0:F0} KB");
    }

    return;
}

if (args.Length >= 1 && args[0] == "--cold-start")
{
    // The first use of each subsystem in a fresh process: the system-font scan, and the load of each Unicode table from the embedded data.
    // BenchmarkDotNet measures the steady state, which hides all of this, so it is timed once per process here (run the command several times and
    // compare the least; `--cold-start` takes no other argument). Each line is milliseconds and KB allocated for that first call alone.
    static void Step(string name, Action action)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        action();
        double elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Console.WriteLine($"{name,-34} {elapsed,9:F2} ms {allocated / 1024.0,10:F0} KB");
    }

    Step("system font scan (family names)", () => _ = FontSet.InstalledFamilyNames.Count());
    Step("load bundled font", () => _ = FontFiles.Load("LiberationSans"));
    Step("Scripts.Of (script table)", () => _ = PeachDrawing.Text.Unicode.Scripts.Of(0x41));
    Step("Bidi.ClassOf (bidi table)", () => _ = PeachDrawing.Text.Unicode.Bidi.ClassOf(new System.Text.Rune(0x41)));
    Step("LineBreaker (line-break data)", () => _ = PeachDrawing.Text.Unicode.LineBreaker.FindOpportunities("Hello world"));
    Step("Segmenter graphemes", () => _ = PeachDrawing.Text.Unicode.Segmenter.FindGraphemeBoundaries("Hello world"));
    Step("Segmenter words", () => _ = PeachDrawing.Text.Unicode.Segmenter.FindWordBoundaries("Hello world"));
    Step("Bidi.Analyze", () => _ = PeachDrawing.Text.Unicode.Bidi.Analyze("Hello world", PeachDrawing.Text.Unicode.BaseDirection.Ltr));
    return;
}

BenchmarkSwitcher.FromAssembly(typeof(FontFiles).Assembly).Run(args);
