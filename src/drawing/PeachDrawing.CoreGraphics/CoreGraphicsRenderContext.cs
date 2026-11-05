#nullable enable

using Metal;
using PeachDrawing.Core;
using PeachDrawing.Text;
using PeachDrawing.Text.Unicode;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PeachDrawing.CoreGraphics;

/// <summary>
/// The <see cref="RenderContext"/> for <see cref="CoreGraphicsCanvas"/>: where fonts, images and colours come from, and the Core Image context
/// layer effects run in. It owns no surface and creates no view or layer.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lifetime.</b> Dispose the context after every canvas and composer made from it. Disposing it releases the font faces the native text
/// engine loaded and the Core Image context; fonts and images it handed out stay valid objects, but a native font can no longer measure text. It
/// is not thread-safe to draw from two threads at once; the context's own caches are.
/// </para>
/// </remarks>
public class CoreGraphicsRenderContext : RenderContext, IDisposable
{
    private readonly FontSet _fontSet;
    private int _syncedFontVersion = -1;
    private readonly CoreGraphicsTextEngine _textEngine;
    private readonly CoreGraphicsImageCodec _imageCodec;
    private readonly NativeFonts? _native;
    private bool _disposed;

    /// <summary>Makes a context.</summary>
    /// <param name="textEngine">which engine shapes and draws text</param>
    /// <param name="imageCodec">which decoder turns an encoded image into pixels</param>
    /// <param name="fontSet">
    /// the set of fonts the PeachDrawing text engine resolves families from, or <see langword="null"/> for one of this context's own. Give several contexts
    /// one set and a font added to it is read once, however many documents use it; call <see cref="SyncFonts"/> to make a context see fonts added since it
    /// was made. Ignored by the native engine, which keeps its own faces per context.
    /// </param>
    /// <param name="gpuDevice">
    /// the Metal device layer effects and <see cref="CoreGraphicsImageCodec.Auto"/> use, or <see langword="null"/> for the system's default one. It is
    /// borrowed (never released here).
    /// </param>
    /// <param name="useGpu">
    /// <see langword="false"/> to run effects on the CPU and treat the context as having no Metal device, whatever the machine has
    /// </param>
    public CoreGraphicsRenderContext(CoreGraphicsTextEngine textEngine = CoreGraphicsTextEngine.PeachDrawing,
        CoreGraphicsImageCodec imageCodec = CoreGraphicsImageCodec.PeachImage, FontSet? fontSet = null, IMTLDevice? gpuDevice = null, bool useGpu = true)
    {
        _fontSet = fontSet ?? new FontSet();
        _textEngine = textEngine;
        _imageCodec = imageCodec;
        GpuDevice = useGpu ? gpuDevice ?? MTLDevice.SystemDefault : null;
        Effects = new CoreGraphicsEffects(GpuDevice);

        var systemUi = _fontSet.ResolveGeneric(GenericFamily.SystemUi);
        if (textEngine == CoreGraphicsTextEngine.Native)
            _native = new NativeFonts(systemUi);

        var names = textEngine == CoreGraphicsTextEngine.Native ? NativeFonts.InstalledFamilyNames : FontSet.InstalledFamilyNames;
        foreach (var familyName in names)
            AddFontFamily(new CoreGraphicsFontFamily(familyName));

        // The system interface font (San Francisco, "System Font" to this library's own font set) is not among the families the font manager
        // lists. Its name is also what a document that names no font is set in on an Apple platform, so a context that did not know it would
        // fail to find the default font.
        if (systemUi is not null && !names.Contains(systemUi, StringComparer.OrdinalIgnoreCase))
            AddFontFamily(new CoreGraphicsFontFamily(systemUi));

        SyncFonts();
    }

    /// <summary>
    /// Registers with this context the font families that were added to its <c>FontSet</c> since it last looked. A context that was given a
    /// set shared with others has to be told when one of them added a font; one with a set of its own sees its own additions at once and never needs this.
    /// </summary>
    public void SyncFonts()
    {
        var version = _fontSet.Version;
        if (version == _syncedFontVersion)
            return;

        foreach (var familyName in _fontSet.AddedFamilyNames)
            AddFontFamily(new CoreGraphicsFontFamily(familyName));

        _syncedFontVersion = version;
    }

    /// <summary>Which text engine this context's fonts use.</summary>
    public CoreGraphicsTextEngine TextEngine => _textEngine;

    /// <summary>Which decoder turns an encoded image into pixels.</summary>
    public CoreGraphicsImageCodec ImageCodec => _imageCodec;

