using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using PeachDrawing.Core;

namespace PeachDrawing.PDF.Tests
{
    /// <summary>Signing an encrypted file in place: the objects written are encrypted with the file's own key, under each object's number.</summary>
    public class PdfEncryptedSigningTests
    {
        static X509Certificate2 NewCertificate()
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=Encrypted Signer", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
            return X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pfx), null);
        }

        static PdfSigningOptions Options(X509Certificate2 certificate, Action<PdfSigningOptions>? more = null)
        {
            var options = new PdfSigningOptions { Certificate = certificate, Reason = "Sealed shut", Location = "Atlanta", SignerName = "Encrypted Signer" };
            more?.Invoke(options);
            return options;
        }

        static byte[] Encrypted(string? userPassword = null, bool linearize = false, int pages = 2)
        {
            var options = new PdfComposerOptions
            {
                Linearize = linearize,
                Encryption = new PdfEncryptionOptions { UserPassword = userPassword ?? "", OwnerPassword = "owner" },
            };
            using var composer = new PdfComposer(new PdfRenderContext(), options);
            for (var i = 0; i < pages; i++)
            {
                var page = composer.AddPage(200, 100);
                page.Canvas.DrawRectangle(page.Canvas.GetSolidBrush(PaintColor.FromArgb(255, 255, 250, 235)), 0, 0, 200, 100);
            }

            using var stream = new MemoryStream();
            composer.Save(stream);
            return stream.ToArray();
        }

        [Fact]
        public void AFileThatOpensWithoutAPassword_IsSigned_AndStaysEncrypted()
        {
            using var certificate = NewCertificate();
            var original = Encrypted();

            var signed = PdfFileSigner.Sign(original, Options(certificate));

            Assert.Equal(original, signed[..original.Length]);
            // The update's trailer names the encryption dictionary, as every trailer of an encrypted file must.
            Assert.Contains("/Encrypt", Encoding.Latin1.GetString(signed, original.Length, signed.Length - original.Length));

            var signature = Assert.Single(PdfPageSource.Open(signed).GetSignatures());
            Assert.True(signature.IsIntact, signature.Problem);
            Assert.Equal("Signature1", signature.FieldName);                 // the field's /T, stored encrypted and read back decrypted
            Assert.Equal("Sealed shut", signature.Reason);                  // and the strings of the signature value
            Assert.Equal("Atlanta", signature.Location);
            Assert.Equal("Encrypted Signer", signature.SignerName);
            Assert.Equal(2, PdfPageSource.Open(signed).PageCount);
        }

        [Fact]
        public void TheStringsOfTheSignatureAreNotStoredInTheClear()
        {
            using var certificate = NewCertificate();
            var original = Encrypted();

            var signed = PdfFileSigner.Sign(original, Options(certificate));

            // A text string is hexadecimal of its UTF-16 text with a byte order mark; written unencrypted it would be visible in the update.
            var update = Encoding.Latin1.GetString(signed, original.Length, signed.Length - original.Length);
            Assert.DoesNotContain("FEFF" + Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes("Sealed shut")), update);
            Assert.DoesNotContain("FEFF" + Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes("Atlanta")), update);
            Assert.Contains("/Reason <", update);        // the entry is there, holding ciphertext
        }

        [Fact]
        public void AFileThatNeedsAPassword_IsSignedWithIt_AndRefusedWithoutOrWithAWrongOne()
        {
            using var certificate = NewCertificate();
            var original = Encrypted("secret");

            Assert.Throws<PdfPasswordException>(() => PdfFileSigner.Sign(original, Options(certificate)));
            Assert.Throws<PdfPasswordException>(() => PdfFileSigner.Sign(original, Options(certificate), "not it"));

            var signed = PdfFileSigner.Sign(original, Options(certificate), "secret");

            Assert.Throws<PdfPasswordException>(() => PdfPageSource.Open(signed));
            var signature = Assert.Single(PdfPageSource.Open(signed, "secret").GetSignatures());
            Assert.True(signature.IsIntact, signature.Problem);
            Assert.Equal("Sealed shut", signature.Reason);
        }

        [Fact]
        public void TheOwnerPasswordOpensAFileToo()
        {
            using var certificate = NewCertificate();

            var signed = PdfFileSigner.Sign(Encrypted("secret"), Options(certificate), "owner");

            Assert.True(Assert.Single(PdfPageSource.Open(signed, "owner").GetSignatures()).IsIntact);
        }

        [Fact]
        public void AVisibleSignature_StoresItsAppearanceStreamEncrypted_AndItStillDraws()
        {
            using var certificate = NewCertificate();
            var options = Options(certificate, o => o.Box = new PdfSignatureBox { Bounds = new Rect(20, 30, 100, 40) });

            var signed = PdfFileSigner.Sign(Encrypted("secret"), options, "secret");

            Assert.DoesNotContain("Digitally signed by", Encoding.Latin1.GetString(signed));      // the content stream is not stored in the clear
            var source = PdfPageSource.Open(signed, "secret");
            using var canvas = new RasterRenderContext().CreateCanvas(200, 100);
            canvas.DrawRectangle(canvas.GetSolidBrush(PaintColor.FromArgb(255, 255, 255, 255)), 0, 0, 200, 100);
            source.Paint(0, canvas, PdfAnnotationPaint.All);
            var pixels = canvas.ToPixelBuffer().PremultipliedRgba.Span;
            Assert.True(pixels[(30 * 200 + 70) * 4] < 200 || pixels[(31 * 200 + 70) * 4] < 200);   // the box's top border
        }

        [Fact]
        public void AnEncryptedFileCanBeSignedAgain_AndTheFirstSignatureStaysIntact()
        {
            using var first = NewCertificate();
            using var second = NewCertificate();

            var once = PdfFileSigner.Sign(Encrypted(), Options(first));
            var twice = PdfFileSigner.Sign(once, Options(second));

            var signatures = PdfPageSource.Open(twice).GetSignatures();
            Assert.Equal(["Signature1", "Signature2"], signatures.Select(s => s.FieldName));
            Assert.All(signatures, s => Assert.True(s.IsIntact, s.Problem));
        }

        [Fact]
        public async Task ADocumentTimestampCanBeAddedToAnEncryptedFile()
        {
            using var certificate = NewCertificate();
            using var authority = new TestTimestampAuthority();
            var signed = PdfFileSigner.Sign(Encrypted("secret"), Options(certificate), "secret");

            var stamped = await PdfFileSigner.AddDocumentTimestampAsync(signed, new PdfTimestampOptions { Exchange = authority.Exchange }, password: "secret", cancellationToken: TestContext.Current.CancellationToken);

            var signatures = PdfPageSource.Open(stamped, "secret").GetSignatures();
            Assert.Equal(["Signature1", "Timestamp1"], signatures.Select(s => s.FieldName));
            Assert.All(signatures, s => Assert.True(s.IsIntact, s.Problem));
        }

        [Fact]
        public void ALinearizedEncryptedFile_CanBeSigned()
        {
            using var certificate = NewCertificate();
            var original = Encrypted(linearize: true, pages: 3);

            var signed = PdfFileSigner.Sign(original, Options(certificate));

            Assert.Equal(original, signed[..original.Length]);
            Assert.True(Assert.Single(PdfPageSource.Open(signed).GetSignatures()).IsIntact);
            Assert.Equal(3, PdfPageSource.Open(signed).PageCount);
        }

        [Fact]
        public void AnUnencryptedFile_IgnoresAPassword()
        {
            using var certificate = NewCertificate();
            using var composer = new PdfComposer(new PdfRenderContext());
            composer.AddPage(100, 100);
            using var stream = new MemoryStream();
            composer.Save(stream);

            var signed = PdfFileSigner.Sign(stream.ToArray(), Options(certificate), "unused");

            Assert.True(Assert.Single(PdfPageSource.Open(signed).GetSignatures()).IsIntact);
        }
    }
}
