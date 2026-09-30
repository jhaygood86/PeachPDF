using PeachDrawing.Text.Internal.Fonts;
using PeachDrawing.Text.Internal.Fonts.OpenType;
using PeachPDF.Tests.TestSupport;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace PeachDrawing.Text.Tests.Fonts
{
    /// <summary>
    /// OpenType font collections (<c>.ttc</c>/<c>.otc</c>): reading each face's description, extracting one
    /// face as a standalone font, and discovering every face of a collection as a system font. Uses
    /// collections built from two genuinely different bundled fonts (Source Sans 3, a glyf TTF, and Source
    /// Code Pro, a CFF OTF), so nothing depends on which fonts the host ships.
    /// </summary>
    public class SfntCollectionTests : IDisposable
    {
        private readonly byte[] _sans = File.ReadAllBytes(BundledFonts.Ttf);
        private readonly byte[] _code = File.ReadAllBytes(BundledFonts.Otf);
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "peachpdf-ttc-" + Path.GetRandomFileName());

        public SfntCollectionTests() => Directory.CreateDirectory(_directory);

        public void Dispose() => Directory.Delete(_directory, recursive: true);

        private string Write(string name, byte[] bytes)
        {
            var path = Path.Combine(_directory, name);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        private byte[] Collection() => SyntheticFontCollection.Build(_sans, _code);

        private static string FaceName(byte[] standaloneFont) => TtfFontDescription.LoadDescription(new MemoryStream(standaloneFont)).FontNameInvariantCulture;

        // ---- reading the collection ----------------------------------------------------------------

        [Fact]
        public void IsCollection_RecognisesTheTtcfTagOnly()
        {
            Assert.True(SfntCollection.IsCollection(Collection()));
            Assert.False(SfntCollection.IsCollection(_sans));
            Assert.False(SfntCollection.IsCollection(_code));
            Assert.False(SfntCollection.IsCollection([0x74, 0x74, 0x63]));
        }

        [Fact]
        public void FaceCount_IsTheCollectionsCount_AndOneForAnOrdinaryFont()
        {
            Assert.Equal(2, SfntCollection.FaceCount(new MemoryStream(Collection())));
            Assert.Equal(1, SfntCollection.FaceCount(new MemoryStream(_sans)));
        }

        [Fact]
        public void FaceOffset_OfAnOrdinaryFont_IsZeroForFaceZeroOnly()
        {
            Assert.Equal(0, SfntCollection.FaceOffset(new MemoryStream(_sans), 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => SfntCollection.FaceOffset(new MemoryStream(_sans), 1));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(2)]
        public void FaceOffset_OutsideTheCollection_Throws(int faceIndex)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SfntCollection.FaceOffset(new MemoryStream(Collection()), faceIndex));
        }

        [Fact]
        public void ACollectionClaimingAbsurdlyManyFaces_IsRejectedBeforeAllocatingForThem()
        {
            var bytes = Collection();
            bytes[8] = 0x7F; bytes[9] = 0xFF; bytes[10] = 0xFF; bytes[11] = 0xFF;

            Assert.Throws<InvalidDataException>(() => SfntCollection.FaceCount(new MemoryStream(bytes)));
        }

        // ---- descriptions --------------------------------------------------------------------------

        [Fact]
        public void LoadDescription_ReadsEachFaceOfACollectionByIndex()
        {
            var sansName = TtfFontDescription.LoadDescription(new MemoryStream(_sans)).FontNameInvariantCulture;
            var codeName = TtfFontDescription.LoadDescription(new MemoryStream(_code)).FontNameInvariantCulture;
            Assert.NotEqual(sansName, codeName);

            using var stream = new MemoryStream(Collection());

            Assert.Equal(sansName, TtfFontDescription.LoadDescription(stream, 0).FontNameInvariantCulture);
            Assert.Equal(codeName, TtfFontDescription.LoadDescription(stream, 1).FontNameInvariantCulture);
            // The one-argument overload is face 0 - what a collection means where a single font is expected.
            Assert.Equal(sansName, TtfFontDescription.LoadDescription(stream).FontNameInvariantCulture);
        }

        [Fact]
        public void LoadDescriptions_OfACollectionFile_ListsEveryFaceWithItsIndex()
        {
            var path = Write("pair.ttc", Collection());

            var faces = TtfFontDescription.LoadDescriptions(path);

            Assert.Equal([0, 1], faces.Select(f => f.FaceIndex));
            Assert.Equal(
                [FaceName(_sans), FaceName(_code)],
                faces.Select(f => f.Description.FontNameInvariantCulture));
        }

        [Fact]
        public void LoadDescriptions_OfAnOrdinaryFile_IsThatOneFaceAtIndexZero()
        {
            var faces = TtfFontDescription.LoadDescriptions(Write("plain.ttf", _sans));

            var (index, description) = Assert.Single(faces);
            Assert.Equal(0, index);
            Assert.Equal(FaceName(_sans), description.FontNameInvariantCulture);
        }

        [Fact]
        public void LoadDescriptions_SkipsAFaceThatCannotBeRead_AndKeepsTheRest()
        {
            var bytes = Collection();
            // Point face 1's directory at a table count of zero: an unreadable face, in a readable collection.
            var faceOne = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16));
            bytes[faceOne + 4] = 0; bytes[faceOne + 5] = 0;

            var faces = TtfFontDescription.LoadDescriptions(Write("damaged.ttc", bytes));

            Assert.Equal(0, Assert.Single(faces).FaceIndex);
        }

        // ---- extracting a face ---------------------------------------------------------------------

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void ExtractFace_YieldsAStandaloneFontTheTableReadersLoad(int faceIndex)
        {
            var original = faceIndex == 0 ? _sans : _code;

            var face = SfntCollection.ExtractFace(Collection(), faceIndex);

            Assert.False(SfntCollection.IsCollection(face));
            Assert.Equal(FaceName(original), FaceName(face));

            // The rebuilt font is read by the real OpenType table parser and maps characters to the same
            // glyphs as the font it came from.
            OpenTypeDescriptor Descriptor(byte[] bytes) => new("t", "t", FontFileData.GetOrCreateFrom(bytes).Fontface);

            var extracted = Descriptor(face);
            var source = Descriptor(original);
            foreach (var c in "Aagz09")
            {
                Assert.NotEqual(0, extracted.CharCodeToGlyphIndex(new Rune(c)));
                Assert.Equal(source.CharCodeToGlyphIndex(new Rune(c)), extracted.CharCodeToGlyphIndex(new Rune(c)));
            }
        }

        [Fact]
        public void ExtractFace_CopiesEveryTableByteForByte()
        {
            var face = SfntCollection.ExtractFace(Collection(), 0);

            var original = SyntheticUvsFont.ReadTables(_sans, out _);
            var extracted = SyntheticUvsFont.ReadTables(face, out _);

            Assert.Equal(original.Keys, extracted.Keys);
            foreach (var tag in original.Keys)
                Assert.Equal(original[tag], extracted[tag]);
        }

        [Fact]
        public void ExtractFace_AtATableLyingOutsideTheFile_Throws()
        {
            var bytes = Collection();
            // Face 0's first table record: push its length past the end of the file.
            var faceZero = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(12));
            bytes[faceZero + 12 + 12] = 0x7F;

            Assert.Throws<InvalidDataException>(() => SfntCollection.ExtractFace(bytes, 0));
        }

        [Fact]
        public void ExtractFace_WithNoTables_Throws()
        {
            var bytes = Collection();
            var faceZero = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(12));
            bytes[faceZero + 4] = 0; bytes[faceZero + 5] = 0;

            Assert.Throws<InvalidDataException>(() => SfntCollection.ExtractFace(bytes, 0));
        }

        // ---- registering and discovering -----------------------------------------------------------

        [Fact]
        public void AddFont_WithACollection_RegistersItsFirstFaceAsAStandaloneFont()
        {
            var resolver = new FontResolver { NullIfFontNotFound = true };
            resolver.AddFont(new MemoryStream(Collection()), "FromCollection");

            var face = resolver.ResolveTypeface("FromCollection", 400, false);

            Assert.Equal(FaceName(_sans), face.FaceName);
            var stored = resolver.GetFont(face.FaceName);
            Assert.False(SfntCollection.IsCollection(stored));
            Assert.Equal(FaceName(_sans), FaceName(stored));
        }

        [Fact]
        public void ParseSystemFonts_FindsEveryFaceOfACollection_AndRemembersWhichFaceIsWhich()
        {
            var collection = Write("system.ttc", Collection());
            var plain = Write("plain.ttf", _sans);

            var (paths, families) = FontResolver.ParseSystemFonts([collection, plain]);

            var codeName = FaceName(_code);
            Assert.Equal((collection, 1), paths[codeName]);
            // The face name Source Sans 3 has in both files resolves to the first file that named it.
            Assert.Equal((collection, 0), paths[FaceName(_sans)]);

            var codeFamily = TtfFontDescription.LoadDescription(new MemoryStream(_code)).FontFamilyInvariantCulture.ToLowerInvariant();
            var sansFamily = TtfFontDescription.LoadDescription(new MemoryStream(_sans)).FontFamilyInvariantCulture.ToLowerInvariant();
            Assert.Contains(codeFamily, families.Keys);
            Assert.Contains(sansFamily, families.Keys);
        }

        [Fact]
        public void ParseSystemFonts_SkipsAnUnreadableFile_AndKeepsTheOthers()
        {
            var garbage = Write("garbage.ttc", Encoding.ASCII.GetBytes("ttcf-not-really-a-font"));
            var good = Write("good.ttc", Collection());

            var (paths, _) = FontResolver.ParseSystemFonts([garbage, good]);

            Assert.Contains(FaceName(_code), paths.Keys);
        }

        [Fact]
        public void LoadSystemFontBytes_GivesAStandaloneFont_ForAFaceOfACollectionAndTheFileItselfForAnOrdinaryFont()
        {
            var collection = Write("system.ttc", Collection());
            var plain = Write("plain.ttf", _sans);

            var face = FontResolver.LoadSystemFontBytes(collection, 1);
            Assert.False(SfntCollection.IsCollection(face));
            Assert.Equal(FaceName(_code), FaceName(face));

            Assert.Equal(_sans, FontResolver.LoadSystemFontBytes(plain, 0));
        }

        [Fact]
        public void GetFontFiles_FindsCollectionsAlongsideOrdinaryFonts_AndOrdersThemLast()
        {
            Write("a.ttf", _sans);
            Write("b.otf", _code);
            Write("c.ttc", Collection());
            Write("d.otc", Collection());
            Write("notes.txt", []);

            var files = FontResolver.GetFontFiles(_directory).Select(f => Path.GetFileName(f)!).ToArray();

            Assert.Equal(["a.ttf", "b.otf", "c.ttc", "d.otc"], files);
        }

        [Fact]
        public void OnWindows_CambriaMath_IsDiscoveredFromCambriaTtc()
        {
            // Windows ships Cambria Math only inside cambria.ttc - the font the math generic's Windows
            // chain names. Skipped (returns) anywhere else, as this project does for host-dependent facts.
            var cambria = Path.Combine(Environment.ExpandEnvironmentVariables(@"%SystemRoot%\Fonts"), "cambria.ttc");
            if (!OperatingSystem.IsWindows() || !File.Exists(cambria))
                return;

            var resolver = new FontResolver { NullIfFontNotFound = true };
            var face = resolver.ResolveTypeface("Cambria Math", 400, false);

            Assert.NotNull(face);
            var bytes = resolver.GetFont(face.FaceName);
            Assert.False(SfntCollection.IsCollection(bytes));
            Assert.True(FontFileData.GetOrCreateFrom(bytes).Fontface.math?.Table != null, "Cambria Math should carry a MATH table");
        }
    }
}
