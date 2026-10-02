using PeachDrawing.Text;
using PeachDrawing.Text.Outlines;
using PeachDrawing.Text.Shaping;
using PeachDrawing.Text.Unicode;
using System;
using System.Text;

namespace PeachDrawing.Core
{
    /// <summary>
    /// A <see cref="Font"/> built directly from a <see cref="PeachDrawing.Text.Typeface"/> and a size, with
    /// no backend-specific wrapper (a PDF font resource, a platform font handle, ...) underneath - every
    /// member below reads only <see cref="Typeface"/>/<see cref="PeachDrawing.Text.TypefaceMetrics"/> data,
    /// scaled by <see cref="Size"/>. This is what a <see cref="Canvas"/> backend with no host-specific font
    /// representation of its own (a standalone raster canvas; any future backend in the same position) uses
    /// directly, and it is also the shape every backend's own metric computation reduces to once its
    /// host-specific extras (PDF font-resource identity, a platform handle, ...) are set aside.
    /// </summary>
    /// <remarks>
    /// Every metric here is the *unscaled* value in the same unit as <see cref="Size"/> itself - no
    /// rounding, no host-specific unit conversion (a PDF backend's internal-layout-unit scaling, an HTML
    /// renderer's CSS-pixel-grid rounding for `line-height: normal`, etc.). A backend that needs either of
    /// those applies it on top of these raw numbers, rather than this type guessing at a host it doesn't
    /// know about.
    /// </remarks>
    public sealed class TypefaceFont : Font
    {
        /// <summary>Builds a font directly from <paramref name="typeface"/> at <paramref name="size"/>, with no backend-specific wrapper underneath.</summary>
        /// <param name="typeface">the matched face this font reads every metric/glyph from</param>
        /// <param name="size">the em-size to scale <paramref name="typeface"/>'s design-unit metrics by</param>
        /// <param name="syntheticStyle">which of the requested style attributes <paramref name="typeface"/> doesn't already have and must be faked</param>
        /// <param name="obliqueSkewSinus">the sine of a declared CSS Fonts 4 <c>oblique &lt;angle&gt;</c>, if any - see <see cref="Font.ObliqueSkewSinus"/></param>
        public TypefaceFont(Typeface typeface, double size, SyntheticStyle syntheticStyle = SyntheticStyle.None, double? obliqueSkewSinus = null)
        {
            ArgumentNullException.ThrowIfNull(typeface);

            Typeface = typeface;
            Size = size;
            SyntheticStyle = syntheticStyle;
            ObliqueSkewSinus = obliqueSkewSinus;
        }

        /// <inheritdoc/>
        public override double Size { get; }

        /// <inheritdoc/>
        public override Typeface Typeface { get; }

        /// <inheritdoc/>
        public override SyntheticStyle SyntheticStyle { get; }

        /// <inheritdoc/>
        public override double? ObliqueSkewSinus { get; }

        private TypefaceMetrics Metrics => Typeface.Metrics;

        private double ScaleUnits(int designUnits) => Size * designUnits / Metrics.UnitsPerEm;

        /// <summary>The distance from one baseline to the next when lines are set solid (the typeface's own cell, not a CSS <c>line-height</c> approximation).</summary>
        public override double Height => ScaleUnits(Metrics.LineSpacing);

        /// <inheritdoc/>
        public override double Ascent => ScaleUnits(Metrics.CellAscent);

        /// <inheritdoc/>
        public override double UnderlineOffset => Height - ScaleUnits(Metrics.CellDescent) + 1;

        /// <inheritdoc cref="Font.UnderlineThickness"/>
        /// <remarks>Falls back to the base class's fixed default when the font records a thickness of 0 (a poorly authored font, not a real zero-width stroke) - see <see cref="Font.UnderlineThickness"/>'s own remarks.</remarks>
        public override double UnderlineThickness => Metrics.UnderlineThickness > 0 ? ScaleUnits(Metrics.UnderlineThickness) : base.UnderlineThickness;

        /// <inheritdoc/>
        public override double UnderlinePosition => ScaleUnits(Metrics.UnderlinePosition);

        /// <inheritdoc/>
        public override double LeftPadding => Height / 6d;

        /// <summary>The font's own real ascent + descent + line-gap (CSS 2.1 §10.8.1's `line-height: normal`), unrounded.</summary>
        public override double NormalLineHeight => ScaleUnits(Metrics.NormalLineAscent) + ScaleUnits(Metrics.NormalLineDescent) + ScaleUnits(Metrics.NormalLineGap);

        private double _whitespaceWidth = -1;

