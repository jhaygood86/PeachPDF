using PeachDrawing.Text.Internal.Fonts;
using PeachPDF.Tests.TestSupport;
using System.Buffers.Binary;
using System.IO;

namespace PeachDrawing.Text.Tests.Fonts
{
    /// <summary>
    /// A font whose only <c>cmap</c> subtable is a format-4 one on the Unicode platform (platform 0) is usable whatever Unicode encoding it names: macOS's
    /// own Helvetica Neue ships with encoding 1 (and 3), and nothing else. Each test derives one from the bundled Source Sans by keeping its format-4
    /// subtable under a single record of that platform and encoding.
    /// </summary>
    public class UnicodePlatformCmapFontTests
    {
        private static byte[] Build(byte[] source, int encoding)
        {
            var tables = SyntheticUvsFont.ReadTables(source, out var sfntVersion);
            var original = tables["cmap"];

            // Find the format-4 subtable of the original and copy it out.
            int count = BinaryPrimitives.ReadUInt16BigEndian(original.AsSpan(2));
            byte[]? format4 = null;
            for (int i = 0; i < count && format4 is null; i++)
            {
                int offset = (int)BinaryPrimitives.ReadUInt32BigEndian(original.AsSpan(4 + 8 * i + 4));
                if (BinaryPrimitives.ReadUInt16BigEndian(original.AsSpan(offset)) == 4)
                {
                    int length = BinaryPrimitives.ReadUInt16BigEndian(original.AsSpan(offset + 2));
                    format4 = original.AsSpan(offset, length).ToArray();
                }
            }

            Assert.NotNull(format4);

            var cmap = new byte[12 + format4.Length];
            BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(2), 1);                 // numTables
            BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(4), 0);                 // platform: Unicode
            BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(6), (ushort)encoding);
            BinaryPrimitives.WriteUInt32BigEndian(cmap.AsSpan(8), 12);                // subtable offset
            format4.CopyTo(cmap.AsSpan(12));

            tables["cmap"] = cmap;
            return SyntheticUvsFont.WriteTables(tables, sfntVersion);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void TheFontLoads_AndItsFormat4SubtableAnswersForTheBmp(int encoding)
        {
            var source = File.ReadAllBytes(BundledFonts.Ttf);
            var original = TypefaceFixtures.FromBytes(source);
            var bytes = Build(source, encoding);

            var face = FontFileData.GetOrCreateFrom(bytes).Fontface;
            Assert.NotNull(face.cmap.cmap4);

            var typeface = TypefaceFixtures.FromBytes(bytes);
            Assert.Equal(original.GlyphOf('A'), typeface.GlyphOf('A'));
            Assert.Equal(original.GlyphOf('z'), typeface.GlyphOf('z'));
        }
    }
}
