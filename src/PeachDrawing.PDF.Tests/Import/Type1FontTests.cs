using System.Text;
using PeachDrawing;
using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Import;

namespace PeachDrawing.PDF.Tests.Import
{
    /// <summary>
    /// Type 1 fonts: a font is assembled here byte by byte (clear text, eexec and charstring encryption, subroutines, flex, seac), parsed,
    /// converted to CFF and drawn from a PDF, with the outline checked against what the charstrings say.
    /// </summary>
    public class Type1FontTests
    {
        // ------------------------------------------------------------ the font generator

        private static byte[] Encrypt(byte[] plain, ushort key, byte[] prefix)
        {
            var data = prefix.Concat(plain).ToArray();
            var o = new byte[data.Length];
            ushort r = key;
            for (var i = 0; i < data.Length; i++)
            {
                var c = (byte)(data[i] ^ (r >> 8));
                o[i] = c;
                r = (ushort)((c + r) * 52845 + 22719);
            }

            return o;
        }

        /// <summary>Assembles a Type 1 charstring from numbers and operator names.</summary>
        private static byte[] Cs(params object[] tokens)
        {
            var o = new List<byte>();
            foreach (var t in tokens)
            {
                if (t is int n)
                {
                    if (n is >= -107 and <= 107) o.Add((byte)(n + 139));
                    else if (n is >= 108 and <= 1131) { var v = n - 108; o.Add((byte)(247 + (v >> 8))); o.Add((byte)v); }
                    else if (n is >= -1131 and <= -108) { var v = -n - 108; o.Add((byte)(251 + (v >> 8))); o.Add((byte)v); }
                    else { o.Add(255); o.AddRange([(byte)(n >> 24), (byte)(n >> 16), (byte)(n >> 8), (byte)n]); }
                    continue;
                }

                switch ((string)t)
                {
                    case "hstem": o.Add(1); break;
                    case "vstem": o.Add(3); break;
                    case "vmoveto": o.Add(4); break;
                    case "rlineto": o.Add(5); break;
                    case "hlineto": o.Add(6); break;
                    case "vlineto": o.Add(7); break;
                    case "rrcurveto": o.Add(8); break;
                    case "closepath": o.Add(9); break;
                    case "callsubr": o.Add(10); break;
                    case "return": o.Add(11); break;
                    case "hsbw": o.Add(13); break;
                    case "endchar": o.Add(14); break;
                    case "rmoveto": o.Add(21); break;
                    case "hmoveto": o.Add(22); break;
                    case "vhcurveto": o.Add(30); break;
                    case "hvcurveto": o.Add(31); break;
                    case "dotsection": o.AddRange([12, 0]); break;
                    case "seac": o.AddRange([12, 6]); break;
                    case "sbw": o.AddRange([12, 7]); break;
                    case "div": o.AddRange([12, 12]); break;
                    case "callothersubr": o.AddRange([12, 16]); break;
                    case "pop": o.AddRange([12, 17]); break;
                    case "setcurrentpoint": o.AddRange([12, 33]); break;
                    default: throw new ArgumentException((string)t);
                }
            }

            return [.. o];
        }