    /// <summary>
    /// Whether an image ImageIO cannot decode is handed to PeachImage instead, when <see cref="ImageCodec"/> is
    /// <see cref="CoreGraphicsImageCodec.Native"/>. With it off, such an image fails to load. It has no effect with
    /// <see cref="CoreGraphicsImageCodec.PeachImage"/> or <see cref="CoreGraphicsImageCodec.Auto"/>, which always hand such an image to PeachImage.
    /// </summary>
    public bool FallbackToPeachImage { get; set; } = true;

    /// <summary>
    /// Whether text is lightly hinted (fitted to the pixel grid vertically only, so layout is unchanged) on a display with fewer than two pixels to a point, where Apple's
    /// own unhinted text can look soft. Off by default; a <c>TextHinting</c> other than <c>None</c> on the context applies at every density and takes precedence. It affects only the
    /// default text engine, whose glyphs are outlines.
    /// </summary>
    public bool HintTextOnLowDensityDisplays { get; set; }

    /// <summary>
    /// Whether outline text (the default text engine) is thickened to match the weight the platform draws its own text at. macOS applies font smoothing, which
    /// adds about a fifth more ink than the glyph outlines alone; with this on, each glyph is also stroked by about a hundredth of an em, which reproduces it to
    /// first order. On by default on macOS and Mac Catalyst; off elsewhere (iOS does not smooth text, and its weight already matches). It does not affect the native
    /// engine, which is drawn by CoreText.
    /// </summary>
    public bool MatchPlatformTextWeight { get; set; } = OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst();

    /// <summary>The Metal device layer effects run on, or <see langword="null"/> when they run on the CPU. Borrowed: never released here.</summary>
    public IMTLDevice? GpuDevice { get; }

    /// <summary>Whether blur, shadow and colour-matrix effects run on the GPU.</summary>
    public bool EffectsUseGpu => GpuDevice is not null;

    /// <summary>Layout units per point; see <see cref="RenderContext.LayoutUnitsPerPoint"/>. 1 means a layout unit is a point.</summary>
    public double PixelsPerPoint { get; set; } = 1.0;

    /// <inheritdoc />
    public override double LayoutUnitsPerPoint => PixelsPerPoint;

    internal CoreGraphicsResourceCounts Counts { get; } = new();

    internal CoreGraphicsEffects Effects { get; }

    internal NativeFonts Native => _native ?? throw new InvalidOperationException("This context uses the PeachDrawing text engine.");

    internal bool IsDisposed => _disposed;

    /// <summary>The system font set the PeachDrawing text engine resolves families from.</summary>
    protected FontSet FontSet => _fontSet;

