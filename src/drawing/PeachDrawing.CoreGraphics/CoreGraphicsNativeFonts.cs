using CoreGraphics;
using CoreText;
using Foundation;
using PeachDrawing.Core;
using PeachDrawing.Text;
using PeachDrawing.Text.Shaping;
using System;
using System.Threading;
using System.Collections.Generic;
using System.Linq;

namespace PeachDrawing.CoreGraphics;

/// <summary>A CoreText face the native text engine resolves a family to: builds a <see cref="CTFont"/> of any size on demand.</summary>
internal sealed class NativeFace : IDisposable
{
    private readonly Func<nfloat, CGAffineTransform?, CTFont> _make;
    private readonly IDisposable? _owned;

    public NativeFace(string familyName, int weight, bool italic, Func<nfloat, CGAffineTransform?, CTFont> make, IDisposable? owned = null)
    {
        FamilyName = familyName;
        Weight = weight;
        Italic = italic;
        _make = make;
        _owned = owned;
    }

    public string FamilyName { get; }

    public int Weight { get; }

    public bool Italic { get; }

    /// <summary>A font of <paramref name="pixelSize"/>; the caller disposes it.</summary>
    public CTFont NewFont(double pixelSize, CGAffineTransform? matrix = null) => _make((nfloat)pixelSize, matrix);

    public bool ContainsGlyph(int codepoint)
    {
        using var font = NewFont(12);
        return CoreGraphicsNativeFont.HasGlyph(font, codepoint);
    }

    public void Dispose() => _owned?.Dispose();
}

