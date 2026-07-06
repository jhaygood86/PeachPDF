using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Drawing.Layout;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Utils;

namespace PeachPDF.Tests.PdfSharpCoreTests.Basics
{
    public class SmokeTests
    {
        [Fact]
        public void Create_Hello_World_PDF()
        {
            // Create a new PDF document.
            var document = new PdfDocument();
            document.Info.Title = "Created with PDFsharp";

            // Create an empty page in this document.
            var page = document.AddPage();

            // Get an XGraphics object for drawing on this page.
            var gfx = XGraphics.FromPdfPage(page);

            // Draw two lines with a red default pen.
            var width = page.Width.Point;
            var height = page.Height.Point;
            gfx.DrawLine(XPens.Red, 0, 0, width, height);
            gfx.DrawLine(XPens.Red, width, 0, 0, height);

            // Draw a circle with a red pen which is 1.5 point thick.
            var r = width / 5;
            gfx.DrawEllipse(new XPen(XColors.Red, 1.5), XBrushes.White,
                new XRect(width / 2 - r, height / 2 - r, 2 * r, 2 * r));

            // Create a font.
            var font = new XFont("Times New Roman", 20, XFontStyle.BoldItalic, new FontResolver());

            // Draw the text.
            gfx.DrawString("Hello, World!", font, XBrushes.Black,
                new XRect(0, 0, width, height), XStringFormats.Center);

            // Save the document.
            using var stream = new MemoryStream();
            document.Save(stream);

            Assert.True(stream.Length > 0);
        }

        [Fact]
        public void Check_PDF_Options()
        {
            const string text = """
                                Facin exeraessisit la consenim iureet dignibh eu facilluptat vercil dunt autpat.
                                Ecte magna faccum dolor sequisc iliquat, quat, quipiss equipit accummy niate magna
                                facil iure eraesequis am velit, quat atis dolore dolent luptat nulla adio odipissectet
                                lan venis do essequatio conulla facillandrem zzriusci bla ad minim inis nim velit eugait
                                aut aut lor at ilit ut nulla ate te eugait alit augiamet ad magnim iurem il eu feuissi.

                                Guer sequis duis eu feugait luptat lum adiamet, si tate dolore mod eu facidunt adignisl in
                                henim dolorem nulla faccum vel inis dolutpatum iusto od min ex euis adio exer sed del
                                dolor ing enit veniamcon vullutat praestrud molenis ciduisim doloborem ipit nulla consequisi.

                                Nos adit pratetu eriurem delestie del ut lumsandreet nis exerilisit wis nos alit venit praestrud
                                dolor sum volore facidui blaor erillaortis ad ea augue corem dunt nis  iustinciduis euisi.
                                Ut ulputate volore min ut nulpute dolobor sequism olorperilit autatie modit wisl illuptat dolore
                                min ut in ute doloboreet ip ex et am dunt at.
                                """;

            int[] results = new int[3];

            for (int run = 0; run < 3; ++run)
            {
                // Create a new PDF document.
                var document = new PdfDocument();
                document.Info.Title = "Created with PDFsharp";

                switch (run)
                {
                    case 0:
                        document.Options.CompressContentStreams = false;
                        break;
                    case 1:
                        document.Options.CompressContentStreams = true;
                        break;
                    case 2:
                        document.Options.CompressContentStreams = true;
                        document.Options.FlateEncodeMode = PdfFlateEncodeMode.BestCompression;
                        break;

                    default:
                        throw new NotSupportedException("Not yet implemented.");
                }

                // Create an empty page in this document.
                var page = document.AddPage();

                // Get an XGraphics object for drawing on this page.
                var gfx = XGraphics.FromPdfPage(page);

                var font = new XFont("Times New Roman", 10, XFontStyle.Bold, new FontResolver());
                var tf = new XTextFormatter(gfx);

                var rect = new XRect(40, 100, 250, 232);
                gfx.DrawRectangle(XBrushes.SeaShell, rect);
                tf.DrawString(text, font, XBrushes.Black, rect, XStringFormats.TopLeft);

                rect = new XRect(310, 100, 250, 232);
                gfx.DrawRectangle(XBrushes.SeaShell, rect);
                tf.Alignment = XParagraphAlignment.Right;
                tf.DrawString(text, font, XBrushes.Black, rect, XStringFormats.TopLeft);

                rect = new XRect(40, 400, 250, 232);
                gfx.DrawRectangle(XBrushes.SeaShell, rect);
                tf.Alignment = XParagraphAlignment.Center;
                tf.DrawString(text, font, XBrushes.Black, rect, XStringFormats.TopLeft);

                rect = new XRect(310, 400, 250, 232);
                gfx.DrawRectangle(XBrushes.SeaShell, rect);
                tf.Alignment = XParagraphAlignment.Justify;
                tf.DrawString(text, font, XBrushes.Black, rect, XStringFormats.TopLeft);

                using (var stream = new MemoryStream())
                {
                    document.Save(stream);
                    results[run] = (int)stream.Length;
                }
            }

            Assert.True(results[0] > results[1], "File with compressed content streams must be smaller.");
            Assert.True(results[0] > results[2], "File with compressed content streams must be smaller.");
            Assert.True(results[1] >= results[2], "File with best compression must not be larger than file with standard compression.");
        }
    }
}
