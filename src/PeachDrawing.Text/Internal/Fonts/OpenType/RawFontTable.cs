#nullable disable warnings

namespace PeachDrawing.Text.Internal.Fonts.OpenType
{
    /// <summary>
    /// A table of a font being written whose bytes are already made: the metrics table of an instance of a variable font, for example.
    /// </summary>
    internal sealed class RawFontTable : OpenTypeFontTable
    {
        internal RawFontTable(string tag)
            : base(null, tag)
        {
        }

        /// <summary>The table's bytes, set before the font is compiled.</summary>
        internal byte[] Data { get; set; } = [];

        public override void PrepareForCompilation()
        {
            base.PrepareForCompilation();
            DirectoryEntry.Length = Data.Length;
            DirectoryEntry.CheckSum = CalcChecksum(Padded());
        }

        public override void Write(OpenTypeFontWriter writer)
        {
            byte[] padded = Padded();
            writer.Write(padded, 0, padded.Length);
        }

        private byte[] Padded()
        {
            if ((Data.Length & 3) == 0)
            {
                return Data;
            }

            var padded = new byte[(Data.Length + 3) & ~3];
            Data.CopyTo(padded, 0);
            return padded;
        }
    }
}