/// <summary>
/// The faces the native text engine resolves families to: the fonts a document added with <c>@font-face</c>, then the system's font manager.
/// Everything it hands out is released when the context is disposed.
/// </summary>
internal sealed class NativeFonts : IDisposable
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, List<NativeFace>> _registered = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string, int, bool), NativeFace?> _matched = [];
    private readonly List<NativeFace> _owned = [];
    private readonly HashSet<string> _installed;
    private readonly string? _systemUi;
    private NativeFace? _default;

    public NativeFonts(string? systemUiName)
    {
        _systemUi = systemUiName;
        _installed = new HashSet<string>(InstalledFamilyNames, StringComparer.OrdinalIgnoreCase);
    }

    private static readonly NSString FamilyNameKey = new("NSFontFamilyAttribute");

    public static IEnumerable<string> InstalledFamilyNames
    {
        get
        {
            // Every installed face reports its family; a family is listed once.
            using var collection = new CTFontCollection(new CTFontCollectionOptions { RemoveDuplicates = true });
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var descriptor in collection.GetMatchingFontDescriptors() ?? [])
            {
                // kCTFontFamilyNameAttribute; the typed attributes object leaves it unset for a descriptor from a collection.
                if (descriptor.GetAttribute(FamilyNameKey) is NSString { Length: > 0 } name)
                    names.Add(name.ToString());
            }

            return names;
        }
    }

    /// <summary>Registers a font file under <paramref name="family"/>. Returns false for a format CoreText cannot load (WOFF, for one).</summary>
    public bool Add(byte[] data, string family, FontFaceDescriptors descriptors)
    {
        using var provider = new CGDataProvider(data);
        var cgFont = CGFont.CreateFromProvider(provider);
        if (cgFont is null)
            return false;

        var weight = descriptors.Weight is { } range ? (int)((range.Minimum + range.Maximum) / 2) : 400;
        var italic = descriptors.IsItalic ?? false;
        var face = new NativeFace(family, weight, italic, (size, matrix) =>
        {
            return matrix is { } m ? new CTFont(cgFont, size, m) : new CTFont(cgFont, size, (CTFontDescriptor)null!);
        }, cgFont);

        lock (_gate)
        {
            _owned.Add(face);
            if (!_registered.TryGetValue(family, out var list))
                _registered[family] = list = [];

            list.Add(face);
            _matched.Clear();
        }

        return true;
    }

    public NativeFace Resolve(string family, double weight, bool italic) => Find(family, weight, italic) ?? Default();

    public NativeFace? Find(string family, double weight, bool italic)
    {
        lock (_gate)
        {
            var key = (family, (int)weight, italic);
            if (_matched.TryGetValue(key, out var cached))
                return cached;

            NativeFace? face = null;
            if (_registered.TryGetValue(family, out var faces))
            {
                // The closest registered face: the right slant first, then the nearest weight.
                face = faces.MinBy(f => (f.Italic == italic ? 0 : 1000) + Math.Abs(f.Weight - weight));
            }
            else if (string.Equals(family, _systemUi, StringComparison.OrdinalIgnoreCase))
            {
                face = Own(MakeSystem(family, (int)weight, italic));
            }
            else if (_installed.Contains(family))
            {
                face = Own(MakeNamed(family, (int)weight, italic));
            }

            _matched[key] = face;
            return face;
        }
    }

    private NativeFace Own(NativeFace face)
    {
        _owned.Add(face);
        return face;
    }

    private static double CtWeight(double cssWeight) => cssWeight switch
    {
        <= 100 => -0.8,
        <= 200 => -0.6,
        <= 300 => -0.4,
        <= 400 => 0.0,
        <= 500 => 0.23,
        <= 600 => 0.3,
        <= 700 => 0.4,
        <= 800 => 0.56,
        _ => 0.62,
    };

    private static NativeFace MakeNamed(string family, int weight, bool italic) => new(family, weight, italic, (size, matrix) =>
    {
        var traits = new CTFontTraits { Weight = (float)CtWeight(weight), Slant = italic ? 0.2f : 0f };
        using var descriptor = new CTFontDescriptor(new CTFontDescriptorAttributes { FamilyName = family, Traits = traits });
        var font = Make(descriptor, size, matrix);

        // A family that has an italic face asks for it by trait; one that has none is left upright and the canvas slants it.
        return font;
    });

    private static NativeFace MakeSystem(string family, int weight, bool italic) => new(family, weight, italic, (size, matrix) =>
    {
        using var ui = new CTFont(CTFontUIFontType.System, size, null!);
        using var baseDescriptor = ui.GetFontDescriptor();
        var traits = new CTFontTraits { Weight = (float)CtWeight(weight) };
        using var descriptor = baseDescriptor.WithAttributes(new CTFontDescriptorAttributes { Traits = traits })!;
        return Make(descriptor, size, matrix);
    });

    private static CTFont Make(CTFontDescriptor descriptor, nfloat size, CGAffineTransform? matrix)
    {
        if (matrix is not { } m)
            return new CTFont(descriptor, size);

        return new CTFont(descriptor, size, ref m);
    }

    public NativeFace Default()
    {
        lock (_gate)
            return _default ??= Own(MakeSystem(_systemUi ?? "System Font", 400, false));
    }

    /// <summary>A face that has a glyph for <paramref name="codepoint"/>, from the system's own cascade, or <see langword="null"/>.</summary>
    public NativeFace? Fallback(int codepoint, double weight, bool italic)
    {
        var text = char.ConvertFromUtf32(codepoint);
        using var baseFont = Default().NewFont(12);
        using var fallback = baseFont.ForString(text, new NSRange(0, text.Length));
        if (fallback is null || !CoreGraphicsNativeFont.HasGlyph(fallback, codepoint))
            return null;

        // The system's cascade often picks a face whose name is not among the listed families (a hidden emoji or symbol face), so the face is
        // made from the font's own descriptor and not looked up by name.
        var descriptor = fallback.GetFontDescriptor();
        if (descriptor is null)
            return null;

        lock (_gate)
            return Own(new NativeFace(fallback.FamilyName ?? "System Font", (int)weight, italic, (size, matrix) => Make(descriptor, size, matrix), descriptor));
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var face in _owned)
                face.Dispose();

            _owned.Clear();
            _registered.Clear();
            _matched.Clear();
            _default = null;
        }
    }
}

/// <summary>
/// A font that CoreText resolves, shapes and draws. It is a recipe over a shared <see cref="NativeFace"/>: every measurement or draw builds a
/// <see cref="CTFont"/> for the one call and releases it at its end, so a font never holds a native object of its own.
/// </summary>
internal sealed class CoreGraphicsNativeFont : Font
{
    private readonly NativeFace _face;
    private readonly double _points;
    private readonly double _ppp;
    private readonly bool _syntheticBold;
    private readonly bool _syntheticItalic;
    private readonly string _faceKey;
    private readonly double _ascent;
    private readonly double _descent;
    private readonly double _height;
    private double _whitespaceWidth = -1;

    private static readonly CGAffineTransform Slant = new(1, 0, 0.25f, 1, 0, 0);

