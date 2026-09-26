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

BenchmarkSwitcher.FromAssembly(typeof(FontFiles).Assembly).Run(args);
