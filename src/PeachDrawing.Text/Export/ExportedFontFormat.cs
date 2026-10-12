namespace PeachDrawing.Text.Export
{
    /// <summary>
    /// What the bytes of an <see cref="ExportedFont"/> are.
    /// </summary>
    public enum ExportedFontFormat
    {
        /// <summary>An OpenType font file: the outlines (TrueType, CFF or CFF2) with the tables that map characters to them.</summary>
        OpenType,

        /// <summary>
        /// The bare Compact Font Format table of a CID-keyed CFF font, which is what a <c>CIDFontType0</c> descendant carries.
        /// </summary>
        CompactFontFormat,
    }
}
