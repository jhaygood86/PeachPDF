using PeachDrawing.Abstractions;
using PeachDrawing.Text;
using PeachDrawing.Text.Unicode;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace PeachDrawing;

/// <summary>
/// A <see cref="RenderContext"/> for drawing directly onto a <see cref="RasterCanvas"/> canvas with no
/// HTML, CSS, layout, or PDF document involved. <see cref="CreateCanvas"/> is the entry point:
/// <code>
/// using var ctx = new RasterRenderContext();
/// using var canvas = ctx.CreateCanvas(width: 800, height: 600);
/// canvas.DrawRectangle(ctx.GetSolidBrush(PaintColor.FromArgb(255, 30, 144, 255)), 50, 50, 200, 100);
/// canvas.Save(stream, "png", new PngEncoderOptions());
/// </code>
/// </summary>
/// <remarks>
/// Font creation goes through <see cref="TypefaceFont"/> and image decoding through
/// <see cref="DecodedImage"/> - both PeachDrawing.Abstractions/PeachDrawing's own types, no PDF backend
/// involved anywhere in the chain. PeachPDF drives this same class as its own raster fallback (the effects
/// PDF cannot express in vector form - filters, shadows, backdrop effects); this is not a copy built for
/// PeachPDF's benefit, it is the one implementation both uses.
/// </remarks>
public sealed class RasterRenderContext : RenderContext
{
    private readonly FontSet _fontSet = new();

    /// <summary>Creates a render context with every font installed on the machine already registered by name.</summary>
    public RasterRenderContext()
    {
        // Unlike PdfSharpAdapter's constructor, this deliberately skips CSS generic-family aliasing
        // (Helvetica->Arial, system-ui, math, etc.) - a standalone canvas author names a real family
        // directly, not a CSS generic keyword, so there is no CSS cascade here to resolve one against.
        foreach (var familyName in FontSet.InstalledFamilyNames)
        {
            AddFontFamily(new NamedFontFamily(familyName));
        }
    }

    /// <summary>
    /// Creates a standalone <see cref="RasterCanvas"/> canvas of exactly <paramref name="widthPx"/> by
    /// <paramref name="heightPx"/> device pixels. This canvas's own coordinate convention is plain: one
    /// user-space unit is one device pixel, regardless of <paramref name="dpi"/> - a <c>Rect(0, 0, 100, 50)</c>
    /// and a font <c>size</c> of <c>24</c> both mean exactly that many pixels. <paramref name="dpi"/> is
    /// accepted for forward compatibility (a future encoder that tags physical resolution on save) but does
    /// not otherwise affect drawing today.
    /// </summary>
    public RasterCanvas CreateCanvas(int widthPx, int heightPx, double dpi = 96)
    {
        _ = dpi;
        var surface = new RasterSurface(widthPx, heightPx, gridX: 0, gridY: 0, pixelsPerUnitX: 1, pixelsPerUnitY: 1);
        return new RasterCanvas(this, surface, pixelsPerPoint: 1);
    }

    /// <inheritdoc/>
    public override string GetCssMediaType(IEnumerable<string> mediaTypesAvailable) => "screen";

