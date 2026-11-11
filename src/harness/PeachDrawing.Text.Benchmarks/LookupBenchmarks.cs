using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using PeachDrawing.Text.Unicode;

namespace PeachDrawing.Text.Benchmarks;

/// <summary>
/// The per-character lookups the layout engine makes for every word: a codepoint to a glyph of a face (the <c>cmap</c> lookup), and a
/// codepoint to its script, bidi class and line-break class. Each benchmark walks one corpus once per invocation, so the figure is
/// time per corpus pass and per-character cost is that over <see cref="Corpus"/>'s length. Public API only.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0)]
public class LookupBenchmarks
{
    private Typeface _typeface = null!;
    private Rune[] _runes = [];
    private int[] _codepoints = [];

    /// <summary>The bundled font whose <c>cmap</c> is searched.</summary>
    [Params("LiberationSans", "SourceSans3")]
    public string Font { get; set; } = "";

    /// <summary>The text walked: plain ASCII prose, Latin prose with typographic punctuation, or a spread across scripts.</summary>
    [Params(Corpora.Ascii, Corpora.Latin, Corpora.Mixed)]
    public string Corpus { get; set; } = "";

    [GlobalSetup]
    public void Setup()
    {
        _typeface = FontFiles.Load(Font);
        _runes = [.. Corpora.Get(Corpus).EnumerateRunes()];
        _codepoints = [.. _runes.Select(r => r.Value)];
    }

    /// <summary>The <c>cmap</c> lookup of every character, as shaping and the per-word face check make it.</summary>
    [Benchmark]
    public int MapRunes()
    {
        int mapped = 0;
        foreach (var rune in _runes)
        {
            if (_typeface.TryMapRune(rune, out _))
                mapped++;
        }

        return mapped;
    }

    /// <summary>The script of every character.</summary>
    [Benchmark]
    public int ScriptOf()
    {
        int length = 0;
        foreach (int codepoint in _codepoints)
            length += Scripts.Of(codepoint).Length;
        return length;
    }

    /// <summary>The bidi class of every character.</summary>
    [Benchmark]
    public int BidiClassOf()
    {
        int sum = 0;
        foreach (var rune in _runes)
            sum += (int)Bidi.ClassOf(rune);
        return sum;
    }
}

/// <summary>
/// Whole-paragraph analysis: bidi, line-break opportunities, and grapheme, word and sentence boundaries, over prose of a few thousand
/// characters. The time is per paragraph set (<see cref="Corpus"/> repeated to <c>Length</c> characters).
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0)]
public class ParagraphBenchmarks
{
    private string _text = "";

    /// <summary>The text analyzed.</summary>
    [Params(Corpora.Ascii, Corpora.Latin, Corpora.Mixed)]
    public string Corpus { get; set; } = "";

    [GlobalSetup]
    public void Setup()
    {
        var builder = new StringBuilder();
        string unit = Corpora.Get(Corpus);
        while (builder.Length < 4000)
            builder.Append(unit);
        _text = builder.ToString();
    }

    /// <summary>The bidi analysis of the text, base direction left to right.</summary>
    [Benchmark]
    public int BidiAnalyze() => Bidi.Analyze(_text, BaseDirection.Ltr).GetHashCode();

    /// <summary>Every line-break opportunity of the text.</summary>
    [Benchmark]
    public int LineBreaks() => LineBreaker.FindOpportunities(_text).Length;

    /// <summary>Every grapheme boundary of the text.</summary>
    [Benchmark]
    public int GraphemeBoundaries() => Segmenter.FindGraphemeBoundaries(_text).Length;

    /// <summary>Every word boundary of the text.</summary>
    [Benchmark]
    public int WordBoundaries() => Segmenter.FindWordBoundaries(_text).Length;
}

/// <summary>The texts the lookup and paragraph benchmarks walk; <see cref="Get"/> maps a name to its text.</summary>
public static class Corpora
{
    /// <summary>Plain ASCII prose.</summary>
    public const string Ascii = "ascii";

    /// <summary>Latin prose with curly quotes, dashes, accents and an ellipsis: not ASCII, still all left to right.</summary>
    public const string Latin = "latin";

    /// <summary>Latin, Greek, Cyrillic, CJK, Arabic, Hebrew and Devanagari together.</summary>
    public const string Mixed = "mixed";

    /// <summary>The text a corpus name stands for.</summary>
    public static string Get(string name) => name switch
    {
        Ascii => "The quick brown fox jumps over the lazy dog. Pack my box with five dozen liquor jugs, and sphinx of black quartz, judge my vow. "
            + "How vexingly quick daft zebras jump! Typography is the art and technique of arranging type to make written language legible. ",
        Latin => "“The café’s naïve façade—rebuilt in 1923—was, as Zoë put it, ‘rather charming’ …” she said. Müller’s résumé listed Åsa, Łódź and São Paulo. "
            + "An en–dash, a non‑breaking hyphen and a thin space follow, then more words to fill out the line and the paragraph. ",
        Mixed => "Hello world. Γειά σου κόσμε. Привет, мир! 你好，世界。こんにちは世界。 مرحبا بالعالم שלום עולם नमस्ते दुनिया. "
            + "Mixed text such as this is rarer than prose in one script, but it is what exercises every table. ",
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };
}
