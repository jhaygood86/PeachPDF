#nullable enable

using PeachDrawing.Core;
using PeachDrawing.Text;
using PeachDrawing.Text.Unicode;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PeachDrawing.Skia;

/// <summary>
/// The <see cref="RenderContext"/> for <see cref="SkiaCanvas"/>: where fonts, images and colours come from. It owns no surface and creates
/// no GPU context; it may be told about the one the host draws with (<see cref="GpuContext"/>) so decoded images are uploaded to it once.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lifetime.</b> Dispose the context after every canvas and composer made from it. Disposing it releases the font faces the native text
/// engine loaded; fonts and images it handed out stay valid objects, but a native font can no longer measure text. It is not thread-safe to
/// draw from two threads at once; the context's own caches are.
/// </para>
/// </remarks>
public class SkiaRenderContext : RenderContext, IDisposable
{
    private readonly FontSet _fontSet = new();
    private readonly SkiaTextEngine _textEngine;
    private readonly SkiaImageCodec _imageCodec;
    private readonly NativeFonts? _native;
    private bool _disposed;

    /// <summary>Makes a context.</summary>
    /// <param name="textEngine">which engine shapes and draws text</param>
    /// <param name="imageCodec">which decoder turns an encoded image into pixels</param>
    /// <param name="textShaper">
    /// for <see cref="SkiaTextEngine.Native"/>, what shapes text into glyphs: <see langword="null"/> uses a basic one that needs nothing beyond
    /// Skia (no ligatures, kerning or complex scripts); pass one over a shaping library (such as HarfBuzz) for those. Ignored by the other engine.
    /// </param>
    public SkiaRenderContext(SkiaTextEngine textEngine = SkiaTextEngine.PeachDrawing, SkiaImageCodec imageCodec = SkiaImageCodec.PeachImage, ISkiaTextShaper? textShaper = null)
    {
        _textEngine = textEngine;
        _imageCodec = imageCodec;
        TextShaper = textShaper ?? BasicSkiaShaper.Instance;
        if (textEngine == SkiaTextEngine.Native)
            _native = new NativeFonts(TextShaper);

        var names = textEngine == SkiaTextEngine.Native ? NativeFonts.InstalledFamilyNames : FontSet.InstalledFamilyNames;
        foreach (var familyName in names)
            AddFontFamily(new SkiaFontFamily(familyName));
    }

    /// <summary>Which text engine this context's fonts use.</summary>
    public SkiaTextEngine TextEngine => _textEngine;

    /// <summary>Which decoder turns an encoded image into pixels.</summary>
    public SkiaImageCodec ImageCodec => _imageCodec;

    /// <summary>What shapes text for <see cref="SkiaTextEngine.Native"/> fonts.</summary>
    public ISkiaTextShaper TextShaper { get; }

    /// <summary>
    /// Whether an image Skia's codecs cannot decode is handed to PeachImage instead, when <see cref="ImageCodec"/> is
    /// <see cref="SkiaImageCodec.Native"/>. With it off, such an image fails to load. It has no effect with <see cref="SkiaImageCodec.PeachImage"/>.
    /// </summary>
    public bool FallbackToPeachImage { get; set; } = true;

    /// <summary>
    /// The GPU context the host draws with, or <see langword="null"/> for a CPU surface. It is borrowed (never disposed here). When set,
    /// a decoded image is uploaded to it once and the texture reused for every draw; when the host's canvas is not backed by it, leave it unset.
    /// </summary>
    public GRContext? GpuContext { get; set; }

    /// <summary>Layout units per point; see <see cref="RenderContext.LayoutUnitsPerPoint"/>. 1 means a layout unit is a point.</summary>
    public double PixelsPerPoint { get; set; } = 1.0;

    /// <inheritdoc />
    public override double LayoutUnitsPerPoint => PixelsPerPoint;

    internal SkiaResourceCounts Counts { get; } = new();

    internal NativeFonts Native => _native ?? throw new InvalidOperationException("This context uses the PeachDrawing text engine.");

