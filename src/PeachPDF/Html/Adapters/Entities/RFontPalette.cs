using System.Collections.Generic;

namespace PeachPDF.Html.Adapters.Entities
{
    /// <summary>
    /// A fully-resolved CSS <c>font-palette</c> selection carried alongside the text color into the graphics
    /// adapter. The <c>light</c>/<c>dark</c> keywords, <c>@font-palette-values</c> base-palette, and
    /// <c>palette-mix()</c> are all resolved to a concrete <see cref="BasePaletteIndex"/> (+ per-entry
    /// <see cref="Overrides"/>) on the CSS side, so the PDF backend only looks up an index and applies overrides.
    /// A <c>null</c> <see cref="RFontPalette"/> means the default (palette 0, no overrides) — the unchanged path.
    /// </summary>
    internal sealed record RFontPalette(int BasePaletteIndex, IReadOnlyList<KeyValuePair<int, RColor>> Overrides);
}
