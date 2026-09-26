using PeachDrawing.Text.Internal.Fonts;
using PeachDrawing.Text.Internal.Text;
using PeachDrawing.Text.OpenType;
using PeachDrawing.Text.Outlines;
using PeachDrawing.Text.Unicode;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace PeachDrawing.Text
{
    /// <summary>
    /// One face of a family: what a <see cref="TypefaceFamily"/> hands back when it matches a
    /// <see cref="TypefaceQuery"/>. It has no size; the size of text is the caller's to keep.
    /// </summary>
    /// <remarks>
    /// Two typefaces are equal when they read the same font data, whichever query matched them. A typeface stays
    /// valid for as long as it is referenced, including after the <see cref="FontSet"/> that found it is gone. Reading a
    /// typeface, its <see cref="Metrics"/> included, is safe from any number of threads at once.
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

        /// <summary>
        /// The full name of the face as its font file declares it, such as <c>Arial Bold Italic</c>: the family and the style
        /// together. A font that does not declare one gets its family name.
        /// </summary>
        public string FullName => Face.DisplayName;

        /// <summary>
        /// A checksum of the font data the face reads. Two typefaces that read the same data have the same hash, so it can
        /// key a cache of things made from a face, such as an embedded copy of it.
        /// </summary>
        /// <remarks>It is a checksum, so equal data always gives equal hashes but the reverse is only overwhelmingly likely; compare typefaces with <see cref="Equals(Typeface)"/> when it must be certain.</remarks>
        public ulong ContentHash => Face.FontSource.Key;

        /// <summary>Whether the font file declares the face bold (in its OS/2 table).</summary>
        public bool IsBold => Face.IsBold;

        /// <summary>Whether the font file declares the face italic (in its OS/2 table).</summary>
        public bool IsItalic => Face.IsItalic;

        /// <summary>The vertical dimensions of the face, in design units.</summary>
        public TypefaceMetrics Metrics => _metrics ?? Interlocked.CompareExchange(ref _metrics, new TypefaceMetrics(Face.Descriptor), null) ?? _metrics!;

        private TypefaceMetrics? _metrics;

        /// <summary>
        /// Whether the face is one that a renderer draws colour glyphs from: it has <c>COLR</c> and <c>CPAL</c> tables over TrueType
        /// outlines, or it draws its glyphs as pictures (<see cref="HasBitmapGlyphs"/>). A colour font with CFF outlines reports
        /// <see langword="false"/>.
        /// </summary>
        public bool HasColorGlyphs => Face.Descriptor.IsColorFont;

        /// <summary>
        /// Reads the shape of a glyph, in design units with the y axis up.
        /// </summary>
        /// <remarks>
        /// TrueType (<c>glyf</c>) outlines are supported, with composite glyphs flattened into one outline (a component placed by
        /// matching points and not by an offset is placed at no offset), and so are CFF outlines where the charstrings use the
        /// supported operators. Nothing is grid-fitted: hinting instructions are not run.
        /// </remarks>
        /// <param name="glyph">The glyph.</param>
        /// <param name="outline">The outline. It is empty when the method returns <see langword="false"/>.</param>
        /// <returns><see langword="false"/> when the font has no usable outline for the glyph, or the glyph has no ink (a space).</returns>
        public bool TryGetOutline(ushort glyph, out GlyphOutline outline) => Face.Descriptor.TryGetGlyphOutline(glyph, out outline);

        /// <summary>
        /// The colours that colour glyphs of this face are painted with, or <see langword="null"/> when <see cref="HasColorGlyphs"/> is
        /// <see langword="false"/> or the face has no <c>CPAL</c> table, as a face with pictures for glyphs has not.
        /// </summary>
        /// <remarks>
        /// <see cref="GetColorPaint"/>, <see cref="GetColorLayerPaint"/> and <see cref="TryGetColorLayers"/> do not look at
        /// <see cref="HasColorGlyphs"/>: they answer from the <c>COLR</c> table alone.
        /// </remarks>
        public ColorPalette? ColorPalette
        {
            get
            {
                if (!HasColorGlyphs) return null;
                return _colorPalette ??= Face.Descriptor.ColorPalette is { } table ? new ColorPalette(table) : null;
            }
        }

        private ColorPalette? _colorPalette;

        /// <summary>
        /// The root of the paint graph of a version 1 colour glyph, or <see langword="null"/> when the glyph has none: the face has
        /// no version 1 <c>COLR</c> table, or no paint for this glyph.
        /// </summary>
        /// <remarks>
        /// The graph can share nodes and, in a malformed font, can lead back to itself through <see cref="PaintColrGlyph"/> and
        /// <see cref="PaintColrLayers"/>, so a caller that walks it has to bound the depth and the work.
        /// </remarks>
        /// <param name="glyph">The base glyph.</param>
        public ColorPaint? GetColorPaint(ushort glyph)
            => Face.Descriptor.ColorTable is { Version: >= 1 } colr ? colr.GetV1BaseGlyphPaint(glyph) : null;

        /// <summary>The paint at an entry of the layer list, which a <see cref="PaintColrLayers"/> node refers to by index.</summary>
        /// <param name="index">The index in the layer list.</param>
        /// <returns>The paint, or <see langword="null"/> when there is no such entry.</returns>
        public ColorPaint? GetColorLayerPaint(int index) => Face.Descriptor.ColorTable?.GetLayerPaint(index);

        /// <summary>
        /// The layers of a version 0 colour glyph, painted in order from the bottom layer up, each a glyph in one palette colour.
        /// </summary>
        /// <param name="glyph">The base glyph.</param>
        /// <param name="layers">The layers.</param>
        /// <returns><see langword="false"/> when the face has no version 0 layers for the glyph.</returns>
        public bool TryGetColorLayers(ushort glyph, out IReadOnlyList<ColorLayer> layers)
        {
            if (Face.Descriptor.ColorTable is { } colr && colr.TryGetV0Layers(glyph, out var found))
            {
                layers = found;
                return true;
            }

            layers = [];
            return false;
        }

        /// <summary>
        /// Whether the face draws colour glyphs as pictures, one per glyph and size (<c>CBDT</c>/<c>CBLC</c> or <c>sbix</c>),
        /// and not from outlines. Almost every font has none.
        /// </summary>
        public bool HasBitmapGlyphs => Face.Descriptor.HasBitmapGlyphs;

        /// <summary>
        /// Whether the face is made for setting mathematics, which is to say it has a <c>MATH</c> table.
        /// </summary>
        public bool HasMathData => Face.Descriptor.HasMathTable;

        /// <summary>
        /// The <c>MATH</c> table of a face that has one: the constants, per-glyph information and stretchy-glyph variants a
        /// math layout algorithm reads.
        /// </summary>
        /// <value>The table, or <see langword="null"/> when <see cref="HasMathData"/> is <see langword="false"/>.</value>
        public MathTable? MathData => Face.Descriptor.MathTable;

        /// <summary>
        /// The picture of a glyph from the strike best suited to a font size.
        /// </summary>
        /// <param name="glyph">The glyph.</param>
        /// <param name="ppem">The size the glyph will be drawn at, in pixels per em.</param>
        /// <param name="bitmap">The picture.</param>
        /// <returns><see langword="false"/> when the glyph has no picture: it is drawn from outlines, or the face has no bitmap tables.</returns>
        public bool TryGetBitmap(ushort glyph, double ppem, out EmbeddedBitmap bitmap) => Face.Descriptor.TryGetBitmapGlyph(glyph, ppem, out bitmap);

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
        /// <remarks>
        /// The horizontal coordinate is always half the glyph's horizontal advance; no font table supplies it. The vertical
        /// one is the font's own <c>VORG</c> value when <see cref="HasVerticalOrigin"/> is <see langword="true"/>, and
        /// otherwise the first of the <c>vhea</c> ascent, the <c>OS/2</c> typographic ascender and one em that the font has.
        /// </remarks>
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
