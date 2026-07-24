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
        private double _underlineOffset = -1;

        /// <summary>
        /// Cached font height.
        /// </summary>
        private double _height = -1;

        /// <summary>
        /// Cached font ascent.
        /// </summary>
        private double _ascent = -1;

        /// <summary>
        /// Cached font whitespace width.
        /// </summary>
        private double _whitespaceWidth = -1;


        /// <summary>
        /// Init.
        /// </summary>
        public FontAdapter(XFont font, double pixelsPerPoint)
        {
            Font = font;
            PixelsPerPoint = pixelsPerPoint;
        }

        /// <summary>
        /// the underline win-forms font.
        /// </summary>
        public XFont Font { get; }

        private double PixelsPerPoint { get; set; }

        public override double Size => Font.Size;

        public override double UnderlineOffset => _underlineOffset;

        public override double Height => _height * PixelsPerPoint;

        public override double Ascent => _ascent * PixelsPerPoint;

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

        /// <summary>
        /// Set font metrics to be cached for the font for future use.
        /// </summary>
        /// <param name="height">the full height of the font</param>
        /// <param name="underlineOffset">the vertical offset of the font underline location from the top of the font.</param>
        /// <param name="ascent">the distance from the top of the font's line box down to its baseline</param>
        internal void SetMetrics(int height, int underlineOffset, int ascent)
        {
            _height = height;
            _underlineOffset = underlineOffset;
            _ascent = ascent;
        }
    }
}