        private static readonly (string Name, byte[] Code)[] Glyphs =
        [
            (".notdef", Cs(0, 500, "hsbw", "endchar")),
            // A: a 500x500 square whose left edge is at x = 50.
            ("A", Cs(50, 600, "hsbw", 0, 0, "rmoveto", 500, "hlineto", 500, "vlineto", -500, "hlineto", "closepath", "endchar")),
            // B: the same square, its two edges drawn by subroutine 4.
            ("B", Cs(50, 600, "hsbw", 0, 0, "rmoveto", 4, "callsubr", -500, "hlineto", "closepath", "endchar")),
            // C: starts with a flex (two curves, from (50,0) to (350,0) to (650,0)), then up and back along the top.
            ("C", Cs(50, 700, "hsbw", 0, 0, "rmoveto",
                1, "callsubr",
                250, 0, "rmoveto", 2, "callsubr",
                -150, 50, "rmoveto", 2, "callsubr",
                100, 0, "rmoveto", 2, "callsubr",
                100, -50, "rmoveto", 2, "callsubr",
                100, -50, "rmoveto", 2, "callsubr",
                100, 0, "rmoveto", 2, "callsubr",
                100, 50, "rmoveto", 2, "callsubr",
                50, 650, 0, 0, "callsubr",
                500, "vlineto", -600, "hlineto", "closepath", "endchar")),
            // acute: a 100x100 square at (50,700).
            ("acute", Cs(50, 300, "hsbw", 0, 700, "rmoveto", 100, "hlineto", 100, "vlineto", -100, "hlineto", "closepath", "endchar")),
            // Aacute: A with the acute shifted by sbx + adx - asb = 50 + 200 - 50 = 200.
            ("Aacute", Cs(50, 600, "hsbw", 50, 200, 0, 65, 194, "seac")),
            // D: two sides of a square by hint replacement (subroutine 4), a curve, a closed first contour, then a second contour whose
            // start is offset by "600 2 div".
            ("D", Cs(50, 600, "hsbw", 0, 0, "rmoveto", 100, 100, "hstem", 4, 1, 3, "callothersubr", "pop", "callsubr",
                100, -100, -100, -100, "hvcurveto", -500, "hlineto", "closepath",
                600, 2, "div", 0, "rmoveto", 50, "hlineto", 50, "vlineto", -50, "hlineto", "closepath", "endchar")),
        ];

        private static readonly (int Code, string Name)[] Encoded = [(65, "A"), (66, "B"), (67, "C"), (68, "D"), (194, "acute"), (69, "Aacute")];

        private static byte[] Subr(params object[] tokens) => Cs(tokens);

        /// <summary>Builds the font file: clear text, then the encrypted private part. <paramref name="lenIV"/> is the charstring cipher's random prefix length.</summary>
        private static byte[] BuildFont(bool standardEncoding = false, int lenIV = 4, bool hex = false, bool pfb = false, string name = "TestT1")
        {
            var clear = new StringBuilder();
            clear.Append("%!PS-AdobeFont-1.0: ").Append(name).Append(" 1.0\n");
            clear.Append("/FontName /").Append(name).Append(" def\n/FontMatrix [0.001 0 0 0.001 0 0] readonly def\n/FontBBox {0 0 700 800} readonly def\n");
            if (standardEncoding) clear.Append("/Encoding StandardEncoding def\n");
            else
            {
                clear.Append("/Encoding 256 array\n0 1 255 {1 index exch /.notdef put} for\n");
                foreach (var (code, glyph) in Encoded) clear.Append($"dup {code} /{glyph} put\n");
                clear.Append("readonly def\n");
            }

            clear.Append("currentdict end\ncurrentfile eexec\n");

            var priv = new List<byte>();
            void Text(string s) => priv.AddRange(Encoding.Latin1.GetBytes(s));
            byte[] CharString(byte[] code) => lenIV < 0 ? code : Encrypt(code, 4330, new byte[lenIV]);
            void Binary(string intro, byte[] data, string outro)
            {
                Text($"{intro} {data.Length} RD ");
                priv.AddRange(data);
                Text($" {outro}\n");
            }

            Text("dup /Private 8 dict dup begin\n/RD {string currentfile exch readstring pop} executeonly def\n/ND {noaccess def} executeonly def\n/NP {noaccess put} executeonly def\n");
            if (lenIV != 4) Text($"/lenIV {lenIV} def\n");
            Text("/Subrs 5 array\n");
            byte[][] subrs =
            [
                Subr(3, 0, "callothersubr", "pop", "pop", "setcurrentpoint", "return"),          // 0: end flex
                Subr(0, 1, "callothersubr", "return"),                                            // 1: start flex
                Subr(0, 2, "callothersubr", "return"),                                            // 2: a flex point
                Subr("return"),                                                                   // 3: (hint replacement does its work inline)
                Subr(500, "hlineto", 500, "vlineto", "return"),                                   // 4: two sides of a square
            ];
            for (var i = 0; i < subrs.Length; i++) Binary($"dup {i}", CharString(subrs[i]), "NP");
            Text("ND\n2 index /CharStrings " + Glyphs.Length + " dict dup begin\n");
            foreach (var (glyph, code) in Glyphs) Binary("/" + glyph, CharString(code), "ND");
            Text("end\nend\nreadonly put\nnoaccess put\ndup /FontName get exch definefont pop\nmark currentfile closefile\n");

            var cipher = Encrypt([.. priv], 55665, [0xAA, 0xBB, 0xCC, 0xDD]);
            var trailer = Encoding.ASCII.GetBytes("\n" + string.Concat(Enumerable.Repeat(new string('0', 64) + "\n", 8)) + "cleartomark\n");
            var clearBytes = Encoding.Latin1.GetBytes(clear.ToString());

            if (hex)
            {
                var hexText = new StringBuilder();
                for (var i = 0; i < cipher.Length; i++)
                {
                    hexText.Append(cipher[i].ToString("X2"));
                    if (i % 32 == 31) hexText.Append('\n');
                }

                cipher = Encoding.ASCII.GetBytes(hexText.ToString());
            }

            if (!pfb) return [.. clearBytes, .. cipher, .. trailer];

            byte[] Segment(int type, byte[] bytes) => [0x80, (byte)type, (byte)bytes.Length, (byte)(bytes.Length >> 8), (byte)(bytes.Length >> 16), (byte)(bytes.Length >> 24), .. bytes];
            return [.. Segment(1, clearBytes), .. Segment(2, cipher), .. Segment(1, trailer), 0x80, 3];
        }

