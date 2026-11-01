using System;

namespace PeachDrawing.Text.Export
{
    /// <summary>
    /// A font made ready to be embedded in a document: the bytes of a font file, and what kind of outlines they hold.
    /// </summary>
    public sealed class ExportedFont
    {
        private readonly byte[] _data;

        internal ExportedFont(byte[] data, bool hasCffOutlines, bool isSubset, ExportedFontFormat format)
        {
            _data = data;
            HasCffOutlines = hasCffOutlines;
            IsSubset = isSubset;
            Format = format;
        }

        /// <summary>The bytes of the font, in the form <see cref="Format"/> says.</summary>
        public ReadOnlyMemory<byte> Data => _data;

        /// <summary>
        /// Whether the glyphs are drawn from CFF outlines (a <c>CFF </c> table, in an OpenType font or on its own), and not from
        /// TrueType outlines.
        /// </summary>
        public bool HasCffOutlines { get; }

        /// <summary>
        /// Whether the font holds only the glyphs that were asked for. A CID-keyed CFF font is cut down, and so is a face with CFF2
        /// outlines, which is written afresh. Any other font with CFF outlines is handed over whole, because its outlines cannot be
        /// cut down.
        /// </summary>
        public bool IsSubset { get; }

        /// <summary>
        /// What <see cref="Data"/> is: an OpenType font file, or the bare Compact Font Format table of a CID-keyed CFF font.
        /// </summary>
        public ExportedFontFormat Format { get; }
    }
}
