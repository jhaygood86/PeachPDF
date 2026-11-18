using System.Text;
using PeachDrawing.PDF.Import;

namespace PeachDrawing.PDF.Tests.Import
{
    /// <summary>CMaps: the text form (an embedded CMap stream), variable-length codes, the predefined CMaps compiled from Adobe's resources, and the CID-to-Unicode tables.</summary>
    public class PdfCMapTests
    {
        private static string Bytes(params int[] bytes) => new(bytes.Select(b => (char)b).ToArray());

        private static PdfCMap Parse(string text, Func<string, PdfCMap?>? parent = null) =>
            PdfCMap.Parse(Encoding.ASCII.GetBytes(text), "Test", parent ?? (_ => null));

        [Fact]
        public void AnEmbeddedCMap_IsReadFromItsText()
        {
            var map = Parse("""
                /CIDInit /ProcSet findresource begin 12 dict begin begincmap
                /CMapName /Test def /WMode 1 def
                2 begincodespacerange <00> <80> <8140> <9FFC> endcodespacerange
                2 begincidrange <20> <7e> 1 <8140> <817e> 633 endcidrange
                1 begincidchar <a0> 500 endcidchar
                endcmap
                """);

            Assert.True(map.Vertical);
            Assert.Equal(2, map.Spaces.Count);
            Assert.Equal(1, map.Lookup(0x20, 1));
            Assert.Equal(34, map.Lookup(0x41, 1));
            Assert.Equal(633, map.Lookup(0x8140, 2));
            Assert.Equal(634, map.Lookup(0x8141, 2));
            Assert.Equal(500, map.Lookup(0xA0, 1));
            Assert.Equal(-1, map.Lookup(0x1F, 1));
            Assert.Equal(-1, map.Lookup(0x9000, 2));
        }

        [Fact]
        public void CodesOfDifferentLengths_AreSplitByTheCodeSpaces()
        {
            var map = Parse("2 begincodespacerange <00> <80> <8140> <9FFC> endcodespacerange 2 begincidrange <20> <7e> 1 <8140> <817e> 633 endcidrange");

            var codes = map.Decode(Bytes(0x41, 0x81, 0x41, 0x42)).ToList();

            Assert.Equal([new PdfCharCode(0x41, 1, 34), new PdfCharCode(0x8141, 2, 634), new PdfCharCode(0x42, 1, 35)], codes);
        }

        [Fact]
        public void BytesNoCodeSpaceAccepts_AreOneCodeOfTheShortestLengthWithCidZero()
        {
            var map = Parse("1 begincodespacerange <8140> <9FFC> endcodespacerange 1 begincidrange <8140> <817e> 633 endcidrange");

            var codes = map.Decode(Bytes(0x20, 0x20, 0x81, 0x40)).ToList();

            Assert.Equal([new PdfCharCode(0x2020, 2, 0), new PdfCharCode(0x8140, 2, 633)], codes);
        }

        [Fact]
        public void ACMapThatBuildsOnAnother_FallsBackToItsParent_AndAddsItsOwnCodeSpaces()
        {
            var parent = Parse("1 begincodespacerange <0000> <FFFF> endcodespacerange 1 begincidrange <0000> <00ff> 100 endcidrange");
            var child = Parse("/Parent usecmap 1 begincidchar <0041> 9 endcidchar", name => name == "Parent" ? parent : null);

            Assert.Same(parent, child.Parent);
            Assert.Equal(9, child.Lookup(0x41, 2));                  // its own
            Assert.Equal(100 + 0x42, child.Lookup(0x42, 2));         // the parent's
            Assert.Equal([new PdfCharCode(0x41, 2, 9)], child.Decode(Bytes(0, 0x41)));
        }

        [Fact]
        public void IdentityIsTwoBytesThatAreTheirOwnCid()
        {
            var map = PdfCMap.Identity(vertical: true);

            Assert.True(map.Vertical);
            Assert.Equal([new PdfCharCode(0x1234, 2, 0x1234), new PdfCharCode(0xFFFF, 2, 0xFFFF)], map.Decode(Bytes(0x12, 0x34, 0xFF, 0xFF)));
        }

        [Fact]
        public void JunkInACMap_IsSkippedAndDoesNotThrow()
        {
            var map = Parse("(unterminated string begincidrange <00> <ff> 1 endcidrange % comment\n 3 begincidrange <01> <0200> 5 <10> <00> 9 <zz> endcidrange <<>> [ ] { }");

            Assert.Equal(0, map.RangeCount);        // a range across code lengths or backwards is dropped
        }

        [Fact]
        public void ACMapSurvivesTheBinaryForm()
        {
            var map = Parse("/WMode 1 def 2 begincodespacerange <00> <80> <8140> <9FFC> endcodespacerange 3 begincidrange <20> <7e> 1 <8140> <817e> 633 <8200> <8210> 5 endcidrange 1 begincidchar <a0> 500 endcidchar");

            var back = PdfCMapBinary.DecodeMap("Test", PdfCMapBinary.EncodeMap(map), _ => null);

            Assert.True(back.Vertical);
            Assert.Equal(map.Spaces, back.Spaces);
            foreach (var (code, length) in new (uint, int)[] { (0x20, 1), (0x41, 1), (0x7E, 1), (0xA0, 1), (0x8140, 2), (0x817E, 2), (0x8205, 2), (0x8211, 2), (0x00, 1) })
                Assert.Equal(map.Lookup(code, length), back.Lookup(code, length));
        }