        /// <inheritdoc/>
        public override double GetWhitespaceWidth(Canvas graphics) =>
            _whitespaceWidth >= 0 ? _whitespaceWidth : _whitespaceWidth = graphics.MeasureString(" ", this).Width;

        /// <inheritdoc/>
        public override bool HasGlyph(Rune rune) => Typeface.HasGlyph(rune);

        /// <inheritdoc/>
        public override bool MatchesEmojiPresentation(Rune baseCodepoint, EmojiPresentation presentation) =>
            Typeface.MatchesEmojiPresentation(baseCodepoint, presentation);

        /// <inheritdoc/>
        public override bool SupportsFontVariantCaps(CapsMode feature) => Typeface.SupportsFeatures(Shaper.GetFeatureTags(feature));

        /// <inheritdoc/>
        public override bool SupportsFontVariantPosition(SubSuperMode feature) => Typeface.SupportsFeatures(Shaper.GetFeatureTags(feature));

        /// <inheritdoc/>
        public override (double SizeScale, double BaselineShift)? GetSubSuperscriptMetrics(bool superscript) =>
            Typeface.TryGetScriptPosition(superscript ? ScriptPlacement.Superscript : ScriptPlacement.Subscript, out var position)
                ? (position.SizeScale, position.BaselineShift)
                : null;

        /// <inheritdoc/>
        public override string FaceKey => _faceKey ??= Typeface.ContentHash + "/" + (int)SyntheticStyle + "/" + Typeface.VariationKey;

        private string? _faceKey;

        // ---- CPAL color-palette query surface (COLR/CPAL color fonts) --------------------------

        private ColorPalette? ColorPalette => Typeface.ColorPalette;

        /// <inheritdoc/>
        public override int PaletteCount => ColorPalette?.PaletteCount ?? 0;

        /// <inheritdoc/>
        public override int PaletteEntryCount => ColorPalette?.EntriesPerPalette ?? 0;

        /// <inheritdoc/>
        public override int? FirstLightPalette() => ColorPalette?.FirstLightPalette();

        /// <inheritdoc/>
        public override int? FirstDarkPalette() => ColorPalette?.FirstDarkPalette();

        /// <inheritdoc/>
        public override bool TryGetPaletteColor(int paletteIndex, int entryIndex, out PaintColor color)
        {
            if (ColorPalette is { } cpal && cpal.TryGetColor(paletteIndex, entryIndex, out var c))
            {
                color = PaintColor.FromArgb(c.A, c.R, c.G, c.B);
                return true;
            }

            color = PaintColor.Empty;
            return false;
        }

        // ---- Vertical metrics query surface (vhea/vmtx/VORG) -----------------------------------

        /// <inheritdoc/>
        public override bool HasVerticalMetrics => Typeface.HasVerticalMetrics;

        /// <inheritdoc/>
        public override double GetVerticalAdvance(Rune rune) => ScaleDesignUnits(rune, static (typeface, glyph) => typeface.GetVerticalAdvance(glyph));

        /// <inheritdoc/>
        public override bool HasVerticalOrigin => Typeface.HasVerticalOrigin;

        /// <inheritdoc/>
        public override double GetVerticalOriginY(Rune rune) => ScaleDesignUnits(rune, static (typeface, glyph) => typeface.GetVerticalOrigin(glyph).Y);

        private double ScaleDesignUnits(Rune rune, Func<Typeface, ushort, int> designUnits)
        {
            Typeface.TryMapRune(rune, out var glyph);
            return ScaleUnits(designUnits(Typeface, glyph));
        }

        // ---- MATH table query surface (mathematical typesetting fonts) ------------------------

        /// <inheritdoc/>
        public override bool HasMathTable => Typeface.HasMathData;

        /// <inheritdoc/>
        public override PeachDrawing.Text.OpenType.MathTable? MathTable => Typeface.MathData;

        /// <inheritdoc/>
        public override double FontUnitsPerEm => Metrics.UnitsPerEm;

        /// <inheritdoc/>
        public override int GetGlyphIndex(Rune rune) => Typeface.TryMapRune(rune, out var glyph) ? glyph : 0;

        /// <inheritdoc/>
        public override int GetGlyphAdvanceWidthDesignUnits(int glyphIndex) => Typeface.GetAdvance((ushort)glyphIndex);

        /// <inheritdoc/>
        public override double? XHeightEm => Metrics is { HasMeasuredXHeight: true, UnitsPerEm: > 0 } ? (double)Metrics.XHeight / Metrics.UnitsPerEm : null;

        /// <inheritdoc/>
        public override double? CapHeightEm => Metrics is { UnitsPerEm: > 0, CapHeight: > 0 } ? (double)Metrics.CapHeight / Metrics.UnitsPerEm : null;
    }
}
