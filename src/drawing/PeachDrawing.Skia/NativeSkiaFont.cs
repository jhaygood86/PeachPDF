using PeachDrawing.Core;
using PeachDrawing.Text.Shaping;
using SkiaSharp;
using System;

namespace PeachDrawing.Skia;

/// <summary>
/// A font that Skia's font manager resolves, a shaper shapes and Skia draws. It is a recipe over a shared <see cref="NativeFace"/>: every
/// measurement or draw builds an <see cref="SKFont"/> for the one call and releases it at its end, so a font never holds a native object of
/// its own and cannot outlive the context's faces by more than a failed call.
/// </summary>
internal sealed class NativeSkiaFont : Font
{
    private readonly NativeFace _face;
    private readonly double _points;
    private readonly double _ppp;
    private readonly bool _syntheticBold;
    private readonly bool _syntheticItalic;
    private readonly string _faceKey;
    private readonly double _ascent;
    private readonly double _height;
    private double _whitespaceWidth = -1;

    public NativeSkiaFont(NativeFace face, string family, double size, double pixelsPerPoint, double weight, bool italic, double stretch, bool syntheticBold, bool syntheticItalic)
    {
        _face = face;
        _ppp = pixelsPerPoint;
        _points = size / pixelsPerPoint;
        _syntheticBold = syntheticBold;
        _syntheticItalic = syntheticItalic;
        _faceKey = $"skia/{family}/{(int)weight}/{(italic ? "i" : "n")}/{(int)stretch}";

        using var font = NewFont();
        var metrics = font.Metrics;
        _ascent = -metrics.Ascent;
        _height = _ascent + metrics.Descent + metrics.Leading;
    }

    public override double Size => _points;

    public override string FaceKey => _faceKey;

    internal NativeFace Face => _face;

    /// <summary>An <see cref="SKFont"/> for this recipe, in layout units. The caller disposes it.</summary>
    public SKFont NewFont()
    {
        var font = new SKFont(_face.Typeface, (float)(_points * _ppp))
        {
            Subpixel = true,
            Edging = SKFontEdging.SubpixelAntialias,
            Hinting = SKFontHinting.None,
            Embolden = _syntheticBold,
        };

        if (_syntheticItalic)
            font.SkewX = -0.25f;

        return font;
    }

    public override double Height => _height;

    public override double Ascent => Math.Round(_ascent);

    public override double TextBaselineOffset => _ascent;

    public override double NormalLineHeight => _height;

    public override double UnderlineOffset => Math.Round(_height - (_height - _ascent) * 0.5);

    public override double LeftPadding => _height / 6f;

    public override double GetWhitespaceWidth(Canvas graphics)
    {
        if (_whitespaceWidth < 0)
            _whitespaceWidth = graphics.MeasureString(" ", this).Width;

        return _whitespaceWidth;
    }

    public override bool HasGlyph(System.Text.Rune rune) => _face.Typeface.ContainsGlyph(rune.Value);

    // Skia's shaping applies the font's default features; a caller's explicit ones are not passed to the shaper.
    public override bool SupportsFontVariantCaps(CapsMode feature) => false;

    public override bool SupportsFontVariantPosition(SubSuperMode feature) => false;

    public override (double SizeScale, double BaselineShift)? GetSubSuperscriptMetrics(bool superscript) => null;

    /// <summary>The size of <paramref name="text"/> on one line, in layout units.</summary>
    public Core.Size Measure(string text)
    {
        if (text.Length == 0)
            return new Core.Size(0, _height);

        using var font = NewFont();
        return new Core.Size(_face.Shape(text, font).Width, _height);
    }

    /// <summary>How many glyph clusters <paramref name="text"/> shapes into.</summary>
    public int CountClusters(string text)
    {
        if (text.Length == 0)
            return 0;

        using var font = NewFont();
        var clusters = _face.Shape(text, font).Clusters;
        var count = 0;
        for (var i = 0; i < clusters.Length; i++)
        {
            if (i == 0 || clusters[i] != clusters[i - 1])
                count++;
        }

        return count;
    }

    /// <summary>Draws <paramref name="text"/> with its top-left corner at <paramref name="point"/> on <paramref name="canvas"/>.</summary>
    public void Draw(SKCanvas canvas, SKPaint paint, string text, PaintPoint point, double letterSpacing)
    {
        using var font = NewFont();
        var run = _face.Shape(text, font);
        if (run.Glyphs.Length == 0)
            return;

        var points = new SKPoint[run.Glyphs.Length];
        for (var i = 0; i < points.Length; i++)
            points[i] = new SKPoint(run.XOffsets[i] + (float)(letterSpacing * i), run.YOffsets[i]);

        using var builder = new SKTextBlobBuilder();
        builder.AddPositionedRun(run.Glyphs, font, points);
        using var blob = builder.Build();
        if (blob is not null)
            canvas.DrawText(blob, (float)point.X, (float)(point.Y + _ascent), paint);
    }
}
