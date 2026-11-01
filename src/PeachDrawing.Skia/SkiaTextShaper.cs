using SkiaSharp;
using System;

namespace PeachDrawing.Skia;

/// <summary>
/// One run of text after shaping: which glyph to draw for each position, where each goes, and how wide the whole run is. All the arrays have
/// one entry per glyph, in drawing order, and positions are relative to the start of the run on its baseline.
/// </summary>
/// <param name="Glyphs">the glyph ids, as the typeface numbers them</param>
/// <param name="XOffsets">how far right of the run's start each glyph's origin is</param>
/// <param name="YOffsets">how far below the baseline each glyph's origin is (usually 0)</param>
/// <param name="Clusters">for each glyph, the index in the source text of the first character it stands for</param>
/// <param name="Width">the advance of the whole run</param>
public sealed record SkiaShapedRun(ushort[] Glyphs, float[] XOffsets, float[] YOffsets, uint[] Clusters, float Width);

/// <summary>
/// Turns text into positioned glyphs for the <see cref="SkiaTextEngine.Native"/> engine. The package ships one that needs nothing beyond Skia
/// (one glyph per character, in order, advanced by the font's own widths - right for text that needs no shaping); a host that wants real
/// shaping (ligatures, kerning, scripts that reorder or join) implements this over a shaping library such as <c>SkiaSharp.HarfBuzz</c>, which
/// this package deliberately does not depend on, and passes it to <see cref="SkiaRenderContext"/>.
/// </summary>
/// <remarks>
/// The context calls a shaper from one thread at a time, so an implementation may hold state (an <c>SKShaper</c> per typeface, say) without
/// locking of its own. It must not dispose the font it is given.
/// </remarks>
public interface ISkiaTextShaper
{
    /// <summary>Shapes <paramref name="text"/> in <paramref name="font"/>, which carries the typeface, size and styling to shape with.</summary>
    /// <param name="text">the text of one run</param>
    /// <param name="font">the font to shape with</param>
    /// <returns>the shaped run</returns>
    SkiaShapedRun Shape(string text, SKFont font);
}

/// <summary>The shaper used when none is supplied: Skia's own character-to-glyph mapping and advances, with no substitution or reordering.</summary>
internal sealed class BasicSkiaShaper : ISkiaTextShaper
{
    public static BasicSkiaShaper Instance { get; } = new();

    public SkiaShapedRun Shape(string text, SKFont font)
    {
        var glyphs = font.GetGlyphs(text);
        var widths = font.GetGlyphWidths(glyphs);
        var xs = new float[glyphs.Length];
        var clusters = new uint[glyphs.Length];

        // One glyph per character (a surrogate pair is one character), so each glyph's cluster is where its character starts.
        var index = 0;
        var x = 0f;
        for (var i = 0; i < glyphs.Length; i++)
        {
            xs[i] = x;
            x += widths[i];
            clusters[i] = (uint)Math.Min(index, text.Length);
            index += index + 1 < text.Length && char.IsHighSurrogate(text[index]) && char.IsLowSurrogate(text[index + 1]) ? 2 : 1;
        }

        return new SkiaShapedRun(glyphs, xs, new float[glyphs.Length], clusters, x);
    }
}
