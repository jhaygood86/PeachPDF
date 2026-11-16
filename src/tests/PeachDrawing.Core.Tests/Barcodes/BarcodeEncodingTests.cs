using PeachDrawing.Core.Barcodes;

namespace PeachDrawing.Core.Tests.Barcodes
{
    public class BarcodeEncodingTests
    {
        [Fact]
        public void Code128_UsesCodeSetCForDigitPairs()
            => Assert.Equal([105, 12, 34, 56, 78, 90, 85], Code128.GetSymbolValues("1234567890"));

        [Fact]
        public void Code128_SwitchesBetweenSets()
            => Assert.Equal([104, 48, 42, 42, 17, 18, 19, 35, 55], Code128.GetSymbolValues("PJJ123C"));

        [Fact]
        public void Code128_ModulesEndWithTheThirteenModuleStop()
        {
            var modules = Code128.GetModules("1234567890");
            Assert.Equal(7 * 11 + 13, modules.Length);
            Assert.True(modules[0]);
            Assert.True(modules[^1]);
        }

        [Theory]
        [InlineData("")]
        [InlineData("café")]
        public void Code128_RejectsEmptyAndNonAscii(string text)
            => Assert.Throws<ArgumentException>(() => Code128.GetSymbolValues(text));

        [Fact]
        public void Gs1128_PutsFnc1FirstAndAfterVariableLengthElements()
        {
            string text = Gs1Elements.ToCode128Text("(01)09501101530003(10)AB-123(17)140704");
            Assert.Equal(Code128.Fnc1 + "0109501101530003" + "10AB-123" + Code128.Fnc1 + "17140704", text);
            Assert.Equal([105, 102, 1, 9, 50], Code128.GetSymbolValues(text).Take(5));
        }

        [Theory]
        [InlineData("0109501101530003")]
        [InlineData("(1)1")]
        [InlineData("(01)")]
        [InlineData("")]
        public void Gs1Elements_RejectsMalformedText(string elements)
            => Assert.Throws<ArgumentException>(() => Gs1Elements.ToCode128Text(elements));

        [Fact]
        public void Gs1Elements_LeavesFixedLengthElementsWithoutSeparator()
        {
            Assert.Equal(Code128.Fnc1 + "3103000123" + "10A", Gs1Elements.ToCode128Text("(3103)000123(10)A"));
            Assert.Equal(Code128.Fnc1 + "4101234567890123" + "10A", Gs1Elements.ToCode128Text("(410)1234567890123(10)A"));
        }

        [Fact]
        public void Gs1_OnlyAis410To417AreFixedAt13Digits()
            => Assert.Equal(Code128.Fnc1 + "4181234" + Code128.Fnc1 + "10A", Gs1Elements.ToCode128Text("(418)1234(10)A"));

        [Theory]
        [InlineData("590123412345", "5901234123457")]
        [InlineData("400638133393", "4006381333931")]
        public void Ean13_CompletesTheCheckDigit(string digits, string expected)
            => Assert.Equal(expected, Barcode.CreateEan13(digits).Text);

        [Fact]
        public void EanAndUpc_ModuleCountsAndCheckDigits()
        {
            Assert.Equal(95, Barcode.CreateEan13("5901234123457").Columns);
            Assert.Equal(67, Barcode.CreateEan8("96385074").Columns);
            Assert.Equal(95, Barcode.CreateUpcA("036000291452").Columns);
            Assert.Equal("96385074", Barcode.CreateEan8("9638507").Text);
            Assert.Equal("036000291452", Barcode.CreateUpcA("03600029145").Text);
        }

        [Theory]
        [InlineData("590123412345X")]
        [InlineData("5901234123458")]
        [InlineData("123")]
        public void Ean13_RejectsBadInput(string digits)
            => Assert.Throws<ArgumentException>(() => Barcode.CreateEan13(digits));

        [Fact]
        public void Qr_VersionOneIs21ModulesWithFinderPatterns()
        {
            var qr = Barcode.CreateQrCode("HELLO WORLD");
            Assert.Equal(21, qr.Columns);
            Assert.Equal(21, qr.Rows);
            foreach (var (r, c) in new[] { (0, 0), (0, 14), (14, 0) })
            {
                Assert.True(qr.IsDark(r, c));
                Assert.True(qr.IsDark(r + 3, c + 3));
                Assert.False(qr.IsDark(r + 1, c + 1));
            }
        }

