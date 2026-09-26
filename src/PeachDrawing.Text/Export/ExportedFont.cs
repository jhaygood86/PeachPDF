using System;

namespace PeachDrawing.Text.Export
{
    /// <summary>
    /// A font made ready to be embedded in a document: the bytes of a font file, and what kind of outlines they hold.
    /// </summary>
    public sealed class ExportedFont
    {
        internal ExportedFont(byte[] data, bool hasCffOutlines, bool isSubset)
        {
            _data = data;
            HasCffOutlines = hasCffOutlines;
            IsSubset = isSubset;
        }

        private readonly byte[] _data;

        /// <summary>The bytes of the font file.</summary>
        public ReadOnlyMemory<byte> Data => _data;

        /// <summary>Whether the glyphs are drawn from CFF outlines (an OpenType font with a <c>CFF </c> table), and not from TrueType outlines.</summary>
        public bool HasCffOutlines { get; }

        /// <summary>
        /// Whether the font holds only the glyphs that were asked for. It does not when the face's outlines are CFF, which the
        /// exporter cannot cut down and so hands over whole.
        /// </summary>
        public bool IsSubset { get; }
    }
}
