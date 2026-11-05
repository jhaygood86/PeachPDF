using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using PeachDrawing.Text.Internal.Fonts;
using PeachDrawing.Text.Internal.Fonts.Apple;
using PeachPDF.Tests.TestSupport;

namespace PeachDrawing.Text.Tests.Fonts
{
    /// <summary>
    /// The pieces of the Apple (CoreText) system font source that run on any host: the sfnt assembler that puts a
    /// font back together from CoreText's tables, and the storage that names a font without a file. The CoreText
    /// calls themselves are covered by the tests at the end, which run on an Apple OS only.
    /// </summary>
    public class AppleSystemFontsTests
    {
        private static readonly byte[] FontBytes = File.ReadAllBytes(BundledFonts.Ttf);

        /// <summary>Every table of a font file, by tag, read straight off its table directory.</summary>
        private static List<(uint Tag, byte[] Data)> ReadTables(byte[] font)
        {
            int count = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
            var tables = new List<(uint, byte[])>();
            for (var i = 0; i < count; i++)
            {
                var record = font.AsSpan(12 + 16 * i);
                var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(record[8..]);
                var length = (int)BinaryPrimitives.ReadUInt32BigEndian(record[12..]);
                tables.Add((BinaryPrimitives.ReadUInt32BigEndian(record), font.AsSpan(offset, length).ToArray()));
            }
            return tables;
        }

        [Fact]
        public void Tag_PacksFourCharactersBigEndian()
        {
            Assert.Equal(0x6E616D65u, SfntAssembler.Tag("name"));
            Assert.Equal(0x4F532F32u, SfntAssembler.Tag("OS/2"));
            Assert.Throws<ArgumentException>(() => SfntAssembler.Tag("abc"));
        }

        [Fact]
        public void Build_WritesASortedAlignedDirectory_WhoseChecksumsMatchTheirTables()
        {
            var tables = ReadTables(FontBytes);
            tables.Reverse(); // the assembler must not depend on the order it is given

            var built = SfntAssembler.Build(tables);
            var rebuilt = ReadTables(built);

            Assert.Equal(tables.Count, rebuilt.Count);
            Assert.Equal(tables.Select(t => t.Tag).Order(), rebuilt.Select(t => t.Tag));

            var count = BinaryPrimitives.ReadUInt16BigEndian(built.AsSpan(4));
            for (var i = 0; i < count; i++)
            {
                var record = built.AsSpan(12 + 16 * i);
                var tag = BinaryPrimitives.ReadUInt32BigEndian(record);
                var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(record[8..]);
                var length = (int)BinaryPrimitives.ReadUInt32BigEndian(record[12..]);

                Assert.Equal(0, offset % 4);
                Assert.Equal(SfntAssembler.Checksum(built.AsSpan(offset, length)), BinaryPrimitives.ReadUInt32BigEndian(record[4..]));
                Assert.Equal(tables.Single(t => t.Tag == tag).Data, built.AsSpan(offset, length).ToArray());
            }
        }

        [Theory]
        [InlineData(1, 0, 16, 0)]
        [InlineData(2, 1, 32, 0)]
        [InlineData(3, 1, 32, 16)]
        [InlineData(9, 3, 128, 16)]
        public void Build_FillsInTheBinarySearchHeader(int tableCount, int entrySelector, int searchRange, int rangeShift)
        {
            var tables = Enumerable.Range(0, tableCount)
                .Select(i => (SfntAssembler.Tag($"tb{(char)('a' + i)}1"), new byte[] { 1, 2, 3 }))
                .ToList();

            var built = SfntAssembler.Build(tables);

            Assert.Equal(tableCount, BinaryPrimitives.ReadUInt16BigEndian(built.AsSpan(4)));
            Assert.Equal(searchRange, BinaryPrimitives.ReadUInt16BigEndian(built.AsSpan(6)));
            Assert.Equal(entrySelector, BinaryPrimitives.ReadUInt16BigEndian(built.AsSpan(8)));
            Assert.Equal(rangeShift, BinaryPrimitives.ReadUInt16BigEndian(built.AsSpan(10)));
        }

        [Fact]
        public void Build_LeavesOutEmptyTables_AndKeepsTheFirstOfARepeatedTag()
        {
            var tag = SfntAssembler.Tag("test");
            var built = SfntAssembler.Build([(tag, new byte[] { 1 }), (tag, new byte[] { 2 }), (SfntAssembler.Tag("none"), [])]);

            var table = Assert.Single(ReadTables(built));
            Assert.Equal(tag, table.Tag);
            Assert.Equal(new byte[] { 1 }, table.Data);
        }