    public CoreGraphicsNativeFont(NativeFace face, string family, double size, double pixelsPerPoint, double weight, bool italic, double stretch, bool syntheticBold, bool syntheticItalic)
    {
        _face = face;
        _ppp = pixelsPerPoint;
        _points = size / pixelsPerPoint;
        _syntheticBold = syntheticBold;
        _syntheticItalic = syntheticItalic;
        _faceKey = $"coretext/{family}/{(int)weight}/{(italic ? "i" : "n")}/{(int)stretch}";

        using var font = NewFont();
        _ascent = font.AscentMetric;
        _descent = font.DescentMetric;
        _height = _ascent + _descent + font.LeadingMetric;
    }

    public override double Size => _points;

    public override string FaceKey => _faceKey;

    /// <summary>A <see cref="CTFont"/> for this recipe, in layout units. The caller disposes it.</summary>
    public CTFont NewFont() => _face.NewFont(_points * _ppp, _syntheticItalic ? Slant : null);

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

    public override bool HasGlyph(System.Text.Rune rune)
    {
        using var font = NewFont();
        return HasGlyph(font, rune.Value);
    }

    internal static bool HasGlyph(CTFont font, int codepoint)
    {
        var chars = char.ConvertFromUtf32(codepoint).ToCharArray();
        var glyphs = new ushort[chars.Length];

        // For a character outside the BMP the glyph is reported at the high surrogate and the low one's slot stays empty, so the call's own
        // answer (every slot mapped) is not the one wanted.
        font.GetGlyphsForCharacters(chars, glyphs);
        return glyphs[0] != 0;
    }

    // CoreText's shaping applies the font's default features; a caller's explicit ones are not passed to it.
    public override bool SupportsFontVariantCaps(CapsMode feature) => false;

    public override bool SupportsFontVariantPosition(SubSuperMode feature) => false;

    public override (double SizeScale, double BaselineShift)? GetSubSuperscriptMetrics(bool superscript) => null;

    private CTLine NewLine(string text, CTFont font, double letterSpacing)
    {
        var attributes = new CTStringAttributes { Font = font, ForegroundColorFromContext = true };
        if (letterSpacing != 0)
            attributes.KerningAdjustment = (float)letterSpacing;

        using var attributed = new NSAttributedString(text, attributes);
        return new CTLine(attributed);
    }

    /// <summary>The size of <paramref name="text"/> on one line, in layout units.</summary>
    public Core.Size Measure(string text)
    {
        if (text.Length == 0)
            return new Core.Size(0, _height);

        using var font = NewFont();
        using var line = NewLine(text, font, 0);
        return new Core.Size(line.GetTypographicBounds(), _height);
    }

    /// <summary>How many glyphs <paramref name="text"/> shapes into.</summary>
    public int CountGlyphs(string text)
    {
        if (text.Length == 0)
            return 0;

        using var font = NewFont();
        using var line = NewLine(text, font, 0);
        return (int)line.GlyphCount;
    }

    /// <summary>Draws <paramref name="text"/> with its top-left corner at <paramref name="point"/> in <paramref name="context"/>, which is y-down.</summary>
    public void Draw(CGContext context, PaintColor color, string text, PaintPoint point, double letterSpacing, CoreGraphicsGlyphPlacement placement)
    {
        using var font = NewFont();
        using var line = NewLine(text, font, letterSpacing);
        using var fill = CoreGraphicsCanvas.ToColor(color);

        context.SaveState();
        try
        {
            // Exact placement: layout measures with fractional advances, so each glyph is put at its exact position. Otherwise CoreGraphics places it as it does the system's own text
            // (baseline on a whole pixel, glyph origins rounded down to a size-dependent fraction of one).
            if (placement == CoreGraphicsGlyphPlacement.Exact)
            {
                context.SetAllowsSubpixelPositioning(true);
                context.SetShouldSubpixelPositionFonts(true);
                context.SetAllowsFontSubpixelQuantization(false);
                context.ShouldSubpixelQuantizeFonts(false);
            }

            context.SetFillColor(fill);
            context.TranslateCTM((nfloat)point.X, (nfloat)(point.Y + _ascent));
            context.ScaleCTM(1, -1);
            context.TextPosition = new CGPoint(0, 0);
            if (_syntheticBold)
            {
                context.SetStrokeColor(fill);
                context.SetLineWidth((nfloat)(_points * _ppp * 0.02));
                context.SetTextDrawingMode(CGTextDrawingMode.FillStroke);
            }

            line.Draw(context);
        }
        finally
        {
            context.RestoreState();
        }
    }
}
