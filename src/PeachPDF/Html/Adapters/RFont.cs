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

using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Text;

namespace PeachPDF.Html.Adapters
{
    /// <summary>
    /// Adapter for platform specific font object - used to render text using specific font.
    /// </summary>
    internal abstract class RFont
    {
        /// <summary>
        /// Gets the em-size of this Font measured in the units specified by the Unit property.
        /// </summary>
        public abstract double Size { get; }

        /// <summary>
        /// The line spacing, in pixels, of this font.
        /// </summary>
        public abstract double Height { get; }

        /// <summary>
        /// Get the vertical offset of the font underline location from the top of the font.
        /// </summary>
        public abstract double UnderlineOffset { get; }

        /// <summary>
        /// Get the ascent, in pixels, of the font — the distance from the top of the font's
        /// line box down to its baseline.
        /// </summary>
        public abstract double Ascent { get; }

        /// <summary>
        /// Get the left padding, in pixels, of the font.
        /// </summary>
        public abstract double LeftPadding { get; }

        public abstract double GetWhitespaceWidth(RGraphics graphics);

        /// <summary>
        /// Whether this font actually contains a glyph for <paramref name="rune"/> (as opposed to
        /// resolving to the missing-glyph box). Drives per-codepoint font fallback: a run whose resolved
        /// font lacks a character is re-resolved against the rest of the <c>font-family</c> stack.
        /// </summary>
        public abstract bool HasGlyph(System.Text.Rune rune);

        /// <summary>
        /// Whether this font's GSUB table defines an active lookup for every OpenType feature tag
        /// <paramref name="feature"/> needs (e.g. both <c>smcp</c> and <c>c2sc</c> for
        /// <see cref="FontVariantCapsFeature.AllSmallCaps"/> - see <see cref="GsubShaper.GetFeatureTags"/>).
        /// Called from <c>CssBox.AddWord</c>'s synthesis gate, which runs during DOM/box-tree
        /// parsing - before any <see cref="RGraphics"/> exists - so this lives on <see cref="RFont"/>
        /// itself rather than the graphics abstraction, mirroring <see cref="HasGlyph"/>.
        /// </summary>
        public abstract bool SupportsFontVariantCaps(FontVariantCapsFeature feature);

        /// <summary>
        /// A stable identity for the concrete face this font renders with, used only to coalesce adjacent
        /// per-codepoint fragments that resolve to the same face into one word rather than splitting every
        /// character. Two <see cref="RFont"/>s with the same key at the same size/style render identically.
        /// </summary>
        public abstract string FaceKey { get; }

        // ---- CPAL color-palette query surface (COLR/CPAL color fonts) ---------------------------
        // Lets the CSS layer resolve `font-palette` (light/dark, @font-palette-values, palette-mix) against
        // the used font's own palettes. A non-color font reports no palettes; the defaults below make every
        // non-color RFont a no-op, so only the color-capable adapter overrides them.

        /// <summary>The number of CPAL palettes this font carries (0 for a non-color font).</summary>
        public virtual int PaletteCount => 0;

        /// <summary>The number of color entries in each CPAL palette (0 for a non-color font).</summary>
        public virtual int PaletteEntryCount => 0;

        /// <summary>The index of the first palette flagged usable with a light background, or null when none.</summary>
        public virtual int? FirstLightPalette() => null;

        /// <summary>The index of the first palette flagged usable with a dark background, or null when none.</summary>
        public virtual int? FirstDarkPalette() => null;

        /// <summary>
        /// Resolves a CPAL palette entry to its color. Returns false when the font has no palette data or the
        /// indices are out of range.
        /// </summary>
        public virtual bool TryGetPaletteColor(int paletteIndex, int entryIndex, out RColor color)
        {
            color = RColor.Empty;
            return false;
        }

        // ---- Vertical metrics query surface (vhea/vmtx/VORG) -----------------------------------
        // Lets vertical-writing-mode layout/paint consult a font's own real per-glyph vertical
        // typesetting advance and origin when it has them (mainly professional CJK vertical fonts),
        // instead of the font.Height-per-character/plain-top-of-cell approximation. A font without real
        // data reports no support; the defaults below reproduce that approximation exactly, so only the
        // OpenType-descriptor-backed adapter overrides them - mirroring the CPAL section above.

        /// <summary>Whether this font carries real OpenType vertical metrics (vhea + vmtx) to consult.</summary>
        public virtual bool HasVerticalMetrics => false;

        /// <summary>
        /// This rune's real <c>vmtx</c> advance height, in the same pixel units as <see cref="Height"/>.
        /// Only meaningful when <see cref="HasVerticalMetrics"/> is true; the default reproduces the
        /// flat per-character line-height step used when it's false.
        /// </summary>
        public virtual double GetVerticalAdvance(System.Text.Rune rune) => Height;

        /// <summary>Whether this font carries a real OpenType <c>VORG</c> table this reader trusts (see
        /// <see cref="PeachPDF.Fonts.OpenType.OpenTypeDescriptor.HasVerticalOrigin"/> for the CFF-only
        /// restriction that gates this).</summary>
        public virtual bool HasVerticalOrigin => false;

        /// <summary>
        /// This rune's real <c>VORG</c> vertical-origin Y, in the same pixel units as <see cref="Ascent"/>
        /// (baseline-relative, same convention). Only meaningful when <see cref="HasVerticalOrigin"/> is
        /// true; the default reproduces the plain top-of-cell anchor used when it's false (see
        /// <c>FragmentPainter.Text.cs</c>'s <c>PaintUprightVerticalRun</c> remarks for the derivation).
        /// </summary>
        public virtual double GetVerticalOriginY(System.Text.Rune rune) => Ascent;
    }
}