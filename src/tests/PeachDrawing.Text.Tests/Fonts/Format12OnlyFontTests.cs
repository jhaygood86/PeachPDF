using PeachDrawing.Text.Export;
using PeachDrawing.Text.Internal.Fonts;
using PeachDrawing.Text.Internal.Fonts.OpenType;
using PeachDrawing.Text.Shaping;
using PeachPDF.Tests.TestSupport;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace PeachDrawing.Text.Tests.Fonts
{
    /// <summary>
    /// A font whose only <c>cmap</c> subtable is format 12 (a platform 3, encoding 10 subtable and no format 4) is
    /// usable: the format-12 groups answer for the Basic Multilingual Plane as well as the astral planes. Each test
    /// derives one from the bundled Source Sans by replacing its cmap.
    /// </summary>
    public class Format12OnlyFontTests
    {
        private const int Astral = 0x1F600;

        /// <summary>The bundled font with a cmap that has one format-12 subtable mapping A to Z, a to z and one astral codepoint.</summary>
        private static byte[] Build(byte[] source, out Typeface original, int platform = 3)
        {
            original = TypefaceFixtures.FromBytes(source);

            var groups = new (int Code, int Glyph)[53];
            for (int i = 0; i < 26; i++)
            {
                groups[i] = ('A' + i, original.GlyphOf((char)('A' + i)));
                groups[26 + i] = ('a' + i, original.GlyphOf((char)('a' + i)));
            }
            groups[52] = (Astral, original.GlyphOf('A'));

            var subtable = new byte[16 + 12 * groups.Length];
            BinaryPrimitives.WriteUInt16BigEndian(subtable, 12);
            BinaryPrimitives.WriteUInt32BigEndian(subtable.AsSpan(4), (uint)subtable.Length);
            BinaryPrimitives.WriteUInt32BigEndian(subtable.AsSpan(12), (uint)groups.Length);
            for (int i = 0; i < groups.Length; i++)
            {
                var group = subtable.AsSpan(16 + 12 * i);
                BinaryPrimitives.WriteUInt32BigEndian(group, (uint)groups[i].Code);
                BinaryPrimitives.WriteUInt32BigEndian(group[4..], (uint)groups[i].Code);
                BinaryPrimitives.WriteUInt32BigEndian(group[8..], (uint)groups[i].Glyph);
            }

            var cmap = new byte[12 + subtable.Length];
            BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(2), 1);   // numTables
            BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(4), (ushort)platform);   // platform: Windows unless a test says otherwise
            BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(6), 10);  // encoding: Unicode full repertoire
            BinaryPrimitives.WriteUInt32BigEndian(cmap.AsSpan(8), 12);  // subtable offset
            subtable.CopyTo(cmap.AsSpan(12));

            var tables = SyntheticUvsFont.ReadTables(source, out var sfntVersion);
            tables["cmap"] = cmap;
            return SyntheticUvsFont.WriteTables(tables, sfntVersion);
        }

        [Fact]
        public void TheFontLoads_AndItsFormat12GroupsAnswerForTheBmpAndTheAstralPlane()
        {
            var bytes = Build(File.ReadAllBytes(BundledFonts.Ttf), out var original);
            var face = FontFileData.GetOrCreateFrom(bytes).Fontface;

            Assert.Null(face.cmap.cmap4);
            Assert.NotNull(face.cmap.cmap12);

            var typeface = TypefaceFixtures.FromBytes(bytes);
            Assert.Equal(original.GlyphOf('A'), typeface.GlyphOf('A'));
            Assert.Equal(original.GlyphOf('z'), typeface.GlyphOf('z'));
            Assert.Equal(original.GlyphOf('A'), typeface.GlyphOf(new Rune(Astral)));

            // Unmapped codepoints, BMP and astral, are the missing glyph.
            Assert.Equal(0, typeface.GlyphOf('1'));
            Assert.Equal(0, typeface.GlyphOf(new Rune(0x1F601)));
            Assert.True(typeface.HasGlyph(new Rune('A')));
            Assert.False(typeface.HasGlyph(new Rune('1')));
        }

        [Fact]
        public void ItsCoverageComesFromTheFormat12Groups()
        {
            var bytes = Build(File.ReadAllBytes(BundledFonts.Ttf), out _);
            var family = new FontSet().AddData(bytes);

            Assert.True(family.TryMatch(new TypefaceQuery { MustCover = new Rune('Q') }, out _));
            Assert.True(family.TryMatch(new TypefaceQuery { MustCover = new Rune(Astral) }, out _));
            Assert.False(family.TryMatch(new TypefaceQuery { MustCover = new Rune('1') }, out _));
        }

        [Fact]
        public void ItShapesText()
        {
            var bytes = Build(File.ReadAllBytes(BundledFonts.Ttf), out var original);
            var typeface = TypefaceFixtures.FromBytes(bytes);

            var run = Shaper.Shape(typeface, "Hi", ShapeSettings.Default);

            Assert.Equal(2, run.Glyphs.Count);
            Assert.Equal(original.GlyphOf('H'), run.Glyphs[0].GlyphIndex);
        }

        [Fact]
        public void AFormat12SubtableOfAPlatformThatIsNotUnicodeDoesNotMakeTheFontUsable()
        {
            var bytes = Build(File.ReadAllBytes(BundledFonts.Ttf), out _, platform: 9);

            Assert.Throws<InvalidOperationException>(() => new OpenTypeFontface(FontFileData.CreateCompiledFont(bytes)));
        }

        [Fact]
        public void ItCanBeSubsetForEmbedding()
        {
            var bytes = Build(File.ReadAllBytes(BundledFonts.Ttf), out _);
            var typeface = TypefaceFixtures.FromBytes(bytes);

            var exported = TypefaceExporter.ExportSubset(typeface, [typeface.GlyphOf('H'), typeface.GlyphOf('i')], keepCharacterMap: true);

            var tables = SyntheticUvsFont.ReadTables(exported.Data.ToArray(), out _);
            Assert.Contains("glyf", tables.Keys);
            Assert.Contains("cmap", tables.Keys);
        }
    }
}
