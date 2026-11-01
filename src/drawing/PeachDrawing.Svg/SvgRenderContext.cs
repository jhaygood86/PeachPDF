#nullable enable

using PeachDrawing.Core;
using PeachDrawing.Text;
using PeachDrawing.Text.Unicode;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PeachDrawing.Svg;

/// <summary>
/// The <see cref="RenderContext"/> an <see cref="SvgCanvas"/> draws with: the fonts (installed ones, plus every font added), images
/// decoded for embedding, and the scale between layout units and points. A host adds what is specific to it - HTML and CSS font-family
/// aliases, <c>font-variation-settings</c> parsing, resource loading - by deriving from it, exactly as for the PDF render context.
/// </summary>
public class SvgRenderContext : RenderContext
{
    private readonly FontSet _fontSet = new();

    /// <summary>Creates a render context with every font installed on the machine already registered by name.</summary>
    public SvgRenderContext()
    {
        foreach (var familyName in FontSet.InstalledFamilyNames)
        {
            AddFontFamily(new SvgCanvasFontFamily(familyName));
        }
    }

    /// <summary>The fonts this context renders with, for a derived context that maps names onto them.</summary>
    protected FontSet FontSet => _fontSet;

    /// <summary>
    /// The scale between layout units and points: <c>1.0</c> (no scaling) draws one layout unit as one point. A host that lays out in
    /// another unit (<c>PixelsPerInch / 72</c> layout units per point) sets it before drawing anything.
    /// </summary>
    public double PixelsPerPoint { get; set; } = 1.0;

    /// <summary>Fonts are built at <c>size / PixelsPerPoint</c> points, so <see cref="PixelsPerPoint"/> is part of a cached font's identity.</summary>
    public override double LayoutUnitsPerPoint => PixelsPerPoint;

    /// <inheritdoc/>
    public override string GetCssMediaType(IEnumerable<string> mediaTypesAvailable) =>
        mediaTypesAvailable.Contains("screen") ? "screen" : "all";

    /// <summary>
    /// Adds a font to the context so canvases can draw with it. TrueType, OpenType, WOFF and WOFF2 are recognised by their content.
    /// </summary>
    /// <param name="stream">the font file; it is read to the end, and not closed</param>
    /// <param name="fontFamilyName">the family name to register it under, or <see langword="null"/> to take the name from the font file</param>
    /// <returns>a task that completes when the font has been added</returns>
    public async Task AddFont(Stream stream, string? fontFamilyName) => await AddFont(stream, fontFamilyName, default);

    /// <summary>
    /// Adds a font like <see cref="AddFont(Stream, string?)"/>, restricting which weights, widths and styles it is matched for and which
    /// characters it supplies.
    /// </summary>
    /// <param name="stream">the font file; it is read to the end, and not closed</param>
    /// <param name="fontFamilyName">the family name to register it under, or <see langword="null"/> to take the name from the font file</param>
    /// <param name="descriptors">the weight, width, slant and style ranges the font is declared for</param>
    /// <param name="unicodeRanges">the characters the font supplies, or <see langword="null"/> for all it has</param>
    /// <returns>a task that completes when the font has been added</returns>
    public async Task AddFont(Stream stream, string? fontFamilyName, FontFaceDescriptors descriptors, IReadOnlyList<RuneInterval>? unicodeRanges = null)
    {
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream);
        AddFont(memoryStream.ToArray(), fontFamilyName, descriptors, unicodeRanges);
    }

    private void AddFont(ReadOnlyMemory<byte> data, string? fontFamilyName, FontFaceDescriptors descriptors, IReadOnlyList<RuneInterval>? unicodeRanges)
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

        AddFontFamily(new SvgCanvasFontFamily(family.Name));
        OnFontFamilyAdded(family.Name);
    }

    /// <summary>
    /// Called after a font has been added to the context and its family registered, so a host can react (point a default font at the
    /// first family registered when nothing else resolves it, say). Does nothing by default.
    /// </summary>
    /// <param name="familyName">the family the added font belongs to</param>
    protected virtual void OnFontFamilyAdded(string familyName)
    {
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
        SvgImage.TryDecode(memoryStream) ?? throw new InvalidDataException("The stream is not a recognised image format.");

    /// <inheritdoc/>
    protected override Font CreateFontInt(string family, double size, PaintFontStyle style, double weight = 400, double stretch = 100, double? obliqueSkewSinus = null, string? variations = null) =>
        MatchAndCreateFont(family, size, style, weight, stretch, obliqueSkewSinus, variations);

    /// <inheritdoc/>
    protected override Font CreateFontInt(FontFamily family, double size, PaintFontStyle style, double weight = 400, double stretch = 100, double? obliqueSkewSinus = null, string? variations = null) =>
        MatchAndCreateFont(family.Name, size, style, weight, stretch, obliqueSkewSinus, variations);

    private SvgCanvasFont MatchAndCreateFont(string family, double size, PaintFontStyle style, double weight, double stretch, double? obliqueSkewSinus, string? variations)
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
    /// Turns the host's encoding of the variation axis settings a font was asked for (<see cref="RenderContext"/> passes it through as
    /// an opaque string) into the axis settings to apply. A host with no such syntax gets none.
    /// </summary>
    /// <param name="variations">the host's encoding of the settings, or <see langword="null"/> for none</param>
    /// <param name="sizeInPixels">the font size in CSS pixels, the unit the optical size axis is defined in</param>
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

    private SvgCanvasFont CreateCanvasFont(double size, TypefaceMatch match, double? obliqueSkewSinus)
    {
        // A face with a slant axis draws the oblique itself, so the canvas must not shear it as well.
        if (obliqueSkewSinus is not null && match.Typeface.Axes.Any(a => a.Tag == AxisTags.Slant))
            obliqueSkewSinus = null;

        return new SvgCanvasFont(new TypefaceFont(match.Typeface, size / PixelsPerPoint, match.Synthesis, obliqueSkewSinus), PixelsPerPoint);
    }

    /// <inheritdoc/>
    protected override Font? CreateFontForCodepointInt(string family, double size, PaintFontStyle style, double weight, double stretch, double? obliqueSkewSinus, System.Text.Rune codepoint, string? variations)
    {
        var fontStyle = WithoutSynthesisSwitches(style);

        // A null here tells the caller to try the next family in the stack: never build a font for a family that can't render this codepoint.
        if (!_fontSet.TryFindFamily(family, out var typefaceFamily)
            || !typefaceFamily.TryMatch(QueryFor(weight, stretch, IsItalic(fontStyle), codepoint, size, obliqueSkewSinus, variations), out var match))
        {
            return null;
        }

        return CreateCanvasFont(size, Restrict(match, style), obliqueSkewSinus);
    }

    /// <inheritdoc/>
    protected override Font? CreateSystemFallbackFontForCodepointInt(double size, PaintFontStyle style, double weight, double stretch, double? obliqueSkewSinus, System.Text.Rune codepoint, EmojiPresentation presentation, string? variations)
    {
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

    /// <inheritdoc/>
    protected override bool FamilyHasExplicitUnicodeRangesInt(string family) => _fontSet.HasExplicitRanges(family);

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    protected override Task<bool> AddLocalFont(string fontFamilyName, string localFontFaceName, FontFaceDescriptors descriptors = default, IReadOnlyList<RuneInterval>? unicodeRanges = null)
    {
        if (!_fontSet.TryGetFontData(localFontFaceName, out var data))
            return Task.FromResult(false);

        AddFont(data, fontFamilyName, descriptors, unicodeRanges);
        return Task.FromResult(true);
    }
}