    internal bool IsDisposed => _disposed;

    /// <summary>The system font set the PeachDrawing text engine resolves families from.</summary>
    protected FontSet FontSet => _fontSet;

    /// <summary>Releases the font faces the native text engine loaded. Safe to call more than once.</summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases what this context owns.</summary>
    /// <param name="disposing"><see langword="true"/> when called from <see cref="Dispose()"/></param>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || _disposed)
            return;

        _disposed = true;
        _native?.Dispose();
    }

    /// <inheritdoc />
    public override string GetCssMediaType(IEnumerable<string> mediaTypesAvailable) =>
        mediaTypesAvailable.Contains("screen") ? "screen" : "all";

    /// <summary>Adds a font from <paramref name="stream"/> under <paramref name="fontFamilyName"/> (or the family the file names).</summary>
    /// <param name="stream">the font file</param>
    /// <param name="fontFamilyName">the family to register it under, or <see langword="null"/> for the one the file names</param>
    public async Task AddFont(Stream stream, string? fontFamilyName) => await AddFont(stream, fontFamilyName, default);

    /// <summary>Adds a font from <paramref name="stream"/> with the weight, width and style it is declared for.</summary>
    /// <param name="stream">the font file</param>
    /// <param name="fontFamilyName">the family to register it under, or <see langword="null"/> for the one the file names</param>
    /// <param name="descriptors">the weight, width and style the font is declared for</param>
    /// <param name="unicodeRanges">the characters the font is restricted to, or <see langword="null"/> for no restriction</param>
    public async Task AddFont(Stream stream, string? fontFamilyName, FontFaceDescriptors descriptors, IReadOnlyList<RuneInterval>? unicodeRanges = null)
    {
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream);
        AddFont(memoryStream.ToArray(), fontFamilyName, descriptors, unicodeRanges);
    }

    private void AddFont(byte[] data, string? fontFamilyName, FontFaceDescriptors descriptors, IReadOnlyList<RuneInterval>? unicodeRanges)
    {
        // The PeachDrawing engine always learns the font (it also tells us the family name); Skia is handed the same bytes when it is the
        // engine in use. A format Skia cannot load (WOFF) stays available to the PeachDrawing engine only.
        var family = _fontSet.AddData(data, new AddOptions
        {
            FamilyName = fontFamilyName,
            WeightRange = descriptors.Weight,
            IsItalic = descriptors.IsItalic,
            WidthRange = descriptors.Width,
            ObliqueRange = descriptors.Oblique,
            UnicodeRanges = unicodeRanges
        });

        _native?.Add(data, family.Name, descriptors);

        AddFontFamily(new SkiaFontFamily(family.Name));
        OnFontFamilyAdded(family.Name);
    }

    /// <summary>Called after a font family has been added, so a subclass can react (clear its own caches, say).</summary>
    /// <param name="familyName">the family that was added</param>
    protected virtual void OnFontFamilyAdded(string familyName)
    {
    }

    /// <inheritdoc />
    protected override PaintColor GetColorInt(string colorName)
    {
        if (!Enum.TryParse<System.Drawing.KnownColor>(colorName, true, out var knownColor))
            return PaintColor.Empty;

        var c = System.Drawing.Color.FromKnownColor(knownColor);
        return PaintColor.FromArgb(c.A, c.R, c.G, c.B);
    }

    /// <inheritdoc />
    protected override Core.Image ImageFromStreamInt(Stream memoryStream) =>
        DecodeImage(memoryStream) ?? throw new InvalidDataException("The stream is not a recognised image format.");

    /// <summary>Decodes with the codec this context is set to use, falling back to PeachImage where allowed.</summary>
    internal SkiaImage? DecodeImage(Stream stream) => DecodeImage(SkiaImage.ReadAll(stream));

    internal SkiaImage? DecodeImage(byte[] bytes)
    {
        if (_imageCodec == SkiaImageCodec.Native)
        {
            var native = SkiaImage.TryDecodeNative(bytes, Counts);
            if (native is not null || !FallbackToPeachImage)
                return native;
        }

        return SkiaImage.TryDecodePeach(bytes, Counts);
    }

    /// <inheritdoc />
    protected override Font CreateFontInt(string family, double size, PaintFontStyle style, double weight = 400, double stretch = 100, double? obliqueSkewSinus = null, string? variations = null) =>
        _textEngine == SkiaTextEngine.Native
            ? CreateNativeFont(Native.Resolve(family, weight, IsItalic(style), stretch), family, size, style, weight, stretch)
            : MatchAndCreateFont(family, size, style, weight, stretch, obliqueSkewSinus, variations);

    /// <inheritdoc />
    protected override Font CreateFontInt(FontFamily family, double size, PaintFontStyle style, double weight = 400, double stretch = 100, double? obliqueSkewSinus = null, string? variations = null) =>
        CreateFontInt(family.Name, size, style, weight, stretch, obliqueSkewSinus, variations);

    private NativeSkiaFont CreateNativeFont(NativeFace face, string family, double size, PaintFontStyle style, double weight, double stretch)
    {
        var italic = IsItalic(style);

        // Synthesis happens only where the matched face lacks the style and the document has not forbidden it.
        var syntheticBold = weight >= 600 && face.Typeface.FontWeight < 600 && (style & PaintFontStyle.NoSyntheticBold) == 0;
        var syntheticItalic = italic && face.Typeface.FontSlant == SKFontStyleSlant.Upright && (style & PaintFontStyle.NoSyntheticItalic) == 0;
        return new NativeSkiaFont(face, family, size, PixelsPerPoint, weight, italic, stretch, syntheticBold, syntheticItalic);
    }

    private SkiaFont MatchAndCreateFont(string family, double size, PaintFontStyle style, double weight, double stretch, double? obliqueSkewSinus, string? variations)
    {
        var fontStyle = WithoutSynthesisSwitches(style);
        var match = _fontSet.MatchOrFallback(family, QueryFor(weight, stretch, IsItalic(fontStyle), null, size, obliqueSkewSinus, variations));
        return CreateCanvasFont(size, Restrict(match, style), obliqueSkewSinus);
    }

    private TypefaceQuery QueryFor(double weight, double stretch, bool isItalic, System.Text.Rune? mustCover, double size, double? obliqueSkewSinus, string? variations) =>
        TypefaceQuery.From(weight, stretch, isItalic, mustCover,
            ResolveVariationAxes(variations, size / PixelsPerPoint / PointsPerCssPixel),
            obliqueSkewSinus);

    private const double PointsPerCssPixel = 72d / 96d;

    /// <summary>Resolves the encoded <c>font-variation-settings</c> into axis settings; the default has none.</summary>
    /// <param name="variations">the encoded settings, or <see langword="null"/></param>
    /// <param name="sizeInPixels">the font size in CSS pixels, for optical sizing</param>
    /// <returns>the axis settings, or <see langword="null"/> for none</returns>
    protected virtual IReadOnlyList<AxisSetting>? ResolveVariationAxes(string? variations, double sizeInPixels) => null;

    private static PaintFontStyle WithoutSynthesisSwitches(PaintFontStyle style) =>
        style & ~(PaintFontStyle.NoSyntheticBold | PaintFontStyle.NoSyntheticItalic);

    private static bool IsItalic(PaintFontStyle style) => (style & PaintFontStyle.Italic) == PaintFontStyle.Italic;

    private static TypefaceMatch Restrict(TypefaceMatch match, PaintFontStyle style)
    {
        var synthesis = match.Synthesis;
        if ((style & PaintFontStyle.NoSyntheticBold) != 0) synthesis &= ~SyntheticStyle.Bold;
        if ((style & PaintFontStyle.NoSyntheticItalic) != 0) synthesis &= ~SyntheticStyle.Italic;
        return synthesis == match.Synthesis ? match : match with { Synthesis = synthesis };
    }

    private SkiaFont CreateCanvasFont(double size, TypefaceMatch match, double? obliqueSkewSinus)
    {
        // A face with a slant axis draws the oblique itself, so the canvas must not shear it as well.
        if (obliqueSkewSinus is not null && match.Typeface.Axes.Any(a => a.Tag == AxisTags.Slant))
            obliqueSkewSinus = null;

        return new SkiaFont(new TypefaceFont(match.Typeface, size / PixelsPerPoint, match.Synthesis, obliqueSkewSinus), PixelsPerPoint);
    }

    /// <inheritdoc />
    protected override Font? CreateFontForCodepointInt(string family, double size, PaintFontStyle style, double weight, double stretch, double? obliqueSkewSinus, System.Text.Rune codepoint, string? variations)
    {
        if (_textEngine == SkiaTextEngine.Native)
        {
            // A null here tells the caller to try the next family in the stack: never build a font for a family that can't render this codepoint.
            return Native.Find(family, weight, IsItalic(style), stretch) is { } face && face.Typeface.ContainsGlyph(codepoint.Value)
                ? CreateNativeFont(face, family, size, style, weight, stretch)
                : null;
        }

        var fontStyle = WithoutSynthesisSwitches(style);
        if (!_fontSet.TryFindFamily(family, out var typefaceFamily)
            || !typefaceFamily.TryMatch(QueryFor(weight, stretch, IsItalic(fontStyle), codepoint, size, obliqueSkewSinus, variations), out var match))
        {
            return null;
        }

        return CreateCanvasFont(size, Restrict(match, style), obliqueSkewSinus);
    }

    /// <inheritdoc />
    protected override Font? CreateSystemFallbackFontForCodepointInt(double size, PaintFontStyle style, double weight, double stretch, double? obliqueSkewSinus, System.Text.Rune codepoint, EmojiPresentation presentation, string? variations)
    {
        if (_textEngine == SkiaTextEngine.Native)
        {
            return Native.Fallback(codepoint.Value, weight, IsItalic(style), stretch) is { } fallback
                ? CreateNativeFont(fallback, fallback.Typeface.FamilyName, size, style, weight, stretch)
                : null;
        }

        if (!_fontSet.TryFindCoveringFamily(codepoint, presentation, out var fallbackFamily))
            return null;

        var fontStyle = WithoutSynthesisSwitches(style);

        try
        {
            if (!fallbackFamily.TryMatch(QueryFor(weight, stretch, IsItalic(fontStyle), codepoint, size, obliqueSkewSinus, variations), out var match))
                return null;

            return CreateCanvasFont(size, Restrict(match, style), obliqueSkewSinus);
        }
        catch
        {
            // A candidate that passed the cmap-coverage pre-check can still fail to actually load: treat it as "no candidate found".
            return null;
        }
    }

    /// <inheritdoc />
    protected override bool FamilyHasExplicitUnicodeRangesInt(string family) => _fontSet.HasExplicitRanges(family);

    /// <inheritdoc />
    protected override async Task<bool> AddFontFromStream(string fontFamilyName, Stream stream, string? format, FontFaceDescriptors descriptors = default, IReadOnlyList<RuneInterval>? unicodeRanges = null)
    {
        // A missing format() hint is valid CSS and must still be attempted; only formats declared as unsupported are skipped.
        if (format is null or "truetype" or "woff" or "woff2" or "opentype")
        {
            await AddFont(stream, fontFamilyName, descriptors, unicodeRanges);
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    protected override Task<bool> AddLocalFont(string fontFamilyName, string localFontFaceName, FontFaceDescriptors descriptors = default, IReadOnlyList<RuneInterval>? unicodeRanges = null)
    {
        if (!_fontSet.TryGetFontData(localFontFaceName, out var data))
            return Task.FromResult(false);

        AddFont(data.ToArray(), fontFamilyName, descriptors, unicodeRanges);
        return Task.FromResult(true);
    }
}