        // ------------------------------------------------------------ parsing

        [Fact]
        public void Parse_ReadsTheHeader_TheEncoding_AndEveryGlyph()
        {
            var font = Type1Font.Parse(BuildFont())!;

            Assert.Equal("TestT1", font.Name);
            Assert.Equal([0.001, 0, 0, 0.001, 0, 0], font.FontMatrix);
            Assert.Equal([0.0, 0, 700, 800], font.FontBBox);
            Assert.Equal(".notdef A B C acute Aacute D".Split(' '), font.GlyphNames);
            Assert.Equal("A", font.Encoding![65]);
            Assert.Equal("acute", font.Encoding[194]);
            Assert.Equal(6, font.Encoding.Count);                         // the .notdef fill entries are not codes
        }

        [Theory]
        [InlineData(true, false)]       // hexadecimal eexec section
        [InlineData(false, true)]       // PFB segment headers
        [InlineData(true, true)]
        public void Parse_ReadsHexEexecAndPfbWrappers(bool hex, bool pfb)
        {
            var font = Type1Font.Parse(BuildFont(hex: hex, pfb: pfb))!;

            Assert.Equal(7, font.GlyphNames.Count);
            Assert.NotNull(font.GetOutline("A"));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(-1)]
        public void Parse_HonoursLenIV_IncludingAnUnencryptedCharstring(int lenIV)
        {
            var font = Type1Font.Parse(BuildFont(lenIV: lenIV))!;

            Assert.Equal(500, font.GetOutline("A")!.Contours[0].Segments[2].End.Y - 0, 6);
        }

        [Fact]
        public void StandardEncoding_Leaves_TheEncodingNull()
        {
            Assert.Null(Type1Font.Parse(BuildFont(standardEncoding: true))!.Encoding);
        }

        [Theory]
        [InlineData(new byte[0])]
        [InlineData(new byte[] { 0x80, 1, 0, 0, 0, 0 })]
        public void NotAType1Font_IsNull(byte[] data)
        {
            Assert.Null(Type1Font.Parse(data));
            Assert.Null(Type1Font.Parse(Encoding.ASCII.GetBytes("%!PS-AdobeFont-1.0\n/FontName /X def\n")));       // no eexec section
            Assert.Null(Type1Font.Parse(Encoding.ASCII.GetBytes("currentfile eexec\n0000 not encrypted binary data at all")));
        }

        // ------------------------------------------------------------ the charstring interpreter

        [Fact]
        public void Square_IsTracedFromItsSideBearingPoint_WithItsWidth()
        {
            var a = Type1Font.Parse(BuildFont())!.GetOutline("A")!;

            Assert.Equal(600, a.Width);
            var contour = Assert.Single(a.Contours);
            Assert.Equal(new Type1Font.Point(50, 0), contour.Start);
            Assert.Equal([new Type1Font.Point(550, 0), new Type1Font.Point(550, 500), new Type1Font.Point(50, 500)], contour.Segments.Select(s => s.End));
            Assert.All(contour.Segments, s => Assert.True(s.IsLine));
        }

