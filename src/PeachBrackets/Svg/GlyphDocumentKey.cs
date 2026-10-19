using PeachDrawing.Core;
using PeachDrawing.Text;
using System;
using System.Collections.Generic;

namespace PeachBrackets.Svg
{
    /// <summary>
    /// Everything that decides what an SVG color glyph's artwork looks like - size and position excluded, which the drawing's placement
    /// carries, so they must not split the cache.
    /// </summary>
    internal readonly struct GlyphDocumentKey : IEquatable<GlyphDocumentKey>
    {
        private readonly Typeface _typeface;
        private readonly int _glyphId;
        private readonly int _paletteIndex;
        private readonly uint _foreground;
        private readonly IReadOnlyDictionary<int, PaintColor>? _overrides;
        private readonly int _overridesHash;

        public GlyphDocumentKey(Typeface typeface, int glyphId, int paletteIndex, PaintColor foreground,
            IReadOnlyDictionary<int, PaintColor>? overrides)
        {
            _typeface = typeface;
            _glyphId = glyphId;
            _paletteIndex = paletteIndex;
            _foreground = ToArgb(foreground);
            _overrides = overrides;
            _overridesHash = HashOverrides(overrides);
        }

        // The override hash is deliberately not compared here as a shortcut: a dictionary only asks two keys whether they are equal once
        // their hashes have already matched, so a hash compare would only ever hide the value compare below from ever running.
        public bool Equals(GlyphDocumentKey other)
            => ReferenceEquals(_typeface, other._typeface)
               && _glyphId == other._glyphId
               && _paletteIndex == other._paletteIndex
               && _foreground == other._foreground
               && OverridesEqual(_overrides, other._overrides);

        public override bool Equals(object? obj) => obj is GlyphDocumentKey other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(_typeface, _glyphId, _paletteIndex, _foreground, _overridesHash);

        /// <summary>
        /// A CSS font-palette's overrides reach the backend as a fresh dictionary per draw call, so identity comparison would dedupe
        /// nothing - compare by value, order-independently, the way a dictionary itself is equal.
        /// </summary>
        private static bool OverridesEqual(IReadOnlyDictionary<int, PaintColor>? left, IReadOnlyDictionary<int, PaintColor>? right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left is null || right is null || left.Count != right.Count)
                return false;

            foreach ((int entry, PaintColor color) in left)
            {
                if (!right.TryGetValue(entry, out PaintColor other) || ToArgb(other) != ToArgb(color))
                    return false;
            }

            return true;
        }

        private static int HashOverrides(IReadOnlyDictionary<int, PaintColor>? overrides)
        {
            if (overrides is null)
                return 0;

            // XOR-combined so enumeration order (which a dictionary does not promise) cannot change the hash of two equal override sets.
            int hash = overrides.Count;
            foreach ((int entry, PaintColor color) in overrides)
                hash ^= HashCode.Combine(entry, ToArgb(color));
            return hash;
        }

        /// <summary>
        /// Packs a color into its 8-bit-per-channel ARGB form - the resolution the PDF content stream itself keeps, so two values that
        /// write identically never split into two documents over a floating-point difference the output cannot show.
        /// </summary>
        private static uint ToArgb(PaintColor color) => ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
    }
}