        [Fact]
        public void Qr_GrowsWithTextAndErrorCorrection()
        {
            var small = Barcode.CreateQrCode("0123456789", new BarcodeOptions { ErrorCorrection = QrErrorCorrectionLevel.L });
            var big = Barcode.CreateQrCode(new string('x', 300), new BarcodeOptions { ErrorCorrection = QrErrorCorrectionLevel.H });
            Assert.Equal(21, small.Columns);
            Assert.True(big.Columns > 70);
            Assert.Equal(17 + 4 * 40, Barcode.CreateQrCode("a", new BarcodeOptions { QrVersion = 40 }).Columns);
        }

        [Fact]
        public void Qr_RejectsEmptyOverlongAndBadVersion()
        {
            Assert.Throws<ArgumentException>(() => Barcode.CreateQrCode(""));
            Assert.Throws<ArgumentException>(() => Barcode.CreateQrCode(new string('x', 8000)));
            Assert.Throws<ArgumentOutOfRangeException>(() => Barcode.CreateQrCode("a", new BarcodeOptions { QrVersion = 41 }));
        }

        [Fact]
        public void Qr_EncodesNonAsciiText()
            => Assert.True(Barcode.CreateQrCode("café ☕").Columns >= 21);

        [Fact]
        public void DataMatrix_PicksTheSmallestSquareSize()
        {
            var a = Barcode.CreateDataMatrix("A");
            Assert.Equal(10, a.Rows);
            Assert.Equal(10, a.Columns);
            // The L-shaped finder: the left column and bottom row are dark.
            for (int i = 0; i < 10; i++)
            {
                Assert.True(a.IsDark(i, 0));
                Assert.True(a.IsDark(9, i));
            }
            Assert.True(Barcode.CreateDataMatrix(new string('7', 100)).Rows > 10);
        }

        [Fact]
        public void DataMatrix_HonorsFixedAndRectangularSizesAndBase256()
        {
            var rect = Barcode.CreateDataMatrix("AB", new BarcodeOptions { DataMatrixRows = 8, DataMatrixColumns = 18 });
            Assert.Equal((8, 18), (rect.Rows, rect.Columns));
            var b = Barcode.CreateDataMatrix("hello", new BarcodeOptions { DataMatrixEncoding = DataMatrixEncoding.Base256 });
            Assert.True(b.Rows >= 10);
            Assert.Throws<ArgumentException>(() => Barcode.CreateDataMatrix(new string('x', 5000)));
        }

        [Fact]
        public void IsDark_ForLinearCodesHasOneRow()
        {
            var ean = Barcode.CreateEan13("5901234123457");
            Assert.True(ean.IsLinear);
            Assert.Equal(1, ean.Rows);
            Assert.True(ean.IsDark(0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => ean.IsDark(1, 0));
        }
    }

    public class BarcodeLayoutTests
    {
        static double Area(IEnumerable<Rect> rects) => rects.Sum(r => r.Width * r.Height);

        [Fact]
        public void Qr_LayoutMergesRunsWithoutOverlapAndKeepsTheDarkArea()
        {
            var qr = Barcode.CreateQrCode("https://peachpdf.net/");
            var layout = qr.CreateLayout();
            int dark = 0;
            for (int r = 0; r < qr.Rows; r++)
                for (int c = 0; c < qr.Columns; c++)
                    if (qr.IsDark(r, c)) dark++;
            Assert.Equal(dark, Area(layout.Bars));
            Assert.Equal(qr.Columns + 8, layout.Width);
            Assert.True(layout.Bars.Count < dark);
            foreach (var bar in layout.Bars)
                Assert.True(bar.X >= 4 && bar.Right <= layout.Width - 4);
        }

        [Fact]
        public void Qr_QuietZoneOptionChangesTheMargin()
            => Assert.Equal(25, Barcode.CreateQrCode("a", new BarcodeOptions { QuietZone = 2 }).CreateLayout().Width);

        [Fact]
        public void QuietZone_MustBeFiniteAndNotNegative()
        {
            Assert.Throws<InvalidOperationException>(() => Barcode.CreateQrCode("a", new BarcodeOptions { QuietZone = -1 }).CreateLayout());
            Assert.Throws<InvalidOperationException>(() => Barcode.CreateQrCode("a", new BarcodeOptions { QuietZone = double.NaN }).CreateLayout());
        }

