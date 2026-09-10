#region PeachPDF - A .NET library for rendering HTML to PDF
//
// Per-document cache of color-font (COLR/CPAL) glyph artwork drawn once into a Form XObject and
// invoked with `Do` at every later occurrence, instead of re-emitting the whole vector paint graph
// into the page content stream per glyph. A ZWJ emoji sequence costs ~133 KB of paths each time it
// is inlined; as a form it costs that once plus ~25 bytes per occurrence.
//
// The cached artwork is drawn at a canonical em size (ColorGlyphFormCache.EmSize) and placed by the
// `cm` that invokes it, so one form serves every font size the glyph appears at - unlike the raster
// image cache (PdfImageTable), whose embedded pixels do depend on display size.
//
// Everything that can change the artwork is part of the key: the font (descriptor identity), the
// glyph, the selected CPAL palette and its CSS font-palette entry overrides, and the text color -
// which a COLR layer or paint can name through the 0xFFFF "use foreground" sentinel, and which a
// glyph with no color record at all is filled in outright.
//
#endregion

using System;
using System.Collections.Generic;
using PeachPDF.Fonts.OpenType;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.PdfSharpCore.Drawing.Pdf
{
    /// <summary>
    /// A color glyph's cached artwork: the Form XObject holding it, plus where the glyph's baseline
    /// origin sits inside that form. <see cref="Form"/> is null for a glyph that paints no ink at all
    /// (a space, say) - a cached "nothing to draw" answer, so the measure pass runs only once for it.
    /// </summary>
    /// <param name="Form">The form holding the artwork, or null when the glyph paints nothing.</param>
    /// <param name="LeftX">The artwork box's left edge, in canonical world units relative to the baseline origin.</param>
    /// <param name="TopY">The artwork box's top edge, in canonical (y-down) world units relative to the baseline origin.</param>
    internal readonly record struct ColorGlyphForm(XForm? Form, double LeftX, double TopY);

    /// <summary>
    /// Maps a <see cref="ColorGlyphFormCache.Selector"/> to the <see cref="ColorGlyphForm"/> rendered for it.
    /// One instance per <see cref="PeachPDF.PdfSharpCore.Pdf.PdfDocument"/>: a Form XObject belongs to the document it was
    /// created for, and <c>PdfFormXObjectTable.GetForm</c> already adds one form to every page's
    /// resources that invokes it, so a glyph repeated across pages still embeds exactly once.
    /// </summary>
    internal sealed class ColorGlyphFormCache
    {
        /// <summary>
        /// The em size, in world units, the cached artwork is drawn at. Placement scales it by
        /// <c>fontSize / EmSize</c>, so this only sets how much precision the form's own coordinates
        /// keep: at 100, a stream written to four decimal places resolves ~1e-6 em.
        /// </summary>
        public const double EmSize = 100.0;

        private readonly Dictionary<Selector, ColorGlyphForm> _forms = [];

        public bool TryGetForm(in Selector selector, out ColorGlyphForm form) => _forms.TryGetValue(selector, out form);

        public void AddForm(in Selector selector, in ColorGlyphForm form) => _forms[selector] = form;

        /// <summary>
        /// Everything that decides what a color glyph's artwork looks like, size and position excluded -
        /// those are carried by the placement <c>cm</c> and so must not split the cache.
        /// </summary>
        internal readonly struct Selector : IEquatable<Selector>
        {
            private readonly OpenTypeDescriptor _descriptor;
            private readonly int _glyphId;
            private readonly int _paletteIndex;
            private readonly uint _foreground;
            private readonly IReadOnlyDictionary<int, XColor>? _overrides;
            private readonly int _overridesHash;

            public Selector(OpenTypeDescriptor descriptor, int glyphId, int paletteIndex, XColor foreground,
                IReadOnlyDictionary<int, XColor>? overrides)
            {
                _descriptor = descriptor;
                _glyphId = glyphId;
                _paletteIndex = paletteIndex;
                _foreground = ToArgb(foreground);
                _overrides = overrides;
                _overridesHash = HashOverrides(overrides);
            }

            // The override hash is deliberately not compared here as a shortcut: a dictionary only asks
            // two keys whether they are equal once their hashes have already matched, so a hash compare
            // would only ever hide the value compare below from ever running.
            public bool Equals(Selector other)
                => ReferenceEquals(_descriptor, other._descriptor)
                   && _glyphId == other._glyphId
                   && _paletteIndex == other._paletteIndex
                   && _foreground == other._foreground
                   && OverridesEqual(_overrides, other._overrides);

            public override bool Equals(object? obj) => obj is Selector other && Equals(other);

            public override int GetHashCode()
                => HashCode.Combine(_descriptor, _glyphId, _paletteIndex, _foreground, _overridesHash);

            /// <summary>
            /// A CSS font-palette's overrides reach the backend as a fresh dictionary per
            /// <c>DrawString</c> (see <c>GraphicsAdapter.ToGlyphPalette</c>), so identity comparison would
            /// dedupe nothing - compare by value, order-independently, the way a dictionary itself is equal.
            /// </summary>
            private static bool OverridesEqual(IReadOnlyDictionary<int, XColor>? left, IReadOnlyDictionary<int, XColor>? right)
            {
                if (ReferenceEquals(left, right))
                    return true;
                if (left is null || right is null || left.Count != right.Count)
                    return false;

                foreach ((int entry, XColor color) in left)
                {
                    if (!right.TryGetValue(entry, out XColor other) || ToArgb(other) != ToArgb(color))
                        return false;
                }
                return true;
            }

            private static int HashOverrides(IReadOnlyDictionary<int, XColor>? overrides)
            {
                if (overrides is null)
                    return 0;

                // XOR-combined so enumeration order (which a dictionary does not promise) cannot change
                // the hash of two equal override sets.
                int hash = overrides.Count;
                foreach ((int entry, XColor color) in overrides)
                    hash ^= HashCode.Combine(entry, ToArgb(color));
                return hash;
            }

            /// <summary>
            /// Packs a color into its 8-bit-per-channel ARGB form - the resolution the PDF content
            /// stream itself keeps, so two <see cref="XColor"/> values that write identically never
            /// split into two forms over a floating-point difference the output cannot show.
            /// </summary>
            private static uint ToArgb(XColor color) => color.Argb;
        }
    }
}
