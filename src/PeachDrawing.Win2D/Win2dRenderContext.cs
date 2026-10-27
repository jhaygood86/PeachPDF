#nullable enable

using Microsoft.Graphics.Canvas;
using PeachDrawing.Core;
using PeachDrawing.Text;
using PeachDrawing.Text.Unicode;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PeachDrawing.Win2D;

/// <summary>
/// The <see cref="RenderContext"/> for <see cref="Win2dCanvas"/>: where fonts, images and colours come from. It also owns, or borrows, the
/// <see cref="CanvasDevice"/> that offscreen drawing and DirectWrite text need.
/// </summary>
/// <remarks>
/// <para>
/// <b>The device.</b> A device passed to the constructor is borrowed: the context never disposes it. With none given, the context makes
/// its own the first time something needs one (<see cref="Device"/>) and disposes it in <see cref="Dispose()"/>. A
/// <see cref="Win2dCanvas"/> over a caller's drawing session never needs the context's device at all, because it creates everything on the
/// session's own.
/// </para>
/// <para>
/// <b>Lifetime.</b> Dispose the context after every canvas and composer made from it. Disposing it releases the device it made, the font
/// sets it loaded and the temporary font files it wrote; fonts and images it handed out stay valid objects, but a font built for DirectWrite
/// can no longer measure text. It is not thread-safe to draw from two threads at once; the context's own caches are.
/// </para>
/// </remarks>
public class Win2dRenderContext : RenderContext, IDisposable
{
    private readonly FontSet _fontSet = new();
    private readonly object _gate = new();
    private bool _deviceIsOurs;
    private readonly Win2dTextEngine _textEngine;
    private CanvasDevice? _device;
    private DirectWriteFonts? _directWrite;
    private bool _softwareDevice;
    private bool _disposed;

    /// <summary>Makes a context that draws with its own device, or <paramref name="device"/> when one is given.</summary>
    /// <param name="device">a device to use and not dispose, or <see langword="null"/> to let the context make (and own) one when it needs it</param>
    /// <param name="textEngine">which engine shapes and draws text</param>
    public Win2dRenderContext(CanvasDevice? device = null, Win2dTextEngine textEngine = Win2dTextEngine.PeachDrawing)
        : this(device, textEngine, false)
    {
    }

    private Win2dRenderContext(CanvasDevice? device, Win2dTextEngine textEngine, bool softwareDevice)
    {
        _textEngine = textEngine;
        _softwareDevice = softwareDevice;
        if (device is not null)
            AttachDevice(device, ours: false);

        var names = textEngine == Win2dTextEngine.DirectWrite ? DirectWriteFonts.InstalledFamilyNames : FontSet.InstalledFamilyNames;
        foreach (var familyName in names)
            AddFontFamily(new Win2dFontFamily(familyName));
    }

    /// <summary>
    /// Makes a context that draws with a software (WARP) device it owns: slower than the GPU but available without one, and the same on every
    /// machine, which is what a server, a test or a reproducible export wants.
    /// </summary>
    /// <param name="textEngine">which engine shapes and draws text</param>
    public static Win2dRenderContext CreateSoftware(Win2dTextEngine textEngine = Win2dTextEngine.PeachDrawing) =>
        new(null, textEngine, true);

    /// <summary>Which text engine this context's fonts use.</summary>
    public Win2dTextEngine TextEngine => _textEngine;

    /// <summary>Layout units per point; see <see cref="RenderContext.LayoutUnitsPerPoint"/>. 1 means a layout unit is a point.</summary>
    public double PixelsPerPoint { get; set; } = 1.0;

    /// <inheritdoc />
    public override double LayoutUnitsPerPoint => PixelsPerPoint;

    /// <summary>
    /// The device offscreen drawing and DirectWrite text use: the one passed to the constructor, or the one this context made on first use.
    /// </summary>
    /// <exception cref="ObjectDisposedException">the context has been disposed</exception>
    public CanvasDevice Device
    {
        get
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_device is null)
                    AttachDevice(new CanvasDevice(_softwareDevice), ours: true);