        [Fact]
        public void Subroutines_ProduceTheSameOutline_AsInlineCode()
        {
            var font = Type1Font.Parse(BuildFont())!;

            Assert.Equal(font.GetOutline("A")!.Contours[0].Segments.Select(s => s.End), font.GetOutline("B")!.Contours[0].Segments.Select(s => s.End));
        }

        [Fact]
        public void Flex_BecomesTwoCurves_AndTheCurrentPointIsTheFlexEnd()
        {
            var contour = Assert.Single(Type1Font.Parse(BuildFont())!.GetOutline("C")!.Contours);

            Assert.Equal(new Type1Font.Point(50, 0), contour.Start);
            Assert.Equal(4, contour.Segments.Count);
            Assert.False(contour.Segments[0].IsLine);
            Assert.Equal(new Type1Font.Point(150, 50), contour.Segments[0].C1);          // flex point 1
            Assert.Equal(new Type1Font.Point(350, 0), contour.Segments[0].End);          // flex point 3
            Assert.Equal(new Type1Font.Point(650, 0), contour.Segments[1].End);          // flex point 6
            Assert.Equal(new Type1Font.Point(650, 500), contour.Segments[2].End);        // the line that follows starts from the flex end
            Assert.Equal(new Type1Font.Point(50, 500), contour.Segments[3].End);
        }

        [Fact]
        public void Seac_ComposesTheBaseAndAnAccentOffsetBySidebearingAndAdx()
        {
            var composite = Type1Font.Parse(BuildFont())!.GetOutline("Aacute")!;

            Assert.Equal(2, composite.Contours.Count);
            Assert.Equal(new Type1Font.Point(50, 0), composite.Contours[0].Start);       // the base
            Assert.Equal(new Type1Font.Point(250, 700), composite.Contours[1].Start);    // the accent at (50,700), shifted by 200
            Assert.Equal(600, composite.Width);
        }

        [Fact]
        public void CurveOperators_DivisionAndHintReplacement_AreInterpreted()
        {
            var d = Type1Font.Parse(BuildFont())!.GetOutline("D")!;

            Assert.Equal(2, d.Contours.Count);
            var first = d.Contours[0];
            Assert.Equal(new Type1Font.Point(550, 0), first.Segments[0].End);              // the two sides came from subroutine 4 by hint replacement
            Assert.Equal(new Type1Font.Point(550, 500), first.Segments[1].End);
            Assert.False(first.Segments[2].IsLine);                                         // hvcurveto
            Assert.Equal(new Type1Font.Point(650, 500), first.Segments[2].C1);
            Assert.Equal(new Type1Font.Point(550, 300), first.Segments[2].End);
            Assert.Equal(new Type1Font.Point(50, 300), first.Segments[3].End);
            Assert.Equal(new Type1Font.Point(350, 300), d.Contours[1].Start);               // 600 2 div = 300 from the point the first contour ended at
        }

        [Fact]
        public void GlyphThatDoesNotExist_IsNull_AndSeacDepthIsBounded()
        {
            var font = Type1Font.Parse(BuildFont())!;

            Assert.Null(font.GetOutline("nope"));
            Assert.NotNull(font.GetOutline("Aacute", depth: 5));
        }

        // ------------------------------------------------------------ conversion to CFF

        [Fact]
        public void ToCff_ProducesAFontWithTheSameGlyphsNamesAndEncoding()
        {
            var cff = CffFont.Parse(Type1Font.Parse(BuildFont())!.ToCff())!;

            Assert.False(cff.IsCid);
            Assert.Equal(7, cff.GlyphCount);
            Assert.Equal(".notdef", cff.GlyphNames[0]);
            Assert.Contains("Aacute", cff.GlyphNames);
            Assert.Contains("acute", cff.GlyphNames);
            // Glyphs with a code come first, in code order: A B C D Aacute(69) acute(194).
            Assert.Equal(["A", "B", "C", "D", "Aacute", "acute"], cff.GlyphNames.Skip(1).Take(6));
            Assert.Equal(1, cff.BuiltInEncoding[65]);
            Assert.Equal(6, cff.BuiltInEncoding[194]);
            Assert.Equal(0.001, cff.FontMatrix[0], 9);
            Assert.Equal([0.0, 0, 700, 800], cff.FontBBox);
        }

