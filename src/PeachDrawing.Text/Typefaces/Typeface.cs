using PeachDrawing.Text.Internal.Fonts;
using PeachDrawing.Text.Internal.Text;
using PeachDrawing.Text.Unicode;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Text;

namespace PeachDrawing.Text
{
    /// <summary>
    /// One face of a family: what a <see cref="TypefaceFamily"/> hands back when it matches a
    /// <see cref="TypefaceQuery"/>. It has no size; the size of text is the caller's to keep.
    /// </summary>
    /// <remarks>
    /// Two typefaces are equal when they read the same font data, whichever query matched them. A typeface stays
    /// valid for as long as it is referenced, including after the <see cref="FontSet"/> that found it is gone.
    /// </remarks>
    public sealed class Typeface : IEquatable<Typeface>
    {
        internal Typeface(LoadedTypeface face)
        {
            Face = face;
        }

        /// <summary>The engine's own view of this face; PeachPDF's PDF writer reads what it needs through it for now.</summary>
        internal LoadedTypeface Face { get; }

        /// <summary>The family name the font file declares, in English, such as <c>Arial</c>.</summary>
        public string FamilyName => Face.FamilyName;

        /// <summary>The style name the font file declares, in English, such as <c>Bold Italic</c>.</summary>
        public string StyleName => Face.StyleName;

        /// <summary>Whether the font file declares the face bold (in its OS/2 table).</summary>
        public bool IsBold => Face.IsBold;

        /// <summary>Whether the font file declares the face italic (in its OS/2 table).</summary>
        public bool IsItalic => Face.IsItalic;

        /// <summary>The vertical dimensions of the face, in design units.</summary>
        public TypefaceMetrics Metrics => _metrics ??= new TypefaceMetrics(Face.Descriptor);

        private TypefaceMetrics? _metrics;

        /// <summary>
        /// Whether the face draws colour glyphs as vector fills, which is to say it has COLR and CPAL tables over TrueType
        /// outlines. A colour font with CFF outlines reports <see langword="false"/>.
        /// </summary>
        public bool HasColorGlyphs => Face.Descriptor.IsColorFont;

        /// <summary>
        /// Finds the glyph a character is drawn with, through the font's <c>cmap</c>. Characters of the Basic Multilingual
        /// Plane are found in the format 4 subtable; the others need a format 12 subtable.
        /// </summary>
        /// <param name="rune">The character.</param>
        /// <param name="glyph">The glyph, when the font has one for the character.</param>
        /// <returns><see langword="false"/> when the font maps the character to no glyph but the missing-glyph one.</returns>
        public bool TryMapRune(Rune rune, out ushort glyph)
        {
            var index = Face.Descriptor.CharCodeToGlyphIndex(rune);
            glyph = (ushort)index;
            return index != 0;
        }

        /// <summary>Whether the font has a glyph of its own for a character, which is to say <see cref="TryMapRune"/> succeeds.</summary>
        /// <param name="rune">The character.</param>
        public bool HasGlyph(Rune rune) => Face.Descriptor.HasGlyph(rune);

        /// <summary>The horizontal advance of a glyph, in design units (the <c>hmtx</c> table).</summary>
        /// <remarks>Glyphs past the last one with a metric of its own share the last metric, as in a monospaced font.</remarks>
        /// <param name="glyph">The glyph.</param>
        public int GetAdvance(ushort glyph) => Face.Descriptor.GlyphIndexToWidth(glyph);

        /// <summary>
        /// Whether the font has vertical metrics of its own (<c>vhea</c> and <c>vmtx</c>). Without them
        /// <see cref="GetVerticalAdvance"/> answers one em, which is what the OpenType specification allows.
        /// </summary>
        public bool HasVerticalMetrics => Face.Descriptor.HasVerticalMetrics;

        /// <summary>
        /// The advance of a glyph along the vertical axis that a vertical run of text stacks its glyphs on, in design units.
        /// </summary>
        /// <param name="glyph">The glyph.</param>
        public int GetVerticalAdvance(ushort glyph) => Face.Descriptor.GlyphIndexToVerticalAdvance(glyph);

        /// <summary>
        /// Whether the font has a <c>VORG</c> table that is trusted. The specification allows one only in a font with CFF
        /// outlines, so a TrueType-outline font never reports it, whatever tables it carries.
        /// </summary>
        public bool HasVerticalOrigin => Face.Descriptor.HasVerticalOrigin;

        /// <summary>
        /// The vertical origin of a glyph, in design units relative to its horizontal origin: where the glyph hangs from
        /// when text is set vertically.
        /// </summary>
        /// <param name="glyph">The glyph.</param>
        public Point GetVerticalOrigin(ushort glyph)
        {
            var (x, y) = Face.Descriptor.GlyphIndexToVerticalOrigin(glyph);
            return new Point(x, y);
        }

        /// <summary>
        /// How the font's designer wants subscripts or superscripts drawn (the recommended values of the <c>OS/2</c> table).
        /// </summary>
        /// <param name="placement">Which of the two is wanted.</param>
        /// <param name="position">The size and offset to use.</param>
        /// <returns>
        /// <see langword="false"/> when the font has no usable recommendation, which is when a caller falls back to
        /// ratios of its own.
        /// </returns>
        public bool TryGetScriptPosition(ScriptPlacement placement, out ScriptPosition position)
        {
            if (Face.Descriptor.GetSubSuperscriptMetrics(placement == ScriptPlacement.Superscript) is { } found)
            {
                position = new ScriptPosition(found.SizeScale, found.BaselineShift);
                return true;
            }

            position = default;
            return false;
        }

        /// <summary>
        /// Whether the font's <c>GSUB</c> table has an active lookup for every one of the OpenType feature tags.
        /// </summary>
        /// <remarks>
        /// Each tag is checked on its own, under the default script and language of the font, so the answer says whether
        /// the feature exists in the font at all and not whether it applies to the script of a particular piece of text.
        /// </remarks>
        /// <param name="tags">Four-letter feature tags such as <c>smcp</c>.</param>
        public bool SupportsFeatures(IReadOnlySet<string> tags)
        {
            ArgumentNullException.ThrowIfNull(tags);
            return Face.Descriptor.SupportsFeatureTags(tags);
        }

        /// <summary>
        /// Whether the face is one to prefer for a character drawn in a presentation: its <c>cmap</c> format 14 says it
        /// supports the matching variation sequence or, when it declares nothing, its colour-ness agrees (a colour font for
        /// emoji presentation, an outline font for text presentation).
        /// </summary>
        /// <param name="rune">The character.</param>
        /// <param name="presentation">The presentation wanted; <see cref="EmojiPresentation.NoPreference"/> accepts every face.</param>
        public bool MatchesEmojiPresentation(Rune rune, EmojiPresentation presentation)
            => EmojiProperties.FaceMatches(Face.Descriptor.FontFace, rune.Value, presentation);

        /// <inheritdoc />
        public bool Equals(Typeface? other) => other is not null && ReferenceEquals(Face.FontSource, other.Face.FontSource);

        /// <inheritdoc />
        public override bool Equals(object? obj) => Equals(obj as Typeface);

        /// <inheritdoc />
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(Face.FontSource);

        /// <inheritdoc />
        public override string ToString() => Face.DisplayName;
    }
}