    /// <summary>Registers a font directly under <paramref name="fontFamilyName"/>, bypassing CSS <c>@font-face</c> entirely.</summary>
    public async Task AddFont(Stream stream, string? fontFamilyName)
    {
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream);
        AddFontData(memoryStream.ToArray(), fontFamilyName, default, null);
    }

    /// <inheritdoc/>
    protected override PaintColor GetColorInt(string colorName)
    {
        if (!Enum.TryParse<System.Drawing.KnownColor>(colorName, true, out var knownColor))
            return PaintColor.Empty;

        var c = System.Drawing.Color.FromKnownColor(knownColor);
        return PaintColor.FromArgb(c.A, c.R, c.G, c.B);
    }

    /// <inheritdoc/>
    protected override Image ImageFromStreamInt(Stream memoryStream) =>
        DecodedImage.TryDecode(memoryStream) ?? throw new InvalidDataException("The stream is not a recognised image format.");

    /// <inheritdoc/>
    protected override Font CreateFontInt(string family, double size, PaintFontStyle style, double weight = 400, double stretch = 100, double? obliqueSkewSinus = null, string? variations = null) =>
        MatchAndCreateFont(family, size, style, weight, stretch, obliqueSkewSinus);

    /// <inheritdoc/>
    protected override Font CreateFontInt(FontFamily family, double size, PaintFontStyle style, double weight = 400, double stretch = 100, double? obliqueSkewSinus = null, string? variations = null) =>
        MatchAndCreateFont(family.Name, size, style, weight, stretch, obliqueSkewSinus);

    private static bool IsItalic(PaintFontStyle style) => (style & PaintFontStyle.Italic) == PaintFontStyle.Italic;

    private TypefaceFont MatchAndCreateFont(string family, double size, PaintFontStyle style, double weight, double stretch, double? obliqueSkewSinus)
    {
        var query = TypefaceQuery.From(weight, stretch, IsItalic(style), null, null, obliqueSkewSinus);
        var match = _fontSet.MatchOrFallback(family, query);
        return new TypefaceFont(match.Typeface, size, match.Synthesis, obliqueSkewSinus);
    }

    /// <inheritdoc/>
    protected override Font? CreateFontForCodepointInt(string family, double size, PaintFontStyle style, double weight, double stretch, double? obliqueSkewSinus, System.Text.Rune codepoint, string? variations)
    {
        // A null here tells the caller to try the next family in the stack: never build a font for a family
        // that can't render this codepoint.
        if (!_fontSet.TryFindFamily(family, out var typefaceFamily)
            || !typefaceFamily.TryMatch(TypefaceQuery.From(weight, stretch, IsItalic(style), codepoint, null, obliqueSkewSinus), out var match))
        {
            return null;
        }

        return new TypefaceFont(match.Typeface, size, match.Synthesis, obliqueSkewSinus);
    }

    /// <inheritdoc/>
    protected override Font? CreateSystemFallbackFontForCodepointInt(double size, PaintFontStyle style, double weight, double stretch, double? obliqueSkewSinus, System.Text.Rune codepoint, EmojiPresentation presentation, string? variations)
    {
        if (!_fontSet.TryFindCoveringFamily(codepoint, presentation, out var fallbackFamily))
            return null;

        try
        {
            if (!fallbackFamily.TryMatch(TypefaceQuery.From(weight, stretch, IsItalic(style), codepoint, null, obliqueSkewSinus), out var match))
                return null;

            return new TypefaceFont(match.Typeface, size, match.Synthesis, obliqueSkewSinus);
        }
        catch
        {
            // Mirrors PdfSharpAdapter's own catch here: a candidate that passed the cmap-coverage
            // pre-check can still fail to actually load.
            return null;
        }
    }

    /// <inheritdoc/>
    protected override bool FamilyHasExplicitUnicodeRangesInt(string family) => _fontSet.HasExplicitRanges(family);

    /// <inheritdoc/>
    protected override async Task<bool> AddFontFromStream(string fontFamilyName, Stream stream, string? format, FontFaceDescriptors descriptors = default, IReadOnlyList<RuneInterval>? unicodeRanges = null)
    {
        if (format is null or "truetype" or "woff" or "woff2" or "opentype")
        {
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            AddFontData(memoryStream.ToArray(), fontFamilyName, descriptors, unicodeRanges);
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    protected override Task<bool> AddLocalFont(string fontFamilyName, string localFontFaceName, FontFaceDescriptors descriptors = default, IReadOnlyList<RuneInterval>? unicodeRanges = null)
    {
        if (!_fontSet.TryGetFontData(localFontFaceName, out var data))
            return Task.FromResult(false);

        AddFontData(data, fontFamilyName, descriptors, unicodeRanges);
        return Task.FromResult(true);
    }

    private void AddFontData(ReadOnlyMemory<byte> data, string? fontFamilyName, FontFaceDescriptors descriptors, IReadOnlyList<RuneInterval>? unicodeRanges)
    {
        var family = _fontSet.AddData(data, new AddOptions
        {
            FamilyName = fontFamilyName,
            WeightRange = descriptors.Weight,
            IsItalic = descriptors.IsItalic,
            WidthRange = descriptors.Width,
            ObliqueRange = descriptors.Oblique,
            UnicodeRanges = unicodeRanges
        });

        AddFontFamily(new NamedFontFamily(family.Name));
    }
}
