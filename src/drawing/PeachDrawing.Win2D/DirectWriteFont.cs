using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using PeachDrawing.Core;
using PeachDrawing.Text;
using PeachDrawing.Text.Shaping;
using System;
using System.Numerics;
using Windows.UI.Text;

namespace PeachDrawing.Win2D;

/// <summary>
/// A font that DirectWrite shapes, measures and draws. It is a recipe (family, size, weight, style, stretch) and nothing native: every
/// measurement or draw builds a <see cref="CanvasTextFormat"/> and a <see cref="CanvasTextLayout"/> for the one call and releases them at
/// its end, so a font never holds a Win2D object and cannot outlive the device it was made for.
/// </summary>
internal sealed class DirectWriteFont : Font
{
    private readonly Win2dRenderContext _context;
    private readonly string _family;
    private readonly double _points;
    private readonly double _ppp;
    private readonly ushort _weight;
    private readonly bool _italic;
    private readonly FontStretch _stretch;
    private readonly PaintFontStyle _style;
    private readonly string _faceKey;
    private double _ascent = -1;
    private double _lineHeight;
    private double _whitespaceWidth = -1;
    private Typeface? _outlineTypeface;
    private bool _outlineTypefaceLooked;

    public DirectWriteFont(Win2dRenderContext context, string family, double size, PaintFontStyle style, double weight, double stretch)
    {
        _context = context;
        _family = family;
        _ppp = context.PixelsPerPoint;
        _points = size / _ppp;
        _style = style;
        _weight = (ushort)Math.Clamp(Math.Round(weight), 1, 999);
        _italic = (style & PaintFontStyle.Italic) == PaintFontStyle.Italic;
        _stretch = StretchFor(stretch);
        _faceKey = $"dw/{family}/{_weight}/{(_italic ? "i" : "n")}/{(int)_stretch}";
    }

    public override double Size => _points;

    public override string FaceKey => _faceKey;

    /// <summary>The PeachDrawing typeface matched to this family, if one is installed: used for coverage questions and for glyphs addressed by index.</summary>
    public Typeface? OutlineTypeface
    {
        get
        {
            if (!_outlineTypefaceLooked)
            {
                _outlineTypefaceLooked = true;
                if (_context.CoverageFontSet.TryFindFamily(_family, out var family)
                    && family.TryMatch(TypefaceQuery.From(_weight, 100, _italic, null, null, null), out var match))
                {
                    _outlineTypeface = match.Typeface;
                }
            }

            return _outlineTypeface;
        }
    }

    private static FontStretch StretchFor(double percent) => percent switch
    {
        < 56.25 => FontStretch.UltraCondensed,
        < 68.75 => FontStretch.ExtraCondensed,
        < 81.25 => FontStretch.Condensed,
        < 93.75 => FontStretch.SemiCondensed,
        < 106.25 => FontStretch.Normal,
        < 118.75 => FontStretch.SemiExpanded,
        < 137.5 => FontStretch.Expanded,
        < 175 => FontStretch.ExtraExpanded,
        _ => FontStretch.UltraExpanded,
    };

    /// <summary>A text format for this font, in layout units. The caller disposes it.</summary>
    private CanvasTextFormat NewFormat()
    {
        var resolved = _context.DirectWrite.Resolve(_family, _weight, _italic);
        return new CanvasTextFormat
        {
            FontFamily = resolved,
            FontSize = (float)(_points * _ppp),
            FontWeight = new FontWeight { Weight = _weight },
            FontStyle = _italic ? FontStyle.Italic : FontStyle.Normal,
            FontStretch = _stretch,
            WordWrapping = CanvasWordWrapping.NoWrap,
            Direction = CanvasTextDirection.LeftToRightThenTopToBottom,
        };
    }

    /// <summary>A layout of <paramref name="text"/> in this font, created on <paramref name="creator"/>'s device. The caller disposes it.</summary>
    private CanvasTextLayout NewLayout(ICanvasResourceCreator creator, string text, double letterSpacing, ShapeSettings? features)
    {
        using var format = NewFormat();
        var layout = new CanvasTextLayout(creator, text, format, 1_000_000f, 0f);
        try
        {
            if (letterSpacing != 0 && text.Length > 0)
                layout.SetCharacterSpacing(0, text.Length, 0f, (float)letterSpacing, 0f);

            if (features is { } settings && text.Length > 0)
                ApplyFeatures(layout, text.Length, settings);

            return layout;
        }
        catch
        {
            layout.Dispose();
            throw;
        }
    }