    /// <summary>Releases the font faces the native text engine loaded and the Core Image context. Safe to call more than once.</summary>
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
        Effects.Dispose();
    }

    /// <inheritdoc />
    public override string GetCssMediaType(IEnumerable<string> mediaTypesAvailable) =>
        mediaTypesAvailable.Contains("screen") ? "screen" : "all";

    /// <summary>Adds a font from <paramref name="stream"/> under <paramref name="fontFamilyName"/> (or the family the file names).</summary>
    /// <param name="stream">the font file</param>
    /// <param name="fontFamilyName">the family to register it under, or <see langword="null"/> for the one the file names</param>
    public async ValueTask AddFont(Stream stream, string? fontFamilyName) => await AddFont(stream, fontFamilyName, default);

    /// <summary>Adds a font from <paramref name="stream"/> with the weight, width and style it is declared for.</summary>
    /// <param name="stream">the font file</param>
    /// <param name="fontFamilyName">the family to register it under, or <see langword="null"/> for the one the file names</param>
    /// <param name="descriptors">the weight, width and style the font is declared for</param>
    /// <param name="unicodeRanges">the characters the font is restricted to, or <see langword="null"/> for no restriction</param>
    public async ValueTask AddFont(Stream stream, string? fontFamilyName, FontFaceDescriptors descriptors, IReadOnlyList<RuneInterval>? unicodeRanges = null)
    {
        using var memoryStream = new MemoryStream();
        // In memory or on a local disk the copy does not need to wait, which lets a document that must not wait (SynchronousOnly) register the font.
        if (stream is MemoryStream or FileStream)
            stream.CopyTo(memoryStream);
        else
            await stream.CopyToAsync(memoryStream);
        AddFont(memoryStream.ToArray(), fontFamilyName, descriptors, unicodeRanges);
    }

    private void AddFont(byte[] data, string? fontFamilyName, FontFaceDescriptors descriptors, IReadOnlyList<RuneInterval>? unicodeRanges)
    {
        // The PeachDrawing engine always learns the font (it also tells us the family name); CoreText is handed the same bytes when it is the
        // engine in use. A format CoreText cannot load (WOFF) stays available to the PeachDrawing engine only.
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

        AddFontFamily(new CoreGraphicsFontFamily(family.Name));
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

    /// <summary>
    /// What ImageIO reads (PNG, JPEG, GIF, TIFF, BMP, ICO, HEIC, WebP...), when <see cref="ImageCodec"/> is <see cref="CoreGraphicsImageCodec.Native"/>
    /// and <see cref="FallbackToPeachImage"/> is off; PeachImage's formats otherwise.
    /// </summary>
    public override bool SupportsImageType(string mimeType) =>
        _imageCodec == CoreGraphicsImageCodec.Native && !FallbackToPeachImage
            ? mimeType is not null && s_imageIoTypes.Contains(mimeType.Trim())
            : base.SupportsImageType(mimeType);

    private static readonly HashSet<string> s_imageIoTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/jpg", "image/pjpeg", "image/gif", "image/bmp", "image/x-ms-bmp", "image/tiff",
        "image/x-icon", "image/vnd.microsoft.icon", "image/heic", "image/heif", "image/webp", "image/avif", "image/svg+xml"
    };

    /// <inheritdoc />
    protected override Core.Image ImageFromStreamInt(Stream memoryStream) =>
        DecodeImage(CoreGraphicsImage.ReadAll(memoryStream)) ?? throw new InvalidDataException("The stream is not a recognised image format.");

    /// <summary>Decodes with the codec this context is set to use, falling back to PeachImage where allowed.</summary>
    internal CoreGraphicsImage? DecodeImage(byte[] bytes)
    {
        switch (ChooseDecoder(_imageCodec, EffectsUseGpu))
        {
            case ImageDecoder.LazyNative:
                // The system decodes on first draw, in hardware where it can, when it has a codec; anything else is PeachImage's.
                if (CoreGraphicsImage.TryCreateLazy(bytes, Counts) is { } lazy)
                    return lazy;

                break;

            case ImageDecoder.Native:
                var native = CoreGraphicsImage.TryDecodeNative(bytes, Counts);
                if (native is not null || !FallbackToPeachImage)
                    return native;

                break;
        }

        return CoreGraphicsImage.TryDecodePeach(bytes, Counts);
    }

    /// <summary>Which decoder is tried first.</summary>
    internal enum ImageDecoder { PeachImage, Native, LazyNative }

    /// <summary>
    /// The decoder tried first for <paramref name="codec"/>: <see cref="CoreGraphicsImageCodec.Auto"/> keeps an image as encoded bytes for the
    /// system to decode only where there is a Metal device for the result, and is PeachImage's everywhere else.
    /// </summary>
    /// <param name="codec">the codec the context is set to use</param>
    /// <param name="hasGpu">whether the context has a Metal device</param>
    internal static ImageDecoder ChooseDecoder(CoreGraphicsImageCodec codec, bool hasGpu) => codec switch
    {
        CoreGraphicsImageCodec.Auto => hasGpu ? ImageDecoder.LazyNative : ImageDecoder.PeachImage,
        CoreGraphicsImageCodec.Native => ImageDecoder.Native,
        _ => ImageDecoder.PeachImage,
    };

    /// <inheritdoc />
    protected override Font CreateFontInt(string family, double size, PaintFontStyle style, double weight = 400, double stretch = 100, double? obliqueSkewSinus = null, string? variations = null) =>
        _textEngine == CoreGraphicsTextEngine.Native
            ? CreateNativeFont(Native.Resolve(family, weight, IsItalic(style)), family, size, style, weight, stretch)
            : MatchAndCreateFont(family, size, style, weight, stretch, obliqueSkewSinus, variations);

    /// <inheritdoc />
    protected override Font CreateFontInt(FontFamily family, double size, PaintFontStyle style, double weight = 400, double stretch = 100, double? obliqueSkewSinus = null, string? variations = null) =>
        CreateFontInt(family.Name, size, style, weight, stretch, obliqueSkewSinus, variations);

    private CoreGraphicsNativeFont CreateNativeFont(NativeFace face, string family, double size, PaintFontStyle style, double weight, double stretch)
    {
        var italic = IsItalic(style);

        // Synthesis happens only where the matched face lacks the style and the document has not forbidden it.
        var syntheticBold = weight >= 600 && face.Weight < 600 && (style & PaintFontStyle.NoSyntheticBold) == 0;
        var syntheticItalic = italic && !face.Italic && (style & PaintFontStyle.NoSyntheticItalic) == 0;
        return new CoreGraphicsNativeFont(face, family, size, PixelsPerPoint, weight, italic, stretch, syntheticBold, syntheticItalic);
    }

    private CoreGraphicsFont MatchAndCreateFont(string family, double size, PaintFontStyle style, double weight, double stretch, double? obliqueSkewSinus, string? variations)
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

    /// <summary>
    /// Whether a variable font with an optical-size axis (<c>opsz</c>), such as San Francisco, is set to the size it is drawn at, as CoreText does for the system fonts. Without it such a font
    /// draws at its default optical size (San Francisco's is its display cut, narrower and tighter than the text cut CoreText picks below 17 points). On by default; it applies only when the
    /// host gives no <c>font-variation-settings</c> of its own, and to the default text engine (CoreText does it itself for the native one).
    /// </summary>
    public bool AutomaticOpticalSize { get; set; } = true;

    /// <summary>
    /// How glyphs are positioned on the device's pixel grid: <see cref="CoreGraphicsGlyphPlacement.Platform"/> (the default) as Apple places its own text, with a whole-pixel baseline and
    /// a horizontal origin rounded down to a size-dependent fraction of a pixel; <see cref="CoreGraphicsGlyphPlacement.Exact"/> exactly where layout put them. It applies to both text engines
    /// (for the native one, <c>Platform</c> leaves placement to CoreGraphics) and not to a rotated or skewed matrix.
    /// </summary>
    public CoreGraphicsGlyphPlacement GlyphPlacement { get; set; } = CoreGraphicsGlyphPlacement.Platform;

    /// <summary>
    /// Whether a font's tracking table (<c>trak</c>) is applied: the extra space (negative for tighter) the font asks for at the size it is drawn, which Apple's San Francisco uses to set text
    /// tighter as it grows and looser as it shrinks. CoreText always applies it; without it a system font set by the default text engine comes out the wrong width. On by default; it
    /// changes only a font that has the table (almost none but the Apple system fonts), and only the default engine.
    /// </summary>
    public bool ApplyFontTracking { get; set; } = true;

    /// <summary>
    /// Resolves the encoded <c>font-variation-settings</c> into axis settings. With none given, the default sets the optical size to the font's size in points (CoreText's convention, and what
    /// <see cref="AutomaticOpticalSize"/> switches); a host that passes its own variation syntax overrides this (an HTML host uses <c>FontDefaults.ToAxisSettings</c>, whose optical size is the
    /// size in CSS pixels).
    /// </summary>
    /// <param name="variations">the encoded settings, or <see langword="null"/></param>
    /// <param name="sizeInPixels">the font size in CSS pixels, for optical sizing</param>
    /// <returns>the axis settings, or <see langword="null"/> for none</returns>
    protected virtual IReadOnlyList<AxisSetting>? ResolveVariationAxes(string? variations, double sizeInPixels) =>
        variations is null && AutomaticOpticalSize ? [new AxisSetting(AxisTags.OpticalSize, sizeInPixels * PointsPerCssPixel)] : null;

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

    private CoreGraphicsFont CreateCanvasFont(double size, TypefaceMatch match, double? obliqueSkewSinus)
    {
        // A face with a slant axis draws the oblique itself, so the canvas must not shear it as well.
        if (obliqueSkewSinus is not null && match.Typeface.Axes.Any(a => a.Tag == AxisTags.Slant))
            obliqueSkewSinus = null;

        return new CoreGraphicsFont(new TypefaceFont(match.Typeface, size / PixelsPerPoint, match.Synthesis, obliqueSkewSinus), PixelsPerPoint);
    }

    /// <inheritdoc />
    protected override Font? CreateFontForCodepointInt(string family, double size, PaintFontStyle style, double weight, double stretch, double? obliqueSkewSinus, System.Text.Rune codepoint, string? variations)
    {
        if (_textEngine == CoreGraphicsTextEngine.Native)
        {
            // A null here tells the caller to try the next family in the stack: never build a font for a family that can't render this codepoint.
            return Native.Find(family, weight, IsItalic(style)) is { } face && face.ContainsGlyph(codepoint.Value)
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
        if (_textEngine == CoreGraphicsTextEngine.Native)
        {
            return Native.Fallback(codepoint.Value, weight, IsItalic(style)) is { } fallback
                ? CreateNativeFont(fallback, fallback.FamilyName, size, style, weight, stretch)
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
    protected override async ValueTask<bool> AddFontFromStream(string fontFamilyName, Stream stream, string? format, FontFaceDescriptors descriptors = default, IReadOnlyList<RuneInterval>? unicodeRanges = null)
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
    protected override ValueTask<bool> AddLocalFont(string fontFamilyName, string localFontFaceName, FontFaceDescriptors descriptors = default, IReadOnlyList<RuneInterval>? unicodeRanges = null)
    {
        if (!_fontSet.TryGetFontData(localFontFaceName, out var data))
            return new ValueTask<bool>(false);

        AddFont(data.ToArray(), fontFamilyName, descriptors, unicodeRanges);
        return new ValueTask<bool>(true);
    }
}
