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

using PeachDrawing.Text.Unicode;
using PeachPDF.Html.Adapters.Entities;
using PeachDrawing.Text.Internal.Text;

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
        /// The font's own real underline-stroke thickness (OpenType <c>post.underlineThickness</c>,
        /// scaled to this font's size the same way <see cref="UnderlineOffset"/> is), consulted only when
        /// CSS <c>text-decoration-thickness: from-font</c> is used (CSS Text Decoration 4 §3.3) - the
        /// property's initial value (<c>auto</c>) deliberately does not read this, to preserve this
        /// engine's pre-existing fixed decoration-line thickness exactly. Defaults to <c>1</c> (this
        /// engine's own pre-existing hardcoded decoration thickness), mirroring <see cref="NormalLineHeight"/>'s
        /// pattern of a plain default for every <see cref="RFont"/> except the OpenType-descriptor-backed
        /// adapter, which overrides it with the font's real metric.
        /// </summary>
        public virtual double UnderlineThickness => 1;

        /// <summary>
        /// The font's own real preferred underline offset (OpenType <c>post.underlinePosition</c>,
        /// scaled to this font's size the same way <see cref="UnderlineThickness"/> is), consulted only
        /// when CSS <c>text-underline-position: from-font</c> is used
        /// (<see href="https://www.w3.org/TR/css-text-decor-4/#text-underline-position-property">css-text-decor-4
        /// §2.5</see> - <c>from-font</c> does not exist in css-text-decor-3 at all) - a negative value moves the line below the baseline, matching
        /// <c>post.underlinePosition</c>'s own sign convention. Defaults to <c>0</c> (at the baseline)
        /// for every <see cref="RFont"/> except the OpenType-descriptor-backed adapter, which overrides
        /// it with the font's real metric - the same plain-default pattern <see cref="UnderlineThickness"/>
        /// uses.
        /// </summary>
        public virtual double UnderlinePosition => 0;

        /// <summary>
        /// Get the ascent, in pixels, of the font — the distance from the top of the font's
        /// line box down to its baseline.
        /// </summary>
        public abstract double Ascent { get; }

        /// <summary>
        /// The exact offset from <c>DrawString</c>'s top anchor to the baseline it paints at. Layout uses
        /// <see cref="Ascent"/>, which some adapters round for stable box geometry; paint effects that must
        /// sit a precise distance from the rendered glyphs use this unrounded counterpart instead.
        /// </summary>
        public virtual double TextBaselineOffset => Ascent;

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
        /// <see cref="FontVariantCapsFeature.AllSmallCaps"/> - see <see cref="GsubShaper.GetFeatureTags(FontVariantCapsFeature)"/>).
        /// Called from <c>CssBox.AddWord</c>'s synthesis gate, which runs during DOM/box-tree
        /// parsing - before any <see cref="RGraphics"/> exists - so this lives on <see cref="RFont"/>
        /// itself rather than the graphics abstraction, mirroring <see cref="HasGlyph"/>.
        /// </summary>
        public abstract bool SupportsFontVariantCaps(FontVariantCapsFeature feature);

        /// <summary>
        /// Whether this font's GSUB table defines an active lookup for the OpenType feature tag
        /// <paramref name="feature"/> needs (<c>subs</c> or <c>sups</c> - see
        /// <see cref="GsubShaper.GetFeatureTags(FontVariantPositionFeature)"/>). Answering false is what
        /// makes a run take the synthesized sub/superscript path instead, which CSS Fonts 4 requires as
        /// the fallback; like <see cref="SupportsFontVariantCaps"/> this is asked during box-tree parsing,
        /// before any <see cref="RGraphics"/> exists.
        /// </summary>
        public abstract bool SupportsFontVariantPosition(FontVariantPositionFeature feature);

        /// <summary>
        /// This font's own recommended geometry for a synthesized <paramref name="superscript"/> (or
        /// subscript), as fractions of the em: the glyph scale factor, and how far the synthesized
        /// baseline sits from the main one, always positive. Null when the font states nothing usable,
        /// leaving the caller to fall back to representative ratios.
        /// </summary>
        public abstract (double SizeScale, double BaselineShift)? GetSubSuperscriptMetrics(bool superscript);

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
        /// <see cref="PeachDrawing.Text.Internal.Fonts.OpenType.OpenTypeDescriptor.HasVerticalOrigin"/> for the CFF-only
        /// restriction that gates this).</summary>
        public virtual bool HasVerticalOrigin => false;

        /// <summary>
        /// This rune's real <c>VORG</c> vertical-origin Y, in the same pixel units as <see cref="Ascent"/>
        /// (baseline-relative, same convention). Only meaningful when <see cref="HasVerticalOrigin"/> is
        /// true; the default reproduces the plain top-of-cell anchor used when it's false (see
        /// <c>FragmentPainter.Text.cs</c>'s <c>PaintUprightVerticalRun</c> remarks for the derivation).
        /// </summary>
        public virtual double GetVerticalOriginY(System.Text.Rune rune) => Ascent;

        /// <summary>
        /// The used value of `line-height: normal` (CSS 2.1 §10.8.1), intended to be in the same pixel
        /// units as <see cref="Height"/>/<see cref="Ascent"/>. Real browsers resolve this from the font's
        /// own ascent/descent/line-gap metrics rather than a flat multiplier; the default here reproduces
        /// the flat 1.2×-font-size approximation used before real per-font metrics were available (issue
        /// #956), so only the OpenType-descriptor-backed adapter overrides it - mirroring the vertical-
        /// metrics section above. Note this default is only faithful to that unit contract for an
        /// <see cref="RFont"/> whose own <see cref="Size"/> is already in that same space;
        /// <see cref="PeachPDF.Adapters.FontAdapter"/>'s <see cref="Size"/> is not (it's a true, unscaled
        /// point size - see its own remarks), which is exactly why it can't just inherit this default and
        /// overrides the property instead.
        /// </summary>
        public virtual double NormalLineHeight => 1.2 * Size;

        /// <summary>
        /// Whether this font is one to prefer for <paramref name="baseCodepoint"/> when it must be drawn in
        /// <paramref name="presentation"/> (CSS <c>font-variant-emoji</c>): its own variation-sequence data
        /// says so, or it is a colour font for emoji / an outline font for text. Every font accepts
        /// <see cref="EmojiPresentation.NoPreference"/>; the default answers true for every request so a
        /// font with no such data never loses a match it could not judge.
        /// </summary>
        public virtual bool MatchesEmojiPresentation(System.Text.Rune baseCodepoint, PeachDrawing.Text.Unicode.EmojiPresentation presentation) => true;

        // ---- MATH table query surface (mathematical typesetting fonts) ------------------------
        // Lets MathLayoutEngine/MathRenderer read a font's OpenType MATH table (constants, per-glyph
        // italics correction/top-accent attachment, stretchy glyph variants) without depending on the
        // concrete PDF backend, mirroring the CPAL/vertical-metrics query surfaces above. A font
        // without one (almost all fonts - only dedicated math fonts carry a MATH table) reports none;
        // the defaults below make every non-math RFont a no-op, so only the OpenType-descriptor-backed
        // adapter overrides them.

        /// <summary>Whether this font carries a MATH table.</summary>
        public virtual bool HasMathTable => false;

        /// <summary>This font's parsed MATH table, or null if it has none.</summary>
        public virtual PeachDrawing.Text.Internal.Fonts.OpenType.MathTable? MathTable => null;

        /// <summary>This font's design-units-per-em (e.g. 1000 or 2048) - <see cref="MathTable"/>'s
        /// design-unit values need scaling by <c>Size / FontUnitsPerEm</c> to become points. 0 when
        /// this font has no <see cref="MathTable"/> (nothing to scale).</summary>
        public virtual double FontUnitsPerEm => 0;

        /// <summary>This rune's glyph index in this font (0/<c>.notdef</c> if unmapped) - needed to look
        /// a glyph up in <see cref="MathTable"/>'s per-glyph tables (italics correction, top-accent
        /// attachment, stretchy variants) by id rather than by character.</summary>
        public virtual int GetGlyphIndex(System.Text.Rune rune) => 0;

        /// <summary>This glyph's real horizontal advance width, in font design units (see
        /// <see cref="FontUnitsPerEm"/> for the scale) - the font's own <c>hmtx</c> table, as opposed to
        /// a <see cref="MathTable"/> <c>MathVariants</c> entry's <c>AdvanceMeasurement</c> (the vertical
        /// growth-direction extent only, not width). Needed because a stretched glyph - a pre-sized
        /// size variant, or an assembled shape's parts - is a different, wider glyph than the base
        /// character <see cref="MathTable"/> was looked up by, so its own <c>hmtx</c> advance is the only
        /// source for the actual space it needs when drawn. 0 when this font can't resolve one (no
        /// descriptor).</summary>
        public virtual int GetGlyphAdvanceWidthDesignUnits(int glyphIndex) => 0;

        // ---- Font-relative CSS units (CSS Values and Units 4 §6.1.1) ---------------------------
        // The measurements ex/ch/cap/ic are defined by, each as a fraction of the em, so a caller can
        // scale it by whatever em it carries without knowing this font's size space. Null means "not
        // determinable" and lets the caller take the spec's fallback (see FontMetricRatios.Approximate).

        /// <summary>The font's real x-height as a fraction of the em, or null when it doesn't carry one.</summary>
        public virtual double? XHeightEm => null;

        /// <summary>The font's cap height as a fraction of the em, or null when unknown.</summary>
        public virtual double? CapHeightEm => null;

        /// <summary>
        /// The advance width of <paramref name="rune"/>'s glyph as a fraction of the em, or null when
        /// this font has no glyph for it (or no metrics at all). Built from <see cref="GetGlyphIndex"/>/
        /// <see cref="GetGlyphAdvanceWidthDesignUnits"/>/<see cref="FontUnitsPerEm"/>, so it needs no
        /// override of its own.
        /// </summary>
        public double? GetAdvanceEm(System.Text.Rune rune)
        {
            var unitsPerEm = FontUnitsPerEm;
            if (unitsPerEm <= 0) return null;

            var glyph = GetGlyphIndex(rune);
            if (glyph == 0) return null;

            var advance = GetGlyphAdvanceWidthDesignUnits(glyph);
            return advance > 0 ? advance / unitsPerEm : null;
        }
    }
}