                return _device!;
            }
        }
    }

    /// <summary>Raised when the device this context draws with is lost (a driver reset, a removed adapter). Anything drawn must be drawn again.</summary>
    public event EventHandler? DeviceLost;

    internal Win2dResourceCounts Counts { get; } = new();

    internal DirectWriteFonts DirectWrite
    {
        get
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _directWrite ??= new DirectWriteFonts(this);
            }
        }
    }

    internal bool IsDisposed => _disposed;

    /// <summary>The PeachDrawing font set, which DirectWrite fonts consult for coverage and for glyphs addressed by index.</summary>
    internal FontSet CoverageFontSet => _fontSet;

    // Whether a device is the context's to dispose is a fact about that device, not about the context: a borrowed device that is lost is
    // replaced by one the context makes, and that one is ours.
    private void AttachDevice(CanvasDevice device, bool ours)
    {
        _device = device;
        _deviceIsOurs = ours;
        device.DeviceLost += OnDeviceLost;
    }

    private void OnDeviceLost(CanvasDevice sender, object args)
    {
        // Everything made on the lost device is unusable. What this backend made from it is rebuilt on next use because it is keyed by
        // device identity; a device the context made is replaced, and one a caller gave is theirs to replace.
        lock (_gate)
        {
            sender.DeviceLost -= OnDeviceLost;
            if (ReferenceEquals(_device, sender))
            {
                _device = null;
                if (_deviceIsOurs)
                    sender.Dispose();
            }
        }

        DeviceLost?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Releases the device this context made, the font sets it loaded and the font files it wrote. Safe to call more than once.</summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases what this context owns.</summary>
    /// <param name="disposing"><see langword="true"/> when called from <see cref="Dispose()"/></param>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
            return;

        CanvasDevice? ownedDevice = null;
        DirectWriteFonts? directWrite;
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            if (_device is not null)
            {
                _device.DeviceLost -= OnDeviceLost;
                if (_deviceIsOurs)
                    ownedDevice = _device;
                _device = null;
            }

            directWrite = _directWrite;
            _directWrite = null;
        }

        directWrite?.Dispose();
        ownedDevice?.Dispose();
    }

    /// <summary>The system font set the PeachDrawing text engine resolves families from.</summary>
    protected FontSet FontSet => _fontSet;

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
        // The PeachDrawing engine always learns the font (it also tells us the family name); DirectWrite is handed the same bytes when it is
        // the engine in use.
        var family = _fontSet.AddData(data, new AddOptions
        {
            FamilyName = fontFamilyName,
            WeightRange = descriptors.Weight,
            IsItalic = descriptors.IsItalic,
            WidthRange = descriptors.Width,
            ObliqueRange = descriptors.Oblique,
            UnicodeRanges = unicodeRanges
        });

        if (_textEngine == Win2dTextEngine.DirectWrite)
            DirectWrite.AddFont(data, family.Name, descriptors);

        AddFontFamily(new Win2dFontFamily(family.Name));
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
        Win2dImage.TryDecode(memoryStream) ?? throw new InvalidDataException("The stream is not a recognised image format.");

    /// <inheritdoc />
    protected override Font CreateFontInt(string family, double size, PaintFontStyle style, double weight = 400, double stretch = 100, double? obliqueSkewSinus = null, string? variations = null) =>
        _textEngine == Win2dTextEngine.DirectWrite
            ? DirectWrite.CreateFont(family, size, style, weight, stretch)
            : MatchAndCreateFont(family, size, style, weight, stretch, obliqueSkewSinus, variations);

    /// <inheritdoc />
    protected override Font CreateFontInt(FontFamily family, double size, PaintFontStyle style, double weight = 400, double stretch = 100, double? obliqueSkewSinus = null, string? variations = null) =>
        CreateFontInt(family.Name, size, style, weight, stretch, obliqueSkewSinus, variations);

    private Win2dFont MatchAndCreateFont(string family, double size, PaintFontStyle style, double weight, double stretch, double? obliqueSkewSinus, string? variations)
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

    private Win2dFont CreateCanvasFont(double size, TypefaceMatch match, double? obliqueSkewSinus)
    {
        // A face with a slant axis draws the oblique itself, so the canvas must not shear it as well.
        if (obliqueSkewSinus is not null && match.Typeface.Axes.Any(a => a.Tag == AxisTags.Slant))
            obliqueSkewSinus = null;

        return new Win2dFont(new TypefaceFont(match.Typeface, size / PixelsPerPoint, match.Synthesis, obliqueSkewSinus), PixelsPerPoint);
    }

    /// <inheritdoc />
    protected override Font? CreateFontForCodepointInt(string family, double size, PaintFontStyle style, double weight, double stretch, double? obliqueSkewSinus, System.Text.Rune codepoint, string? variations)
    {
        if (_textEngine == Win2dTextEngine.DirectWrite)
            return DirectWrite.CreateFontForCodepoint(family, size, style, weight, stretch, codepoint);

        var fontStyle = WithoutSynthesisSwitches(style);

        // A null here tells the caller to try the next family in the stack: never build a font for a family that can't render this codepoint.
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
        if (_textEngine == Win2dTextEngine.DirectWrite)
            return DirectWrite.CreateSystemFallbackFont(size, style, weight, stretch, codepoint);

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