        [Fact]
        public void ToCff_WithStandardEncoding_AssignsTheStandardCodes()
        {
            var cff = CffFont.Parse(Type1Font.Parse(BuildFont(standardEncoding: true))!.ToCff())!;

            var a = Array.IndexOf(cff.GlyphNames, "A");
            Assert.Equal(a, cff.BuiltInEncoding[65]);
            var acute = Array.IndexOf(cff.GlyphNames, "acute");
            Assert.Equal(acute, cff.BuiltInEncoding[194]);                 // StandardEncoding puts acute at 194
        }

        // ------------------------------------------------------------ in a PDF

        private static byte[] Pdf(byte[] font, string text, string fontExtras = "")
        {
            var dict = $"<< /Type /Font /Subtype /Type1 /BaseFont /TestT1 /FirstChar 65 /LastChar 69 /Widths [600 600 700 600 600] {fontExtras} /FontDescriptor 6 0 R >>";
            var descriptor = "<< /Type /FontDescriptor /FontName /TestT1 /Flags 4 /FontFile 7 0 R >>";
            return TestPdf.Build($"BT /F0 100 Tf 10 10 Td ({text}) Tj ET", resources: "/Font << /F0 5 0 R >>",
                extraObjects: [dict, descriptor, TestPdf.Stream("/Length1 10 /Length2 10 /Length3 0", TestPdf.Binary(font))]);
        }

        private static (byte R, byte G, byte B, byte A) Pixel(byte[] pdf, int x, int y, out IReadOnlyCollection<string> unsupported)
        {
            var doc = PdfSourceDocument.Open(pdf);
            using var canvas = new RasterRenderContext().CreateCanvas(100, 100);
            unsupported = PdfPagePainter.Paint(doc, doc.Pages[0], canvas);
            var span = canvas.ToPixelBuffer().PremultipliedRgba.Span;
            var i = (y * 100 + x) * 4;
            return (span[i], span[i + 1], span[i + 2], span[i + 3]);
        }

        [Fact]
        public void Type1Font_IsDrawn_WithTheOutlineItsCharstringsDescribe()
        {
            // 'A' at size 100 from (10,10): the 500x500 square spans x 15..65 (50..550 -> 5..55, shifted by 10), y 10..60 (canvas y 40..90).
            var pdf = Pdf(BuildFont(), "A");

            Assert.True(Pixel(pdf, 40, 65, out var unsupported).A > 200, "inside the square");
            Assert.Empty(unsupported);
            Assert.True(Pixel(pdf, 80, 65, out _).A < 20, "right of it");
            Assert.True(Pixel(pdf, 40, 20, out _).A < 20, "above it");
        }

        [Fact]
        public void Type1Flex_AndSeacGlyphs_AreDrawnInTheirPlaces()
        {
            // C: x 15..75 wide with a wavy bottom edge between y 0 and 50 units; Aacute: the square, plus the accent at x 35..45, y 80..90 (canvas 10..20).
            Assert.True(Pixel(Pdf(BuildFont(), "C"), 45, 65, out _).A > 200);
            var composite = Pdf(BuildFont(), "E");
            Assert.True(Pixel(composite, 40, 65, out _).A > 200, "the base square");
            Assert.True(Pixel(composite, 40, 15, out _).A > 200, "the accent above it");
            Assert.True(Pixel(composite, 80, 15, out _).A < 20);
        }

        [Fact]
        public void BuiltInEncoding_IsUsed_WhenThePdfGivesNone_AndTheDifferencesWhenItDoes()
        {
            // Code 66 is 'B' in the font's own encoding; a Differences array can point it at 'A' instead (same shape, so ink either way:
            // the check is that both resolve to a glyph at all, through the two different paths).
            Assert.True(Pixel(Pdf(BuildFont(), "B"), 40, 65, out var withBuiltIn).A > 200);
            Assert.Empty(withBuiltIn);
            Assert.True(Pixel(Pdf(BuildFont(), "B", "/Encoding << /Differences [66 /C] >>"), 45, 65, out var withDifferences).A > 200);
            Assert.Empty(withDifferences);
        }

        [Fact]
        public void Type1Program_ThatCannotBeRead_IsReported()
        {
            var pdf = Pdf([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14], "A");

            Pixel(pdf, 1, 1, out var unsupported);

            Assert.Contains("font: embedded font is not an sfnt, CFF or Type 1 program", unsupported);
        }
    }
}
