using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using PeachDrawing.Text.Outlines;

namespace PeachDrawing.Text.Benchmarks;

/// <summary>
/// The cost of a hinting cache that has nothing in it: a typeface is loaded afresh before every iteration (outside the timing) so
/// that its hinting engine starts empty, and the iteration asks for every glyph of the font at one size. Each glyph is a miss, and
/// the first also reads the font's tables and runs its <c>fpgm</c> and <c>prep</c> programs (or sets up the CFF size). The thread
/// that measures has run the interpreter before, so what it keeps between glyphs (the execution context of a TrueType glyph, a CFF
/// scratch) is warm: this is the steady state of a process that hints a lot, not its very first glyph.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Monitoring, RuntimeMoniker.Net10_0, warmupCount: 5, iterationCount: 40)]
public class HintingColdBenchmarks
{
    /// <summary>The bundled fonts to hint: hinted TrueType fonts (two with composites) and the two CFF ones.</summary>
    public static string[] Fonts { get; } =
    [
        "LiberationSans",
        "LiberationSerif",
        "SourceSans3",
        "HintingOpcodes",
        "HintingCff",
        "HintingCffCid",
    ];

    /// <summary>The sizes, in pixels per em, a font is hinted at.</summary>
    public static int[] Sizes { get; } = [12, 16, 40];

    /// <summary>The bundled font to hint.</summary>
    [ParamsSource(nameof(Fonts))]
    public string Font { get; set; } = "";

    /// <summary>The size in pixels per em.</summary>
    [ParamsSource(nameof(Sizes))]
    public int Ppem { get; set; }

    private int _glyphCount;
    private Typeface _typeface = null!;
    private OutlineRequest _request;

    [GlobalSetup]
    public void Setup()
    {
        _glyphCount = FontFiles.GlyphCount(Font);
        _request = new OutlineRequest { PixelsPerEm = Ppem, GridFitting = GridFitting.Standard };
    }

    [IterationSetup]
    public void FreshTypeface() => _typeface = FontFiles.Load(Font);

    /// <summary>Loads every glyph of the font at the size; the result is the number that had an outline that was grid-fitted, so that it cannot be optimized away.</summary>
    [Benchmark]
    public int LoadEveryGlyph()
    {
        int fitted = 0;
        for (int glyph = 0; glyph < _glyphCount; glyph++)
        {
            if (_typeface.TryGetOutline((ushort)glyph, _request, out var outline) && outline.IsGridFitted)
                fitted++;
        }

        return fitted;
    }
}
