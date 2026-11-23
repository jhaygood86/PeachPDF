using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using PeachDrawing.Core;
using PeachDrawing.PDF.Tests.Import;

namespace PeachDrawing.PDF.Tests
{
    public class PdfVisibleSignatureTests
    {
        static X509Certificate2 NewCertificate()
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=Visible Signer", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
            return X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pfx), null);
        }

        static PdfSigningOptions Visible(X509Certificate2 certificate, Action<PdfSigningOptions>? more = null, int page = 1)
        {
            var options = new PdfSigningOptions
            {
                Certificate = certificate,
                Reason = "Approved",
                Box = new PdfSignatureBox { PageNumber = page, Bounds = new Rect(20, 30, 100, 40) },
            };
            more?.Invoke(options);
            return options;
        }

        static byte[] Compose(PdfComposerOptions options, int pages = 2)
        {
            using var composer = new PdfComposer(new PdfRenderContext(), options);
            for (var i = 0; i < pages; i++)
                composer.AddPage(200, 100);
            using var stream = new MemoryStream();
            composer.Save(stream);
            return stream.ToArray();
        }

        /// <summary>The red channel of the pixel at (x, y) of a page painted as a viewer would paint it.</summary>
        static int Pixel(byte[] file, int page, int x, int y)
        {
            var source = PdfPageSource.Open(file);
            using var canvas = new RasterRenderContext().CreateCanvas(200, 100);
            canvas.DrawRectangle(canvas.GetSolidBrush(PaintColor.FromArgb(255, 255, 255, 255)), 0, 0, 200, 100);
            source.Paint(page, canvas, PdfAnnotationPaint.All);
            return canvas.ToPixelBuffer().PremultipliedRgba.Span[(y * 200 + x) * 4];
        }

        static void AssertBorderOnPage(byte[] file, int page, bool expected)
        {
            // The box is 100 x 40 at (20, 30): its top border is the row y = 30, its left border the column x = 20.
            var dark = Pixel(file, page, 70, 30) < 200 || Pixel(file, page, 70, 31) < 200;
            Assert.Equal(expected, dark);
        }

        [Fact]
        public void AVisibleSignatureOnTheFirstPage_IsDrawnThereAndVerifies()
        {
            using var certificate = NewCertificate();
            var file = Compose(new PdfComposerOptions { Signing = Visible(certificate) });
            var text = Encoding.Latin1.GetString(file);

            // 200 x 100 page, box 100 x 40 at (20, 30) from the top: in PDF space the rectangle is [20 30 120 70].
            Assert.Contains("/Rect [20 30 120 70]", text);
            Assert.Contains("(Digitally signed by Visible Signer) Tj", text);
            Assert.Contains("(Reason: Approved) Tj", text);
            AssertBorderOnPage(file, 0, expected: true);
            AssertBorderOnPage(file, 1, expected: false);

            var signature = Assert.Single(PdfPageSource.Open(file).GetSignatures());
            Assert.True(signature.IsIntact, signature.Problem);
        }

        [Fact]
        public void ASignatureCanBePutOnAnotherPage_WithItsOwnText()
        {
            using var certificate = NewCertificate();
            var file = Compose(new PdfComposerOptions
            {
                Signing = Visible(certificate, o => { o.Box!.Text = "Approved (final)\nSecond line"; o.Box.FontSize = 12; }, page: 2),
            });
            var text = Encoding.Latin1.GetString(file);

            AssertBorderOnPage(file, 0, expected: false);
            AssertBorderOnPage(file, 1, expected: true);
            Assert.Contains("(Approved \\(final\\)) Tj", text);       // the parentheses are escaped
            Assert.Contains("(Second line) Tj", text);
            Assert.Contains("/Helv 12 Tf", text);
            Assert.True(Assert.Single(PdfPageSource.Open(file).GetSignatures()).IsIntact);
        }

        [Fact]
        public void APageThatDoesNotExist_FailsTheSave_NamingTheOption()
        {
            using var certificate = NewCertificate();

            var error = Assert.Throws<InvalidOperationException>(() => Compose(new PdfComposerOptions { Signing = Visible(certificate, page: 3) }));

            Assert.Contains("PageNumber", error.Message);
        }

        [Fact]
        public void ATextWithOddCharacters_IsEscapedIntoPlainAscii()
        {
            using var certificate = NewCertificate();
            var file = Compose(new PdfComposerOptions { Signing = Visible(certificate, o => o.Box!.Text = "Caf\u00e9 \\ \u4e2d") });
            var text = Encoding.Latin1.GetString(file);

            Assert.Contains("(Caf\\351 \\\\ ?) Tj", text);        // e-acute as an octal escape, a backslash doubled, a CJK character as ?
            Assert.True(Assert.Single(PdfPageSource.Open(file).GetSignatures()).IsIntact);
        }

        [Fact]
        public async Task AVisibleSignatureCanBeAppendedToAnExistingFile_AndEarlierBytesAreKept()
        {
            using var certificate = NewCertificate();
            var original = Compose(new PdfComposerOptions());

            var signed = await PdfFileSigner.SignAsync(original, Visible(certificate, page: 2), cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(original, signed[..original.Length]);
            Assert.Contains("/Rect [20 30 120 70]", Encoding.Latin1.GetString(signed));
            AssertBorderOnPage(signed, 0, expected: false);
            AssertBorderOnPage(signed, 1, expected: true);
            Assert.True(Assert.Single(PdfPageSource.Open(signed).GetSignatures()).IsIntact);
        }

        [Fact]
        public void TheBoxFollowsAPagesMediaBoxOffset()
        {
            using var certificate = NewCertificate();
            // A page whose MediaBox starts at (5, 5) (the same length as [0 0 90 90], so the file's offsets stay right): the box is placed in
            // the page as shown, then moved into the page's own space: x + 5, and y counted up from the box's top edge at 95.
            var file = TestPdf.Build("", width: 90, height: 90);
            var shifted = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(file).Replace("/MediaBox [0 0 90 90]", "/MediaBox [5 5 95 95]", StringComparison.Ordinal));
            Assert.NotEqual(file, shifted);

            var signed = PdfFileSigner.Sign(shifted, Visible(certificate));

            Assert.Contains("/Rect [25 25 125 65]", Encoding.Latin1.GetString(signed, shifted.Length, signed.Length - shifted.Length));
        }

        [Fact]
        public void ARotatedPage_IsRefused_ForTheIncrementalPathToo()
        {
            using var certificate = NewCertificate();
            var file = TestPdf.Build("", pageExtras: "/Rotate 90");

            Assert.Throws<NotSupportedException>(() => PdfFileSigner.Sign(file, Visible(certificate)));
        }

        [Fact]
        public void APageBeyondTheEnd_IsRefused_ForTheIncrementalPathToo()
        {
            using var certificate = NewCertificate();

            var error = Assert.Throws<InvalidOperationException>(() => PdfFileSigner.Sign(Compose(new PdfComposerOptions()), Visible(certificate, page: 9)));

            Assert.Contains("PageNumber", error.Message);
        }

        [Theory]
        [InlineData(PdfAConformance.PdfA2B)]
        [InlineData(PdfAConformance.PdfA4)]
        public void AVisibleSignature_IsRefusedForAnArchivalFile_ButAnInvisibleOneIsAllowed(PdfAConformance level)
        {
            using var certificate = NewCertificate();
            var archival = new PdfComposerOptions { Conformance = level, Title = "T", CreationDate = new DateTimeOffset(2026, 11, 23, 9, 0, 0, TimeSpan.Zero) };

            archival.Signing = Visible(certificate);
            Assert.Throws<InvalidOperationException>(() => new PdfComposer(new PdfRenderContext(), archival));

            archival.Signing = new PdfSigningOptions { Certificate = certificate };
            var original = Compose(archival);
            Assert.True(Assert.Single(PdfPageSource.Open(original).GetSignatures()).IsIntact);

            // The same rule when signing a PDF/A file that already exists.
            var existing = Compose(new PdfComposerOptions { Conformance = level, Title = "T", CreationDate = archival.CreationDate });
            Assert.Throws<InvalidOperationException>(() => PdfFileSigner.Sign(existing, Visible(certificate)));
        }

        [Theory]
        [InlineData(0, 10, 10, 9.0)]        // page number
        [InlineData(1, 0, 10, 9.0)]         // no width
        [InlineData(1, 10, -4, 9.0)]        // negative height
        [InlineData(1, 10, 10, 2.0)]        // text too small
        [InlineData(1, 10, 10, 99.0)]       // text too large
        [InlineData(1, double.NaN, 10, 9.0)]
        public void AnInvalidAppearance_IsRejectedWhenApplied(int page, double width, double height, double size)
        {
            using var certificate = NewCertificate();
            var options = Visible(certificate, o => { o.Box!.PageNumber = page; o.Box.Bounds = new Rect(0, 0, width, height); o.Box.FontSize = size; });

            Assert.Throws<InvalidOperationException>(() => new PdfComposer(new PdfRenderContext(), new PdfComposerOptions { Signing = options }));
        }
    }
}
