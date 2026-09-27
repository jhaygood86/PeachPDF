using BenchmarkDotNet.Running;
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

BenchmarkSwitcher.FromAssembly(typeof(FontFiles).Assembly).Run(args);
