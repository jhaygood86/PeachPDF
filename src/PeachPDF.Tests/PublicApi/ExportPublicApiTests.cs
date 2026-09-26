using PeachDrawing.Text;
using PeachDrawing.Text.Export;
using PeachPDF.Tests.TestSupport;
using System.Text;

namespace PeachPDF.Tests.PublicApi
{
    /// <summary>
    /// Cutting a typeface down to the glyphs a document uses, and the descriptive members an embedder reads to write the
    /// font's dictionary, as a consumer outside the assembly sees them.
    /// </summary>
    public class ExportPublicApiTests
    {
        private static Typeface Face(string path) => TestFonts.TypefaceFromFile(path);

        private static ushort GlyphOf(Typeface face, char c)
        {
            Assert.True(face.TryMapRune(new Rune(c), out var glyph));
            return glyph;
        }

        [Fact]
        public void ExportSubset_KeepsTheAskedForGlyphsAndDropsTheRest()
        {
            var face = Face(BundledFonts.Ttf);
            var a = GlyphOf(face, 'A');
            var b = GlyphOf(face, 'B');

            var exported = TypefaceExporter.ExportSubset(face, [a], keepCharacterMap: true);

            Assert.True(exported.IsSubset);
            Assert.False(exported.HasCffOutlines);
            Assert.True(exported.Data.Length < File.ReadAllBytes(BundledFonts.Ttf).Length);

            // The glyphs keep their indices: the subset has an outline for the one asked for, and none for another.
            var data = exported.Data.ToArray();
            Assert.True(GlyphDataLength(data, a) > 0);
            Assert.Equal(0, GlyphDataLength(data, b));
        }

        /// <summary>Reads how many bytes of outline data a glyph has in a TrueType font file, straight from its <c>loca</c> table (an embedded subset carries no name table, so the font readers that need one refuse it).</summary>
        private static int GlyphDataLength(byte[] font, int glyph)
        {
            static int U16(byte[] d, int at) => d[at] << 8 | d[at + 1];
            static int U32(byte[] d, int at) => U16(d, at) << 16 | U16(d, at + 2);

            int tableCount = U16(font, 4);
            int loca = -1, head = -1;
            for (int i = 0; i < tableCount; i++)
            {
                int record = 12 + i * 16;
                string tag = Encoding.ASCII.GetString(font, record, 4);
                if (tag == "loca") loca = U32(font, record + 8);
                if (tag == "head") head = U32(font, record + 8);
            }

            Assert.True(loca >= 0 && head >= 0);
            bool longOffsets = U16(font, head + 50) != 0;
            int Offset(int index) => longOffsets ? U32(font, loca + index * 4) : U16(font, loca + index * 2) * 2;
            return Offset(glyph + 1) - Offset(glyph);
        }

        [Fact]
        public void ExportSubset_WithoutTheCharacterMap_IsSmaller()
        {
            var face = Face(BundledFonts.Ttf);
            int[] glyphs = [GlyphOf(face, 'A'), GlyphOf(face, 'B')];

            var withMap = TypefaceExporter.ExportSubset(face, glyphs, keepCharacterMap: true);
            var withoutMap = TypefaceExporter.ExportSubset(face, glyphs, keepCharacterMap: false);

            Assert.True(withoutMap.Data.Length < withMap.Data.Length);
        }

        [Fact]
        public void ExportSubset_OfAFontWithCffOutlines_HandsOverTheWholeFont()
        {
            var face = Face(BundledFonts.Otf);
            var exported = TypefaceExporter.ExportSubset(face, [GlyphOf(face, 'A')], keepCharacterMap: false);

            Assert.True(exported.HasCffOutlines);
            Assert.False(exported.IsSubset);
            Assert.Equal(File.ReadAllBytes(BundledFonts.Otf), exported.Data.ToArray());
        }

        [Fact]
        public void ExportSubset_RejectsNulls()
        {
            var face = Face(BundledFonts.Ttf);

            Assert.Throws<ArgumentNullException>(() => TypefaceExporter.ExportSubset(null!, [1], true));
            Assert.Throws<ArgumentNullException>(() => TypefaceExporter.ExportSubset(face, null!, true));
        }

        [Fact]
        public void ContentHash_IsEqualForEqualData_AndDiffersBetweenFonts()
        {
            var first = Face(BundledFonts.Ttf);
            var second = Face(BundledFonts.Ttf);
            var other = Face(BundledFonts.Otf);

            Assert.Equal(first.ContentHash, second.ContentHash);
            Assert.NotEqual(first.ContentHash, other.ContentHash);
        }

        [Fact]
        public void FullName_IsWhatTheFontNamesItself()
        {
            var face = Face(BundledFonts.Ttf);

            Assert.Equal(face.Face.DisplayName, face.FullName);
            Assert.Contains(face.FamilyName, face.FullName);
        }

        [Fact]
        public void Metrics_ReportTheFlagsAnEmbedderWritesIntoAFontDescriptor()
        {
            foreach (var path in new[] { BundledFonts.Ttf, BundledFonts.Otf, BundledFonts.Math, BundledFonts.Emoji })
            {
                var face = Face(path);
                var metrics = face.Metrics;
                var engine = face.Face.Descriptor.FontFace;

                Assert.Equal(engine.cmap.symbol, metrics.IsSymbolic);
                Assert.Equal(engine.post.isFixedPitch != 0, metrics.IsFixedPitch);
                Assert.Equal((engine.os2.sFamilyClass >> 8) is >= 1 and <= 7, metrics.HasSerifs);
                Assert.Equal(engine.os2.IsItalic, metrics.IsItalicStyle);
                Assert.Equal(engine.os2.usFirstCharIndex, metrics.FirstCharIndex);
            }
        }

        [Fact]
        public void Metrics_TellAMonospacedFontFromAProportionalOne()
        {
            Assert.False(Face(BundledFonts.Ttf).Metrics.IsFixedPitch);
        }
    }
}