        [Fact]
        public void Ean13_LayoutHasQuietZonesGuardBarsAndThirteenDigits()
        {
            var layout = Barcode.CreateEan13("5901234123457").CreateLayout();
            Assert.Equal(11 + 95 + 7, layout.Width);
            Assert.Equal(13, layout.Texts.Count);
            Assert.Equal("5901234123457", string.Concat(layout.Texts.Select(t => t.Text)));
            Assert.Equal(63, layout.Bars[0].Height);
            Assert.Contains(layout.Bars, b => b.Height == 58);
        }

        [Fact]
        public void Ean13AndUpc_KeepTheirOuterDigitsInsideTheSymbol()
        {
            var layout = Barcode.CreateEan13("5901234123457", new BarcodeOptions { QuietZone = 1 }).CreateLayout();
            Assert.All(layout.Texts, t => Assert.True(t.Bounds.X >= 0));
            var noText = Barcode.CreateEan13("5901234123457", new BarcodeOptions { QuietZone = 1, ShowText = false }).CreateLayout();
            Assert.Equal(1 + 95 + 1, noText.Width);
        }

        [Fact]
        public void LinearCodes_WithoutTextHaveUniformBars()
        {
            var options = new BarcodeOptions { ShowText = false, BarHeight = 30 };
            foreach (var code in new[] { Barcode.CreateEan8("96385074", options), Barcode.CreateUpcA("036000291452", options), Barcode.CreateCode128("abc", options) })
            {
                var layout = code.CreateLayout();
                Assert.Empty(layout.Texts);
                Assert.All(layout.Bars, b => Assert.Equal(30, b.Height));
                Assert.Equal(30, layout.Height);
            }
        }

        [Fact]
        public void Code128_LayoutHasATextLineAndTenModuleMargins()
        {
            var code = Barcode.CreateCode128("PJJ123C");
            var layout = code.CreateLayout();
            Assert.Equal(code.Columns + 20, layout.Width);
            Assert.Equal("PJJ123C", Assert.Single(layout.Texts).Text);
            var custom = Barcode.CreateCode128("a", new BarcodeOptions { QuietZone = 3 }).CreateLayout();
            Assert.Equal(Barcode.CreateCode128("a").Columns + 6, custom.Width);
        }

        [Fact]
        public void Gs1128_DisplaysTheElementStrings()
        {
            var code = Barcode.CreateGs1128("(01)09501101530003(10)AB");
            Assert.Equal(BarcodeSymbology.Gs1128, code.Symbology);
            Assert.Equal("(01)09501101530003(10)AB", code.CreateLayout().Texts.Single().Text);
        }

        [Fact]
        public void Svg_WritesOnePathOfRectanglesAndTheText()
        {
            string svg = BarcodeSvg.ToSvg(Barcode.CreateEan13("5901234123457"), 2, PaintColor.Black, PaintColor.White);
            Assert.StartsWith("<svg ", svg);
            Assert.Contains("viewBox=\"0 0 113 69\"", svg);
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(svg, "<path"));
            Assert.Contains("<rect width=\"100%\"", svg);
            Assert.Equal(13, System.Text.RegularExpressions.Regex.Matches(svg, "<text ").Count);
            Assert.Contains("<title>5901234123457</title>", svg);
            var xml = System.Xml.Linq.XDocument.Parse(BarcodeSvg.ToSvg(Barcode.CreateGs1128("(01)09501101530003(10)A&B"), 1, PaintColor.FromArgb(128, 1, 2, 3)));
            Assert.NotNull(xml.Root);
            Assert.Throws<ArgumentOutOfRangeException>(() => BarcodeSvg.ToSvg(Barcode.CreateQrCode("a"), 0, PaintColor.Black));
        }

        [Fact]
        public void Svg_ReplacesCharactersXmlForbidsSoTheDocumentStillParses()
        {
            string svg = BarcodeSvg.ToSvg(Barcode.CreateQrCode("a\u0001b\u000Bc😀"), 2, PaintColor.Black);
            var title = System.Xml.Linq.XDocument.Parse(svg).Root!.Elements().Single(e => e.Name.LocalName == "title");
            Assert.Equal("a b c😀", title.Value);
            Assert.ThrowsAny<ArgumentException>(() => Barcode.CreateQrCode("a\ud800"));
        }
    }
}
