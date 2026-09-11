using System.Buffers.Binary;
using System.Text;
using PeachPDF.Fonts.OpenType;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.PdfSharpCoreTests.Fonts
{
    public class GlyphDataTableTests
    {
        [Fact]
        public void CompleteGlyphClosure_IncludesNestedCompositeComponents()
        {
            byte[] fontBytes = File.ReadAllBytes(BundledFonts.Ttf);
            var sourceFace = new OpenTypeFontface(XFontSource.CreateCompiledFont(fontBytes));

            int accentedGlyph = GlyphId(sourceFace, 'á');
            int baseGlyph = GlyphId(sourceFace, 'a');
            int accentGlyph = Assert.Single(GetComponents(sourceFace, accentedGlyph), glyph => glyph != baseGlyph);
            int nestedGlyph = GlyphId(sourceFace, 'x');

            Assert.True(NumberOfContours(sourceFace.glyf.GetGlyphData(accentGlyph)) >= 0);
            Assert.DoesNotContain(nestedGlyph, GetComponents(sourceFace, accentedGlyph));

            // Recast the immediate accent component as another composite. This reproduces the shape
            // used by fonts such as Go Noto CJKCore: á -> (a, acute composite) -> acute outline.
            // Its existing glyph record is large enough for this single-component composite.
            Span<byte> accentData = fontBytes.AsSpan(sourceFace.glyf.GetOffset(accentGlyph));
            BinaryPrimitives.WriteInt16BigEndian(accentData, -1);
            BinaryPrimitives.WriteUInt16BigEndian(accentData[10..], 0x0003); // words + XY values
            BinaryPrimitives.WriteUInt16BigEndian(accentData[12..], (ushort)nestedGlyph);
            BinaryPrimitives.WriteInt16BigEndian(accentData[14..], 0);
            BinaryPrimitives.WriteInt16BigEndian(accentData[16..], 0);

            var nestedFace = new OpenTypeFontface(XFontSource.CreateCompiledFont(fontBytes));
            var selected = new Dictionary<int, object> { [accentedGlyph] = null! };

            nestedFace.glyf.CompleteGlyphClosure(selected);

            Assert.Contains(baseGlyph, selected.Keys);
            Assert.Contains(accentGlyph, selected.Keys);
            Assert.Contains(nestedGlyph, selected.Keys);
        }

        private static int GlyphId(OpenTypeFontface face, char character)
        {
            var descriptor = new OpenTypeDescriptor("nested-composite-test", "nested-composite-test",
                XFontStyle.Regular, face, new XPdfFontOptions(PdfFontEncoding.Unicode));
            return descriptor.CharCodeToGlyphIndex(new Rune(character));
        }

        private static IReadOnlyList<int> GetComponents(OpenTypeFontface face, int glyph)
        {
            ReadOnlySpan<byte> data = face.glyf.GetGlyphData(glyph);
            Assert.True(NumberOfContours(data) < 0);

            var components = new List<int>();
            int offset = 10;
            while (true)
            {
                int flags = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
                components.Add(BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 2)..]));
                if ((flags & 0x0020) == 0)
                    return components;

                offset += 4;
                offset += (flags & 0x0001) == 0 ? 2 : 4;
                if ((flags & 0x0008) != 0)
                    offset += 2;
                else if ((flags & 0x0040) != 0)
                    offset += 4;
                else if ((flags & 0x0080) != 0)
                    offset += 8;
            }
        }

        private static short NumberOfContours(ReadOnlySpan<byte> glyphData) =>
            BinaryPrimitives.ReadInt16BigEndian(glyphData);
    }
}
