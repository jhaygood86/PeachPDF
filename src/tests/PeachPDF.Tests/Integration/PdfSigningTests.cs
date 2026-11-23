using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using PeachDrawing.PDF;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>Signing a generated document through <see cref="PdfGenerateConfig.Signing"/>; the result is read back with the writer's own signature reader.</summary>
    public class PdfSigningTests
    {
        const string Html = "<html><body><p>Hello signed world</p></body></html>";

        static X509Certificate2 SelfSigned()
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=PeachPDF Test Signer", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, true));
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
            return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.Exportable);
        }

        static PdfGenerateConfig Config(PdfSigningOptions? signing) => new()
        {
            PageSize = PageSize.A4,
            Metadata = new PdfDocumentMetadata { Title = "T", CreationDate = new DateTimeOffset(2026, 11, 23, 9, 0, 0, TimeSpan.Zero) },
            Signing = signing,
        };

        static async Task<byte[]> Generate(PdfGenerateConfig config)
        {
            var document = await new PdfGenerator().GeneratePdf(Html, config);
            using var stream = new MemoryStream();
            await document.SaveAsync(stream);
            return stream.ToArray();
        }

        [Fact]
        public async Task ASignedDocument_CarriesAnIntactSignatureOverTheWholeFile()
        {
            using var certificate = SelfSigned();
            var pdf = await Generate(Config(new PdfSigningOptions { Certificate = certificate, Reason = "Approved", Location = "Atlanta", SignerName = "Test Signer" }));

            var signature = Assert.Single(PdfPageSource.Open(pdf).GetSignatures());
            Assert.True(signature.IsIntact, signature.Problem);
            Assert.True(signature.CoversWholeFile);
            Assert.Equal("Approved", signature.Reason);
            Assert.Equal("Atlanta", signature.Location);
            Assert.Equal("Test Signer", signature.SignerName);
            Assert.Equal("adbe.pkcs7.detached", signature.SubFilter);
            Assert.Equal(certificate.Thumbprint, signature.SignerCertificate?.Thumbprint);
        }

        [Fact]
        public async Task TheCadesFormat_IsWrittenAsTheEtsiSubFilter()
        {
            using var certificate = SelfSigned();
            var pdf = await Generate(Config(new PdfSigningOptions { Certificate = certificate, Format = PdfSignatureFormat.CadesDetached }));

            var signature = Assert.Single(PdfPageSource.Open(pdf).GetSignatures());
            Assert.True(signature.IsIntact, signature.Problem);
            Assert.Equal("ETSI.CAdES.detached", signature.SubFilter);
        }

        [Fact]
        public async Task AVisibleBox_IsDrawnWithItsText()
        {
            using var certificate = SelfSigned();
            var box = new PdfSignatureBox { X = 50, Y = 700, Width = 200, Height = 60, Text = "Signed here" };
            var pdf = await Generate(Config(new PdfSigningOptions { Certificate = certificate, Box = box }));

            Assert.True(Assert.Single(PdfPageSource.Open(pdf).GetSignatures()).IsIntact);
            Assert.Contains("Signed here", Encoding.Latin1.GetString(pdf));
        }

        [Fact]
        public async Task ATimestampedDocument_IsRefusedBySyncSave()
        {
            using var certificate = SelfSigned();
            var signing = new PdfSigningOptions { Certificate = certificate, TimestampAuthority = new Uri("http://127.0.0.1:9/tsa") };
            var document = await new PdfGenerator().GeneratePdf(Html, Config(signing));

            using var stream = new MemoryStream();
            Assert.Throws<InvalidOperationException>(() => document.Save(stream));
        }

        [Fact]
        public async Task ASigningOptionWithNoCertificate_IsRefused()
        {
            await Assert.ThrowsAnyAsync<Exception>(() => Generate(Config(new PdfSigningOptions())));
        }

        [Fact]
        public async Task AnUnsignedDocument_HasNoSignatures()
        {
            Assert.Empty(PdfPageSource.Open(await Generate(Config(null))).GetSignatures());
        }

        [Fact]
        public async Task AnEncryptedSignedDocument_IsWritten()
        {
            using var certificate = SelfSigned();
            var config = Config(new PdfSigningOptions { Certificate = certificate });
            config.Encryption = new PdfEncryptionOptions { UserPassword = "open", OwnerPassword = "admin" };

            var pdf = await Generate(config);

            Assert.Contains("/Encrypt", Encoding.Latin1.GetString(pdf));
            Assert.Contains("/ByteRange", Encoding.Latin1.GetString(pdf));
        }

        [Fact]
        public async Task AdditionalCertificates_AreEmbedded()
        {
            using var certificate = SelfSigned();
            using var extra = SelfSigned();
            var options = new PdfSigningOptions { Certificate = certificate };
            options.AdditionalCertificates.Add(extra);

            var pdf = await Generate(Config(options));

            var signature = Assert.Single(PdfPageSource.Open(pdf).GetSignatures());
            Assert.True(signature.IsIntact, signature.Problem);
        }
    }
}
