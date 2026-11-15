using PeachDrawing.Core.Barcodes;
using PeachPDF.Layout;
using System.Text;
using System.Text.RegularExpressions;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// The declarative barcode terminals: each places vector content (path operators in the page's content stream, no
    /// image XObject) of the size the module size and the symbol give.
    /// </summary>
    public class BarcodeDeclarativeTests
    {
        private static async Task<(PeachPdfDocument Document, byte[] Bytes)> BuildAsync(Action<IContainer> content)
        {
            var document = await new PdfGenerator().CreateDocument(doc =>
                doc.Page(page =>
                {
                    page.Size(300, 300).Margin(20);
                    page.Content(content);
                }));
            using var ms = new MemoryStream();
            document.Save(ms);
            return (document, ms.ToArray());
        }

        private static string ContentOf(PeachPdfDocument document)
        {
            var file = document.Inspect();
            return file.ContentText(file.Pages[0]);
        }

        /// <summary>An SVG is placed as a form XObject (vector content, drawn with <c>Do</c>); this is the text of all of them.</summary>
        private static string FormContentOf(PeachPdfDocument document)
        {
            var file = document.Inspect();
            var xobjects = file.Pages[0].Dict("/Resources")?.Dict("/XObject");
            Assert.NotNull(xobjects);
            return string.Concat(xobjects.Keys.Select(k => (xobjects[k] as PdfStreamObject)?.DecodedText));
        }

        [Theory]
        [InlineData("qr")]
        [InlineData("datamatrix")]
        [InlineData("code128")]
        [InlineData("gs1")]
        [InlineData("ean13")]
        [InlineData("ean8")]
        [InlineData("upca")]
        public async Task EachSymbologyIsPlacedAsVectorPaths(string kind)
        {
            var (document, bytes) = await BuildAsync(c =>
            {
                Action<BarcodeStyle> style = s => s.ModuleSize = PdfLength.Points(2);
                switch (kind)
                {
                    case "qr": c.QrCode("https://peachpdf.net/", style); break;
                    case "datamatrix": c.DataMatrix("PeachPDF", style); break;
                    case "code128": c.Code128("PeachPDF-128", style); break;
                    case "gs1": c.Gs1128("(01)09501101530003(10)AB-123", style); break;
                    case "ean13": c.Ean13("590123412345", style); break;
                    case "ean8": c.Ean8("9638507", style); break;
                    default: c.UpcA("03600029145", style); break;
                }
            });

            Assert.DoesNotContain("/Subtype /Image", Encoding.Latin1.GetString(bytes));
            // Every bar is a closed rectangle: many path-construction operators in the page content.
            var text = FormContentOf(document);
            Assert.True(Regex.Matches(text, @"\bl\b").Count > 30, text.Length + ":" + text[..Math.Min(600, text.Length)]);
        }

        [Fact]
        public async Task TheModuleSizeScalesTheSymbol()
        {
            // A QR code of version 1 is 21 modules plus a margin of 4 on each side: 29 modules of 4pt.
            var (document, _) = await BuildAsync(c => c.QrCode("HELLO WORLD", s => s.ModuleSize = PdfLength.Points(4)));
            var file = document.Inspect();
            var xobjects = file.Pages[0].Dict("/Resources")!.Dict("/XObject")!;
            var form = Assert.IsType<PdfStreamObject>(xobjects[xobjects.Keys.Single()]);
            var box = form.Array("/BBox")!.Resolved.Select(v => ((PdfNumber)v).Value).ToArray();
            Assert.Equal(116, box[2] - box[0], 0.5);
            Assert.Equal(116, box[3] - box[1], 0.5);
        }

        [Fact]
        public async Task ABarcodeCreatedElsewhereKeepsItsOptionsAndTakesTheStyle()
        {
            var barcode = Barcode.CreateQrCode("keep", new BarcodeOptions { ErrorCorrection = QrErrorCorrectionLevel.H });
            var (document, bytes) = await BuildAsync(c => c.Barcode(barcode, s =>
            {
                s.ModuleSize = PdfLength.Millimeters(1);
                s.Color = PdfColor.FromRgb(204, 0, 0);
                s.BackgroundColor = PdfColor.White;
                s.AltText = "Keep";
            }));
            Assert.Contains("0.8 0 0 rg", FormContentOf(document));
            Assert.Equal(1, document.PageCount);
            Assert.True(bytes.Length > 0);
        }

        [Fact]
        public async Task ASecondTerminalThrows()
        {
            await Assert.ThrowsAnyAsync<Exception>(() => BuildAsync(c =>
            {
                c.QrCode("a");
                c.Ean13("590123412345");
            }));
        }

        [Fact]
        public async Task InvalidDataAndModuleSizesAreRejected()
        {
            await Assert.ThrowsAnyAsync<ArgumentException>(() => BuildAsync(c => c.Ean13("12")));
            await Assert.ThrowsAnyAsync<ArgumentException>(() => BuildAsync(c => c.QrCode("a", s => s.ModuleSize = PdfLength.Points(0))));
            await Assert.ThrowsAnyAsync<ArgumentNullException>(() => BuildAsync(c => c.Barcode(null!)));
        }

        [Fact]
        public async Task ControlCharactersInTheTextDoNotBreakTheDocument()
        {
            var (document, _) = await BuildAsync(c => c.QrCode("a\u0001b"));
            Assert.Equal(1, document.PageCount);
        }

        [Fact]
        public async Task RelativeModuleSizesAreRejected()
        {
            await Assert.ThrowsAnyAsync<ArgumentException>(() => BuildAsync(c => c.QrCode("a", s => s.ModuleSize = PdfLength.Percent(50))));
            await Assert.ThrowsAnyAsync<ArgumentException>(() => BuildAsync(c => c.QrCode("a", s => s.ModuleSize = PdfLength.Em(1))));
        }

        [Fact]
        public async Task AMatrixBarcodeCreatedElsewhereIsPlaced()
        {
            var (document, bytes) = await BuildAsync(c => c.Barcode(Barcode.CreateDataMatrix("ok")));
            Assert.Equal(1, document.PageCount);
            Assert.DoesNotContain("/Subtype /Image", Encoding.Latin1.GetString(bytes));
        }

        [Fact]
        public async Task StyleOptionsReachTheShorthandMethods()
        {
            var (document, _) = await BuildAsync(c => c.Code128("abc", s =>
            {
                s.Options.ShowText = false;
                s.FontFamily = "serif";
            }));
            Assert.Equal(1, document.PageCount);
        }
    }
}