    private static void ApplyFeatures(CanvasTextLayout layout, int length, ShapeSettings settings)
    {
        var requested = settings.ExplicitFeatures;
        var wantsPlain = !settings.Kerning || settings.Ligatures == LigatureSet.None;
        if (requested is not { Count: > 0 } && !wantsPlain)
            return;

        using var typography = new CanvasTypography();
        if (!settings.Kerning)
            typography.AddFeature(CanvasTypographyFeatureName.Kerning, 0);

        if (settings.Ligatures == LigatureSet.None)
        {
            typography.AddFeature(CanvasTypographyFeatureName.StandardLigatures, 0);
            typography.AddFeature(CanvasTypographyFeatureName.ContextualLigatures, 0);
        }

        if (requested is not null)
        {
            foreach (var feature in requested)
            {
                if (feature.Tag.Length == 4)
                {
                    // A feature name is its four-letter tag packed little-endian.
                    var name = (CanvasTypographyFeatureName)((uint)feature.Tag[0] | (uint)feature.Tag[1] << 8 | (uint)feature.Tag[2] << 16 | (uint)feature.Tag[3] << 24);
                    typography.AddFeature(name, (uint)Math.Max(feature.Value, 0));
                }
            }
        }

        layout.SetTypography(0, length, typography);
    }

    private void EnsureMetrics()
    {
        if (_ascent >= 0)
            return;

        // The line a font makes is its metrics: ask DirectWrite for one line of text and read how tall it is and where its baseline sits.
        using var layout = NewLayout(_context.Device, "Hg", 0, null);
        var line = layout.LineMetrics[0];
        _ascent = line.Baseline;
        _lineHeight = line.Height;
    }

    public override double Height
    {
        get
        {
            EnsureMetrics();
            return _lineHeight;
        }
    }

    public override double Ascent
    {
        get
        {
            EnsureMetrics();
            return Math.Round(_ascent);
        }
    }

    public override double TextBaselineOffset
    {
        get
        {
            EnsureMetrics();
            return _ascent;
        }
    }

    public override double NormalLineHeight => Height;

    public override double UnderlineOffset => Math.Round(Height - (Height - Ascent) * 0.5);

    public override double LeftPadding => Height / 6f;

    public override double GetWhitespaceWidth(Canvas graphics)
    {
        if (_whitespaceWidth < 0)
            _whitespaceWidth = graphics.MeasureString(" ", this).Width;

        return _whitespaceWidth;
    }

    public override bool HasGlyph(System.Text.Rune rune) => OutlineTypeface?.HasGlyph(rune) ?? true;

    public override bool SupportsFontVariantCaps(CapsMode feature) =>
        OutlineTypeface?.SupportsFeatures(Shaper.GetFeatureTags(feature)) ?? false;

    public override bool SupportsFontVariantPosition(SubSuperMode feature) =>
        OutlineTypeface?.SupportsFeatures(Shaper.GetFeatureTags(feature)) ?? false;

    public override (double SizeScale, double BaselineShift)? GetSubSuperscriptMetrics(bool superscript) =>
        OutlineTypeface is { } typeface && typeface.TryGetScriptPosition(superscript ? ScriptPlacement.Superscript : ScriptPlacement.Subscript, out var position)
            ? (position.SizeScale, position.BaselineShift)
            : null;

    // ---- what a canvas asks of a font ---------------------------------------------------------------------

    /// <summary>The size of <paramref name="text"/> on one line, in layout units.</summary>
    public Core.Size Measure(string text, ShapeSettings? features)
    {
        if (text.Length == 0)
            return new Core.Size(0, Height);

        using var layout = NewLayout(_context.Device, text, 0, features);
        var bounds = layout.LayoutBoundsIncludingTrailingWhitespace;
        return new Core.Size(bounds.Width, Math.Max(bounds.Height, Height));
    }

    /// <summary>How many glyph clusters <paramref name="text"/> shapes into.</summary>
    public int CountClusters(string text, ShapeSettings? features)
    {
        if (text.Length == 0)
            return 0;

        using var layout = NewLayout(_context.Device, text, 0, features);
        return layout.ClusterMetrics.Length;
    }

    /// <summary>
    /// DirectWrite hands back no outlines a path could be built from, so this reports none and the caller falls back to drawing the string, as
    /// <see cref="Canvas.GetTextOutline"/> allows for a font without outlines.
    /// </summary>
    public GraphicsPath? GetOutline(GraphicsPath path, string text, PaintPoint baselineOrigin, double letterSpacing, ShapeSettings? features)
    {
        path.Dispose();
        return null;
    }

    /// <summary>Draws <paramref name="text"/> with its top-left corner at <paramref name="point"/>, on <paramref name="canvas"/>'s session.</summary>
    public void Draw(Win2dCanvas canvas, string text, PaintColor color, PaintPoint point, double letterSpacing, ShapeSettings? features)
    {
        if (color.A == 0)
            return;

        using var layout = NewLayout(canvas.Session, text, letterSpacing, features);
        layout.Options = CanvasDrawTextOptions.EnableColorFont;
        var (r, g, b) = color.ToRgb();
        canvas.Session.DrawTextLayout(layout, new Vector2((float)point.X, (float)point.Y), Windows.UI.Color.FromArgb(color.A, r, g, b));
    }
}
