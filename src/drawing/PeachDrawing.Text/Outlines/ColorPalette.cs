using PeachDrawing.Text.Internal.Fonts.OpenType;
using System.Drawing;

namespace PeachDrawing.Text.Outlines
{
    /// <summary>
    /// The colours a colour font's glyphs are painted with: one or more palettes of the same number of entries (the <c>CPAL</c>
    /// table). A colour glyph refers to an entry by index, and the palette in use chooses the colours.
    /// </summary>
    public sealed class ColorPalette
    {
        private readonly CpalTable _table;

        internal ColorPalette(CpalTable table)
        {
            _table = table;
        }

        /// <summary>How many palettes the font has.</summary>
        public int PaletteCount => _table.PaletteCount;

        /// <summary>How many colours each palette has.</summary>
        public int EntriesPerPalette => _table.EntriesPerPalette;

        /// <summary>The index of the first palette that is flagged as usable on a light background, or <see langword="null"/> when none is.</summary>
        /// <remarks>Only a version 1 palette table carries the flag. This is what CSS <c>font-palette: light</c> asks for.</remarks>
        public int? FirstLightPalette() => _table.FirstLightPalette();

        /// <summary>The index of the first palette that is flagged as usable on a dark background, or <see langword="null"/> when none is.</summary>
        /// <remarks>This is what CSS <c>font-palette: dark</c> asks for.</remarks>
        public int? FirstDarkPalette() => _table.FirstDarkPalette();

        /// <summary>Looks up one colour.</summary>
        /// <param name="paletteIndex">The palette; the first one is used when the index is out of range.</param>
        /// <param name="entryIndex">The entry within the palette.</param>
        /// <param name="color">The colour, with its alpha.</param>
        /// <returns><see langword="false"/> when the entry does not exist.</returns>
        public bool TryGetColor(int paletteIndex, int entryIndex, out Color color)
        {
            if (_table.TryGetColor(paletteIndex, entryIndex, out var rgba))
            {
                color = Color.FromArgb(rgba.A, rgba.R, rgba.G, rgba.B);
                return true;
            }

            color = default;
            return false;
        }
    }
}