        [Fact]
        public void Build_MarksAFontWithCffOutlinesAsOpenType()
        {
            Assert.Equal(0x00010000u, BinaryPrimitives.ReadUInt32BigEndian(SfntAssembler.Build([(SfntAssembler.Tag("glyf"), new byte[4])])));
            Assert.Equal(0x4F54544Fu, BinaryPrimitives.ReadUInt32BigEndian(SfntAssembler.Build([(SfntAssembler.Tag("CFF "), new byte[4])])));
            Assert.Equal(0x4F54544Fu, BinaryPrimitives.ReadUInt32BigEndian(SfntAssembler.Build([(SfntAssembler.Tag("CFF2"), new byte[4])])));
        }

        [Fact]
        public void Checksum_PadsATrailingPartialWordWithZeros()
        {
            Assert.Equal(0x01020304u + 0x05000000u, SfntAssembler.Checksum([1, 2, 3, 4, 5]));
            Assert.Equal(0u, SfntAssembler.Checksum([]));
        }

        [Fact]
        public void AssembledDescriptionTables_ReadAsTheWholeFontDoes()
        {
            // A virtual font's description is read from just the tables a description needs.
            var wanted = SystemFontStorage.DescriptionTables.Select(SfntAssembler.Tag).ToHashSet();
            var partial = SfntAssembler.Build(ReadTables(FontBytes).Where(t => wanted.Contains(t.Tag)).ToList());

            var expected = TtfFontDescription.LoadDescription(new MemoryStream(FontBytes));
            var actual = TtfFontDescription.LoadDescription(new MemoryStream(partial));

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void IsVirtual_RecognisesOnlyThePrefixedPaths()
        {
            Assert.True(SystemFontStorage.IsVirtual("coretext:Helvetica"));
            Assert.False(SystemFontStorage.IsVirtual("/System/Library/Fonts/Helvetica.ttc"));
            Assert.False(SystemFontStorage.IsVirtual("Coretext:Helvetica"));
        }

        [Fact]
        public void OpenForDescription_AFile_IsTheFileItself()
        {
            using var stream = SystemFontStorage.OpenForDescription(BundledFonts.Ttf);
            Assert.Equal(FontBytes.Length, stream.Length);
        }

        [Fact]
        public void OpenForDescriptionAndCoverage_AVirtualPath_AskForOnlyTheTablesNeeded()
        {
            IReadOnlyList<string>? asked = null;
            byte[]? Loader(string path, IReadOnlyList<string>? tables)
            {
                asked = tables;
                Assert.Equal("coretext:Test-Regular", path);
                return SfntAssembler.Build(ReadTables(FontBytes));
            }

            using (SystemFontStorage.OpenForDescription("coretext:Test-Regular", Loader))
                Assert.Equal(SystemFontStorage.DescriptionTables, asked);

            using (SystemFontStorage.OpenForCoverage("coretext:Test-Regular", Loader))
                Assert.Equal(SystemFontStorage.CoverageTables, asked);

            using (SystemFontStorage.OpenRead("coretext:Test-Regular", Loader))
                Assert.Null(asked);
        }

        [Fact]
        public void ReadVirtual_AMissingFont_IsAFileNotFound()
        {
            Assert.Throws<FileNotFoundException>(() => SystemFontStorage.ReadVirtual("coretext:Nope", (_, _) => null));
            Assert.Throws<FileNotFoundException>(() => SystemFontStorage.OpenRead("coretext:Nope", (_, _) => null));
        }

        [Fact]
        public void ReadVirtual_ReturnsWhatTheLoaderMade()
        {
            var made = SfntAssembler.Build(ReadTables(FontBytes));
            Assert.Same(made, SystemFontStorage.ReadVirtual("coretext:Any", (_, _) => made));
        }

        [Fact]
        public void LoadSystemFontBytes_AVirtualPathOffAnApplePlatform_HasNoFont()
        {
            if (OperatingSystem.IsMacOS() || OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst() || OperatingSystem.IsTvOS())
                Assert.Skip("CoreText answers for real on this host.");

            Assert.Throws<FileNotFoundException>(() => FontResolver.LoadSystemFontBytes("coretext:Helvetica", 0));
            Assert.Empty(FontResolver.ReadSystemCoverage("coretext:Helvetica", 0));
        }

        // ---- CoreText itself: Apple hosts only ----

        [SuppressMessage("Interoperability", "CA1416", Justification = "Guarded by the Skip above each call.")]
        private static void RequireApple()
        {
            if (!(OperatingSystem.IsMacOS() || OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst() || OperatingSystem.IsTvOS()))
                Assert.Skip("CoreText exists on Apple platforms only.");
        }

        [Fact]
        [SuppressMessage("Interoperability", "CA1416", Justification = "Guarded by RequireApple.")]
        public void CoreText_ListsInstalledFonts_AsFilesOrAsPostScriptNames()
        {
            RequireApple();

            var withFiles = AppleSystemFonts.Discover(preferFiles: true);
            var byName = AppleSystemFonts.Discover(preferFiles: false);

            Assert.NotEmpty(withFiles);
            Assert.NotEmpty(byName);
            Assert.All(byName, p => Assert.True(SystemFontStorage.IsVirtual(p)));
            Assert.All(withFiles, p => Assert.True(SystemFontStorage.IsVirtual(p) || File.Exists(p)));
            Assert.Equal(withFiles.Length, withFiles.Distinct().Count());
        }

        [Fact]
        [SuppressMessage("Interoperability", "CA1416", Justification = "Guarded by RequireApple.")]
        public void CoreText_AssemblesAFont_WhoseDescriptionAndCmapRead()
        {
            RequireApple();

            var assembled = 0;
            foreach (var path in AppleSystemFonts.Discover(preferFiles: false).Take(25))
            {
                using var stream = SystemFontStorage.OpenForDescription(path);
                var description = TtfFontDescription.LoadDescription(stream);
                Assert.False(string.IsNullOrEmpty(description.FontFamilyInvariantCulture));

                var whole = FontResolver.LoadSystemFontBytes(path, 0);
                Assert.True(whole.Length > 12);
                assembled++;
            }

            Assert.True(assembled > 0);
        }

        [Fact]
        [SuppressMessage("Interoperability", "CA1416", Justification = "Guarded by RequireApple.")]
        public void CoreText_AnUnknownPostScriptName_HasNoFont_NotASubstitute()
        {
            RequireApple();

            Assert.Null(AppleSystemFonts.Assemble(SystemFontStorage.VirtualPrefix + "PeachPDF-No-Such-Font", null));
        }

        [Fact]
        [SuppressMessage("Interoperability", "CA1416", Justification = "Guarded by RequireApple.")]
        public void CoreText_TheResolverFindsAWellKnownFamily()
        {
            RequireApple();

            var resolver = new FontResolver();
            Assert.True(resolver.TryGetFamilyName("Helvetica", out _), "Helvetica ships with every Apple OS that exposes fonts to apps.");

            var info = resolver.ResolveTypeface("Helvetica", isBold: false, isItalic: false);
            var bytes = resolver.GetFont(info.FaceName);
            Assert.True(bytes.Length > 12);
        }

        [Theory]
        [InlineData(GenericFamily.SystemUi)]
        [InlineData(GenericFamily.UiSansSerif)]
        [InlineData(GenericFamily.UiSerif)]
        [InlineData(GenericFamily.UiMonospace)]
        [InlineData(GenericFamily.UiRounded)]
        [InlineData(GenericFamily.Serif)]
        [InlineData(GenericFamily.SansSerif)]
        [InlineData(GenericFamily.Monospace)]
        [InlineData(GenericFamily.Math)]
        public void CoreText_EveryGenericResolvesToALoadableFamily(GenericFamily generic)
        {
            RequireApple();

            var fonts = new FontSet();
            var family = fonts.ResolveGeneric(generic);

            Assert.False(string.IsNullOrEmpty(family), $"{generic} has an answer on an Apple platform");
            Assert.True(fonts.TryFindFamily(family!, out var found));
            Assert.True(found.TryMatch(new TypefaceQuery(), out var match));
            Assert.True(match.Typeface.Metrics.UnitsPerEm > 0);
        }

        [Fact]
        [SuppressMessage("Interoperability", "CA1416", Justification = "Guarded by RequireApple.")]
        public void CoreText_TheInterfaceDesignsAreDistinctFamilies_ThatTheResolverCanName()
        {
            RequireApple();

            var families = Enum.GetValues<AppleUiDesign>().Select(FontResolver.AppleUiFamilyName).ToList();

            Assert.All(families, family => Assert.False(string.IsNullOrEmpty(family)));
            Assert.Equal(families.Count, families.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        [Fact]
        public void TheInterfaceFamilyIsUnknownOffTheApplePlatforms()
        {
            if (OperatingSystem.IsMacOS() || OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst() || OperatingSystem.IsTvOS())
                Assert.Skip("This host is an Apple platform.");

            Assert.Null(FontResolver.AppleUiFamilyName(AppleUiDesign.System));
        }
    }
}