        [Fact]
        public void TheBundleHoldsThePredefinedCMapsOfTheFourCollections()
        {
            var names = PdfPredefinedCMaps.Names;

            Assert.True(names.Count >= 59);
            foreach (var expected in new[] { "UniJIS-UCS2-H", "UniJIS-UTF16-V", "90ms-RKSJ-H", "90ms-RKSJ-V", "EUC-H", "H", "V", "GBK-EUC-H", "GBK2K-H", "UniGB-UCS2-H",
                         "B5pc-H", "ETen-B5-H", "HKscs-B5-H", "UniCNS-UTF16-H", "KSCms-UHC-H", "KSC-EUC-H", "UniKS-UCS2-H" })
                Assert.Contains(expected, names);
            Assert.Null(PdfPredefinedCMaps.Get("NoSuchCMap-H"));
            Assert.True(PdfPredefinedCMaps.Get("Identity-V")!.Vertical);
        }

        [Fact]
        public void TheSameKanaHasTheSameCidInTheUnicodeAndTheShiftJisCMaps_AndThatCidIsThatKana()
        {
            var unicode = PdfPredefinedCMaps.Get("UniJIS-UCS2-H")!;
            var shiftJis = PdfPredefinedCMaps.Get("90ms-RKSJ-H")!;
            var ucs = PdfPredefinedCMaps.UnicodeOf("Japan1")!;

            int fromUnicode = unicode.Lookup(0x3042, 2);                 // HIRAGANA LETTER A
            int fromShiftJis = shiftJis.Lookup(0x82A0, 2);               // the same letter in Shift-JIS

            Assert.Equal(843, fromUnicode);
            Assert.Equal(fromUnicode, fromShiftJis);
            Assert.True(ucs.TryGet(843, out int cp));
            Assert.Equal(0x3042, cp);
        }

        [Fact]
        public void ShiftJisTextOfMixedWidths_IsSplitIntoOneAndTwoByteCodes()
        {
            var shiftJis = PdfPredefinedCMaps.Get("90ms-RKSJ-H")!;

            var codes = shiftJis.Decode(Bytes(0x41, 0x82, 0xA0, 0xB1)).ToList();      // A, the hiragana A, half-width katakana A

            Assert.Equal([1, 2, 1], codes.Select(c => c.Length));
            Assert.All(codes, c => Assert.True(c.Cid > 0));
        }

        [Fact]
        public void AVerticalCMapBuildsOnItsHorizontalOne()
        {
            var vertical = PdfPredefinedCMaps.Get("90ms-RKSJ-V")!;

            Assert.True(vertical.Vertical);
            Assert.Equal("90ms-RKSJ-H", vertical.Parent!.Name);
            Assert.Equal(843, vertical.Lookup(0x82A0, 2));                  // not overridden: the horizontal CMap's
            Assert.NotEqual(vertical.Parent.Lookup(0x8141, 2), vertical.Lookup(0x8141, 2));          // the ideographic comma has a vertical form
        }

        [Theory]
        [InlineData("Japan1", 34, 'A')]
        [InlineData("GB1", 34, 'A')]
        [InlineData("CNS1", 34, 'A')]
        [InlineData("Korea1", 34, 'A')]
        public void EveryCollectionKnowsItsLatinLetters(string ordering, int cid, char expected)
        {
            Assert.True(PdfPredefinedCMaps.UnicodeOf(ordering)!.TryGet(cid, out int cp));
            Assert.Equal(expected, (char)cp);
        }

        [Fact]
        public void AnUnknownOrdering_HasNoUnicodeTable()
        {
            Assert.Null(PdfPredefinedCMaps.UnicodeOf("Identity"));
            Assert.False(PdfPredefinedCMaps.UnicodeOf("Japan1")!.TryGet(-5, out _));
            Assert.False(PdfPredefinedCMaps.UnicodeOf("Japan1")!.TryGet(1_000_000, out _));
        }

        [Fact]
        public void TheCidToUnicodeTableSurvivesItsBinaryForm()
        {
            var pairs = new List<(int, int)> { (1, 0x20), (2, 0x21), (3, 0x22), (10, 0x3042), (11, 0x3043), (50, 0x1F600) };

            var table = PdfCMapBinary.DecodeCidUnicode(PdfCMapBinary.EncodeCidUnicode(pairs));

            foreach (var (cid, cp) in pairs)
            {
                Assert.True(table.TryGet(cid, out int got));
                Assert.Equal(cp, got);
            }

            Assert.False(table.TryGet(4, out _));
            Assert.False(table.TryGet(12, out _));
        }
    }
}
