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

using PeachPDF.Fonts.OpenType;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Text;
using System;

namespace PeachPDF.Adapters
{
    /// <summary>
    /// Adapter for WinForms Font object for core.
    /// </summary>
    internal sealed class FontAdapter : RFont
    {
        /// <summary>
        /// the vertical offset of the font underline location from the top of the font.
        /// </summary>
        private readonly double _underlineOffset;

        /// <summary>
        /// Cached font height.
        /// </summary>
        private readonly double _height;

        /// <summary>
        /// Cached font ascent.
        /// </summary>
        private readonly double _ascent;

        /// <summary>
        /// Cached font whitespace width.
        /// </summary>
        private double _whitespaceWidth = -1;


        /// <summary>
        /// Init. Resolves <see cref="Height"/>/<see cref="Ascent"/>/<see cref="UnderlineOffset"/>
        /// eagerly, right here, rather than lazily on this font's first <c>RGraphics.MeasureString</c>
        /// call (as a previous version of this constructor did): <paramref name="font"/>'s own descriptor/
        /// metrics are already fully resolved by the time <c>XFont</c>'s constructor returns
        /// (<c>XFont.Initialize</c> calls <c>CreateDescriptorAndInitializeFontMetrics</c> synchronously), so
        /// there was never a real data dependency on "a string having been measured first" - only an
        /// accident of where this arithmetic used to live. Reading <see cref="Height"/>/<see cref="Ascent"/>
        /// before this font's first <c>MeasureString</c> call used to read back a stale, pre-resolution
        /// sentinel, which cost real debugging time in both the HTML and SVG vertical-writing-mode text
        /// pipelines (see <c>CssLayoutEngine.NaturalWordSize</c>'s and <c>SvgRenderer.LayoutGlyphs</c>'s own
        /// remarks) before either pipeline had gotten around to actually measuring a string with this font.
        /// </summary>
        public FontAdapter(XFont font, double pixelsPerPoint)
        {
            Font = font;
            PixelsPerPoint = pixelsPerPoint;

            // Read ascent/descent/em-height directly off the font's OWN already-resolved descriptor
            // instead of re-deriving them via XFontFamily.GetCellAscent/GetCellDescent/GetEmHeight, which
            // re-resolve a font by its own internal name (e.g. "Source Code Pro" - not the CSS-facing
            // family alias that was actually registered) through IFontResolver - for a custom/@font-face-
            // registered family this can resolve to an entirely unrelated font, and even when it does find
            // something, it bypasses the per-instance cache routing that keeps two PdfGenerators' same-
            // named custom fonts from colliding (see XFont.Descriptor and XGlyphTypeface.OwningInstanceResolver).
            var descriptor = font.Descriptor;
            var descent = font.Size * descriptor.Descender / descriptor.UnitsPerEm;
            var ascent = font.Size * descriptor.Ascender / descriptor.UnitsPerEm;
            // XFont.Height (int, System.Drawing.Font-style API) rounds up to a whole point via
            // Math.Ceiling - harmless at an ordinary font.Size, but collapses to exactly 1 for any
            // sub-1pt size, discarding all proportional information. This adapter's own Height then
            // multiplies that lossy value back up by PixelsPerPoint (below) to undo CreateFontInt's own
            // size/PixelsPerPoint division - a font constructed sub-1pt (any PixelsPerInch/ShrinkToFit/
            // ScaleToPageSize scale where PixelsPerPoint > ~7) would land on exactly PixelsPerPoint
            // itself, not the font's real line height (issue #814's line-height family). GetHeight() is
            // XFont's own double-precision equivalent, with no such collapse.
            _height = font.GetHeight();
            // Height above; UnderlineOffset/Ascent below round to whole points too, but only after
            // scaling by PixelsPerPoint (in the properties, not here) - rounding this font's own tiny
            // constructed size first, the same way Height used to via XFont.Height, would collapse a
            // sub-1pt ascent/descent to exactly 0 before PixelsPerPoint ever gets a chance to restore
            // real magnitude, for the identical reason.
            _underlineOffset = _height - descent + 1f;
            _ascent = ascent;
        }

