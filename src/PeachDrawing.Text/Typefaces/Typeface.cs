using PeachDrawing.Text.Internal.Fonts;
using System;
using System.Runtime.CompilerServices;

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
