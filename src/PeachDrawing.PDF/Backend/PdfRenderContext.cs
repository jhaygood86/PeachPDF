// "Therefore those skilled at the unorthodox
// are infinite as heaven and earth,
// inexhaustible as the great rivers.
// When they come to an end,
// they begin again,
// like the days and months;
// they die and are reborn,
// like the four seasons."
// 
// - Sun Tsu,
// "The Art of War"

#nullable enable

using PeachDrawing.Text;
using PeachDrawing.Text.Unicode;
using PeachDrawing.Core;
using PeachDrawing.PDF.Engine.Drawing;
using PeachDrawing.PDF.Engine;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PeachDrawing.PDF
{
    /// <summary>
    /// The <see cref="RenderContext"/> a <see cref="PdfCanvas"/> draws with: the fonts (installed ones, plus every font added), images
    /// decoded for embedding, and the scale between layout units and PDF points. A host adds what is specific to it - HTML and CSS
    /// font-family aliases, <c>font-variation-settings</c> parsing, resource loading - by deriving from it.
    /// </summary>
    public class PdfRenderContext : RenderContext
    {
        /// <summary>The fonts this adapter renders with: the installed fonts plus everything registered on it.</summary>
        private readonly FontSet _fontSet;

        /// <summary>
        /// Init color resolve.
        /// </summary>
        public PdfRenderContext()
        {
            _fontSet = new FontSet();

            // The engine's static constructor already opened and parsed every system font file once,
            // process-wide (including filtering out any file that failed to parse) -
            // registering family names here from that cache instead of re-reading every file avoids paying
            // that cost again on every single PdfRenderContext construction (see the fontconfig-caching fix
            // in .claude/recent-fixes/2026-09-10-fontconfig-is-asked-once-per-family.md for the same shape
            // of bug one loop earlier in this constructor).
            foreach (var familyName in FontSet.InstalledFamilyNames)
            {
                AddFontFamily(new PdfCanvasFontFamily(familyName));
            }

        }

        /// <summary>The fonts this context renders with, for a derived context that maps names onto them.</summary>
        protected FontSet FontSet => _fontSet;

        /// <summary>
        /// The scale between layout units and true PDF points: <c>1.0</c> (no scaling) draws one layout unit as one point. A host that
        /// lays out in another unit (<c>PixelsPerInch / 72</c> layout units per point) sets it before drawing anything; <c>1.0</c> is the
        /// value of a context that has not been told, and must stay the "no scaling" case rather than repeat the unrelated 72 in "72
        /// points per inch", or every length that reads it silently scales by 72x.
        /// </summary>
        public double PixelsPerPoint { get; set; } = 1.0;

        /// <summary>
        /// Fonts here are built at <c>size / PixelsPerPoint</c> points (see <c>CreateFontInt</c>), so
        /// <see cref="PixelsPerPoint"/> is part of a cached font's identity.
        /// </summary>
        public override double LayoutUnitsPerPoint => PixelsPerPoint;

        /// <inheritdoc />
        public override string GetCssMediaType(IEnumerable<string> mediaTypesAvailable)
        {
            return mediaTypesAvailable.Contains("print") ? "print" : "all";
        }

        /// <summary>
        /// Adds a font to the context so pages can draw with it. TrueType, OpenType, WOFF and WOFF2 are recognised by their content.
        /// </summary>
        /// <param name="stream">the font file; it is read to the end, and not closed</param>
        /// <param name="fontFamilyName">the family name to register it under, or <see langword="null"/> to take the name from the font file</param>
        /// <returns>a task that completes when the font has been added</returns>
        public async Task AddFont(Stream stream, string? fontFamilyName)
        {
            await AddFont(stream, fontFamilyName, default);
        }

        internal async Task AddFont(Stream stream, string? fontFamilyName, FontFaceDescriptors descriptors, IReadOnlyList<RuneInterval>? unicodeRanges = null)
        {
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);

            AddFont(memoryStream.ToArray(), fontFamilyName, descriptors, unicodeRanges);
        }

        private void AddFont(ReadOnlyMemory<byte> data, string? fontFamilyName, FontFaceDescriptors descriptors, IReadOnlyList<RuneInterval>? unicodeRanges)
        {
            // The font set recognises WOFF/WOFF2/TrueType/OpenType by content and reads the family name from the
            // file when the caller gave none.
            var family = _fontSet.AddData(data, new AddOptions
            {
                FamilyName = fontFamilyName,
                WeightRange = descriptors.Weight,
                IsItalic = descriptors.IsItalic,
                WidthRange = descriptors.Width,
                ObliqueRange = descriptors.Oblique,
                UnicodeRanges = unicodeRanges
            });

            AddFontFamily(new PdfCanvasFontFamily(family.Name));

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

        /// <inheritdoc />
        protected override PaintColor GetColorInt(string colorName)
        {
            return Enum.TryParse<KnownColor>(colorName, true, out var knownColor)
                ? Units.Convert(System.Drawing.Color.FromKnownColor(knownColor))
                : PaintColor.Empty;
        }

        /// <inheritdoc />
        protected override Image ImageFromStreamInt(Stream memoryStream)
        {
            return new PdfCanvasImage(PdfRasterImage.FromStream(() => memoryStream));
        }

        /// <inheritdoc />
        protected override Font CreateFontInt(string family, double size, PaintFontStyle style, double weight = 400, double stretch = 100, double? obliqueSkewSinus = null, string? variations = null)
        {
            return MatchAndCreateFont(family, size, style, weight, stretch, obliqueSkewSinus, variations);
        }

        /// <inheritdoc />
        protected override Font CreateFontInt(FontFamily family, double size, PaintFontStyle style, double weight = 400, double stretch = 100, double? obliqueSkewSinus = null, string? variations = null)
        {
            return MatchAndCreateFont(((PdfCanvasFontFamily)family).Name, size, style, weight, stretch, obliqueSkewSinus, variations);
        }

        private PdfCanvasFont MatchAndCreateFont(string family, double size, PaintFontStyle style, double weight, double stretch, double? obliqueSkewSinus, string? variations)
        {
            var fontStyle = WithoutSynthesisSwitches(style);
            var isItalic = (fontStyle & PaintFontStyle.Italic) == PaintFontStyle.Italic;

            var match = _fontSet.MatchOrFallback(family, QueryFor(weight, stretch, isItalic, null, size, obliqueSkewSinus, variations));
            return CreatePdfCanvasFont(size, fontStyle, Restrict(match, style), obliqueSkewSinus);
        }

        /// <summary>
        /// The query a face is matched with. The weight, the width as a percentage of the normal width and the oblique angle of the box
        /// select the face among those of a family and set a variable face's weight, width and slant axes; the axis settings of
        /// <c>font-variation-settings</c> and the automatic optical size (for a font of <paramref name="size"/> in layout units) come after
        /// and win. The axis settings come from <see cref="ResolveVariationAxes"/>, which is specific to the host's syntax for them;
        /// everything else about building the query from these primitives is shared with every backend that creates fonts this way
        /// (<see cref="TypefaceQuery.From"/>).
        /// </summary>
        private TypefaceQuery QueryFor(double weight, double stretch, bool isItalic, System.Text.Rune? mustCover, double size, double? obliqueSkewSinus, string? variations) =>
            TypefaceQuery.From(weight, stretch, isItalic, mustCover,
                ResolveVariationAxes(variations, size / PixelsPerPoint / Units.PointsPerCssPixel),
                obliqueSkewSinus);

        /// <summary>
        /// Turns the host's encoding of the variation axis settings a font was asked for (<see cref="RenderContext"/> passes it through
        /// as an opaque string) into the axis settings to apply, together with the automatic optical size for a font of
        /// <paramref name="sizeInPixels"/>. A host with no such syntax gets none.
        /// </summary>
        /// <param name="variations">the host's encoding of the settings, or <see langword="null"/> for none</param>
        /// <param name="sizeInPixels">the font size in CSS pixels, the unit the optical size axis is defined in</param>
        protected virtual IReadOnlyList<AxisSetting>? ResolveVariationAxes(string? variations, double sizeInPixels) => null;

        /// <summary>The style bits the PDF font layer understands: the synthesis switches are not part of it.</summary>
        private static PaintFontStyle WithoutSynthesisSwitches(PaintFontStyle style)
            => style & ~(PaintFontStyle.NoSyntheticBold | PaintFontStyle.NoSyntheticItalic);

        /// <summary>
        /// Drops from a match's synthesis whatever <c>font-synthesis-weight</c>/<c>-style: none</c> forbade
        /// (<see cref="PaintFontStyle.NoSyntheticBold"/>/<see cref="PaintFontStyle.NoSyntheticItalic"/>), so the face is used as it is.
        /// </summary>
        internal static TypefaceMatch Restrict(TypefaceMatch match, PaintFontStyle style)
        {
            var synthesis = match.Synthesis;
            if ((style & PaintFontStyle.NoSyntheticBold) != 0) synthesis &= ~SyntheticStyle.Bold;
            if ((style & PaintFontStyle.NoSyntheticItalic) != 0) synthesis &= ~SyntheticStyle.Italic;
            return synthesis == match.Synthesis ? match : match with { Synthesis = synthesis };
        }

        private PdfCanvasFont CreatePdfCanvasFont(double size, PaintFontStyle fontStyle, TypefaceMatch match, double? obliqueSkewSinus)
        {
            // A face with a slant axis draws the oblique itself, so the renderer must not shear it as well.
            if (obliqueSkewSinus is not null && match.Typeface.Axes.Any(a => a.Tag == AxisTags.Slant))
            {
                obliqueSkewSinus = null;
            }

            var xFont = new TypefaceFont(match.Typeface, size / PixelsPerPoint, match.Synthesis, obliqueSkewSinus);
            return new PdfCanvasFont(xFont, PixelsPerPoint);
        }

        /// <inheritdoc />
        protected override Font? CreateFontForCodepointInt(string family, double size, PaintFontStyle style, double weight, double stretch, double? obliqueSkewSinus, System.Text.Rune codepoint, string? variations)
        {
            var fontStyle = WithoutSynthesisSwitches(style);
            var isItalic = (fontStyle & PaintFontStyle.Italic) == PaintFontStyle.Italic;

            // A null here tells the caller to try the next family in the stack: never build an TypefaceFont for a family
            // that can't render this codepoint.
            if (!_fontSet.TryFindFamily(family, out var typefaceFamily)
                || !typefaceFamily.TryMatch(QueryFor(weight, stretch, isItalic, codepoint, size, obliqueSkewSinus, variations), out var match))
            {
                return null;
            }

            return CreatePdfCanvasFont(size, fontStyle, Restrict(match, style), obliqueSkewSinus);
        }

        /// <inheritdoc />
        protected override Font? CreateSystemFallbackFontForCodepointInt(double size, PaintFontStyle style, double weight, double stretch, double? obliqueSkewSinus, System.Text.Rune codepoint, PeachDrawing.Text.Unicode.EmojiPresentation presentation, string? variations)
        {
            if (!_fontSet.TryFindCoveringFamily(codepoint, presentation, out var fallbackFamily))
                return null;

            var fontStyle = WithoutSynthesisSwitches(style);
            var isItalic = (fontStyle & PaintFontStyle.Italic) == PaintFontStyle.Italic;

            try
            {
                if (!fallbackFamily.TryMatch(QueryFor(weight, stretch, isItalic, codepoint, size, obliqueSkewSinus, variations), out var match))
                    return null;

                return CreatePdfCanvasFont(size, fontStyle, Restrict(match, style), obliqueSkewSinus);
            }
            catch
            {
                // A candidate that passed the cmap-coverage pre-check can still fail to actually load -
                // e.g. a system font (reached here for the first time, since only the last-resort scan
                // ever attempts EVERY installed font) whose file PeachPDF's own OpenType parser can't
                // fully read. Treat exactly like "no candidate found" - the caller falls back to the
                // box's own default font - rather than letting the failure propagate through ordinary
                // text layout.
                return null;
            }
        }

        /// <inheritdoc />
        protected override bool FamilyHasExplicitUnicodeRangesInt(string family) => _fontSet.HasExplicitRanges(family);

        /// <inheritdoc />
        protected override async Task<bool> AddFontFromStream(string fontFamilyName, Stream stream, string? format, FontFaceDescriptors descriptors = default, IReadOnlyList<RuneInterval>? unicodeRanges = null)
        {
            // A missing format() hint is valid CSS (it's an optional hint, not a requirement) and must
            // still be attempted - real-world stylesheets (e.g. css4.pub's Icelandic dictionary page)
            // ship bare `src: url("Font.otf")` with no format() at all. AddFont itself is already
            // format-agnostic (FontFormatConverter.ToOpenType sniffs actual byte content; TtfFontDescription
            // just walks the sfnt table directory), so there's nothing to lose by attempting the load here
            // - only explicitly-declared, genuinely unsupported formats (embedded-opentype, svg, collection)
            // should still be skipped.
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
            if (!_fontSet.TryGetFontData(localFontFaceName, out var data)) return Task.FromResult(false);

            AddFont(data, fontFamilyName, descriptors, unicodeRanges);

            return Task.FromResult(true);
        }
    }
}