        /// <summary>
        /// the underline win-forms font.
        /// </summary>
        public XFont Font { get; }

        private double PixelsPerPoint { get; set; }

        public override double Size => Font.Size;

        public override double UnderlineOffset => Math.Round(_underlineOffset * PixelsPerPoint);

        public override double Height => _height * PixelsPerPoint;

        public override double Ascent => Math.Round(_ascent * PixelsPerPoint);

        public override double LeftPadding => Height / 6f;


        public override double GetWhitespaceWidth(RGraphics graphics)
        {
            if (_whitespaceWidth < 0)
            {
                _whitespaceWidth = graphics.MeasureString(" ", this).Width;
            }

            return _whitespaceWidth;
        }

        public override bool HasGlyph(System.Text.Rune rune) => Font.Descriptor?.HasGlyph(rune) ?? false;

        public override bool SupportsFontVariantCaps(FontVariantCapsFeature feature) =>
            Font.Descriptor?.SupportsFeatureTags(GsubShaper.GetFeatureTags(feature)) ?? false;

        public override string FaceKey => Font.GlyphTypeface.Key;

        // ---- CPAL color-palette query surface --------------------------------------------------
        // Backed by the font's OpenTypeDescriptor.ColorPalette (the CPAL table). Null for a non-color font,
        // in which case each member falls back to the RFont "no palettes" default.

        private CpalTable? ColorPalette => Font.Descriptor is { IsColorFont: true } d ? d.ColorPalette : null;

        public override int PaletteCount => ColorPalette?.PaletteCount ?? 0;

        public override int PaletteEntryCount => ColorPalette?.EntriesPerPalette ?? 0;

        public override int? FirstLightPalette() => ColorPalette?.FirstLightPalette();

        public override int? FirstDarkPalette() => ColorPalette?.FirstDarkPalette();

        public override bool TryGetPaletteColor(int paletteIndex, int entryIndex, out RColor color)
        {
            if (ColorPalette is { } cpal && cpal.TryGetColor(paletteIndex, entryIndex, out var c))
            {
                color = RColor.FromArgb(c.A, c.R, c.G, c.B);
                return true;
            }

            color = RColor.Empty;
            return false;
        }

        // ---- Vertical metrics query surface -----------------------------------------------------
        // Backed by the font's OpenTypeDescriptor's real vhea/vmtx/VORG parsing, converted through the
        // same design-units-to-pixels formula the constructor above already uses for _ascent/_height.

        public override bool HasVerticalMetrics => Font.Descriptor?.HasVerticalMetrics ?? false;

        public override double GetVerticalAdvance(System.Text.Rune rune) =>
            ScaleDesignUnits(rune, Height, static (descriptor, glyphIndex) => descriptor.GlyphIndexToVerticalAdvance(glyphIndex));

        public override bool HasVerticalOrigin => Font.Descriptor?.HasVerticalOrigin ?? false;

        public override double GetVerticalOriginY(System.Text.Rune rune) =>
            ScaleDesignUnits(rune, Ascent, static (descriptor, glyphIndex) => descriptor.GlyphIndexToVerticalOrigin(glyphIndex).Y);

        /// <summary>
        /// Shared by <see cref="GetVerticalAdvance"/>/<see cref="GetVerticalOriginY"/> - both resolve
        /// <paramref name="rune"/> to a glyph index and scale a raw design-units value from
        /// <see cref="OpenTypeDescriptor"/> by the exact same formula the constructor above already uses
        /// for <c>_ascent</c>/<c>_height</c> (<c>Font.Size * designUnits / UnitsPerEm * PixelsPerPoint</c>);
        /// only which descriptor accessor supplies the design-units value, and the no-descriptor
        /// fallback, differ between the two callers.
        /// </summary>
        private double ScaleDesignUnits(System.Text.Rune rune, double fallback, System.Func<OpenTypeDescriptor, int, int> designUnits)
        {
            var descriptor = Font.Descriptor;
            if (descriptor is null) return fallback;

            var glyphIndex = descriptor.CharCodeToGlyphIndex(rune);
            return Font.Size * designUnits(descriptor, glyphIndex) / descriptor.UnitsPerEm * PixelsPerPoint;
        }
    }
}