using System.Collections.Generic;

namespace PeachPDF.PdfSharpCore.Drawing
{
    /// <summary>
    /// The PDF-backend counterpart of <c>RFontPalette</c>: a resolved CSS <c>font-palette</c> selection —
    /// a CPAL palette index plus per-entry color overrides — handed to the color-glyph painter. Built by
    /// <c>GraphicsAdapter.DrawString</c> from the adapter-layer <c>RFontPalette</c>.
    /// </summary>
    internal sealed class XGlyphPalette
    {
        public XGlyphPalette(int basePaletteIndex, IReadOnlyDictionary<int, XColor> overrides)
        {
            BasePaletteIndex = basePaletteIndex;
            Overrides = overrides;
        }

        /// <summary>The selected CPAL palette index (COLR layer/stop entries resolve against this palette).</summary>
        public int BasePaletteIndex { get; }

        /// <summary>Per-entry color overrides (CPAL entry index → color); empty when there are none.</summary>
        public IReadOnlyDictionary<int, XColor> Overrides { get; }
    }
}
