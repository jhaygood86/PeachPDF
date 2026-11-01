using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using PeachDrawing.Text.Outlines;

namespace PeachDrawing.Text.Benchmarks;

/// <summary>
/// The cost of a hinting cache that has what is asked for: every glyph of a working set is loaded once, then several threads
/// ask for them over and over, in an order of their own. The time is per lookup and per thread (the time of the run over the
/// lookups each thread makes), so a cache that scaled perfectly would show the same figure for one thread and for eight; what it
/// shows above that is the price of the threads sharing it.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0)]
public class HintingHotBenchmarks
{
    private const int LookupsPerThread = 500_000;
    private const int WorkingSetSize = 300;

    /// <summary>The bundled font to hint: one with TrueType outlines and one with CFF outlines.</summary>
    [Params("LiberationSans", "HintingCff")]
    public string Font { get; set; } = "";

    /// <summary>How many threads look glyphs up at the same time.</summary>
    [Params(1, 4, 8)]
    public int Threads { get; set; }

    private Typeface _typeface = null!;
    private OutlineRequest _request;
    private ushort[] _glyphs = [];

    [GlobalSetup]
    public void Setup()
    {
        _typeface = FontFiles.Load(Font);
        _request = new OutlineRequest { PixelsPerEm = 16, GridFitting = GridFitting.Standard };

        // the glyphs that have ink, among the first ones of the font: few enough for the cache to keep them all
        var glyphs = new List<ushort>();
        int count = Math.Min(FontFiles.GlyphCount(Font), 4 * WorkingSetSize);
        for (int glyph = 0; glyph < count && glyphs.Count < WorkingSetSize; glyph++)
        {
            if (_typeface.TryGetOutline((ushort)glyph, _request, out var outline) && outline.IsGridFitted)
                glyphs.Add((ushort)glyph);
        }

        if (glyphs.Count < WorkingSetSize / 2)
            throw new InvalidOperationException($"{Font} has only {glyphs.Count} grid-fitted glyphs to look up.");

        _glyphs = [.. glyphs];
    }

    [Benchmark(OperationsPerInvoke = LookupsPerThread)]
    public int Lookups()
    {
        var workers = new Thread[Threads];
        int total = 0;

        for (int t = 0; t < workers.Length; t++)
        {
            uint seed = (uint)(t + 1) * 2654435761u;
            workers[t] = new Thread(() =>
            {
                uint state = seed;
                int fitted = 0;
                for (int i = 0; i < LookupsPerThread; i++)
                {
                    state = state * 1664525u + 1013904223u;
                    ushort glyph = _glyphs[(int)((state >> 8) % (uint)_glyphs.Length)];
                    if (_typeface.TryGetOutline(glyph, _request, out var outline) && outline.IsGridFitted)
                        fitted++;
                }

                Interlocked.Add(ref total, fitted);
            });
        }

        foreach (var worker in workers)
            worker.Start();
        foreach (var worker in workers)
            worker.Join();

        return total;
    }
}
