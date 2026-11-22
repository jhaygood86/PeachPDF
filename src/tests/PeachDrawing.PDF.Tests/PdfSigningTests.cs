using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using PeachDrawing.Core;

namespace PeachDrawing.PDF.Tests
{
    public partial class PdfSigningTests
    {
        [GeneratedRegex(@"/ByteRange \[(\d+) (\d+) (\d+) (\d+)\]")]
        private static partial Regex ByteRangePattern();

        static X509Certificate2 NewCertificate(bool ecdsa = false)
        {
            using var key = ecdsa ? (AsymmetricAlgorithm)ECDsa.Create() : RSA.Create(2048);
            var request = key is RSA rsa
                ? new CertificateRequest("CN=Peach Signer", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
                : new CertificateRequest("CN=Peach Signer", (ECDsa)key, HashAlgorithmName.SHA256);
            using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
            return X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pfx), null);
        }

        static byte[] Save(PdfComposerOptions options, int pages = 1)
        {
            using var composer = new PdfComposer(new PdfRenderContext(), options);
            for (var i = 0; i < pages; i++)
            {
                var page = composer.AddPage(100, 100);
                page.Canvas.DrawRectangle(page.Canvas.GetSolidBrush(PaintColor.FromArgb(255, 200, 30, 30)), 0, 0, 100, 100);
            }

            using var stream = new MemoryStream();
            composer.Save(stream);
            return stream.ToArray();
        }

        static PdfComposerOptions Signed(X509Certificate2 certificate, Action<PdfSigningOptions>? more = null)
        {
            var signing = new PdfSigningOptions { Certificate = certificate };
            more?.Invoke(signing);
            return new PdfComposerOptions { Signing = signing };
        }

        /// <summary>The signed bytes the file's own /ByteRange names, and the signature in its /Contents, read back from the file text.</summary>
        static (byte[] Covered, byte[] Signature, int[] Range) Read(byte[] file)
        {
            var text = Encoding.Latin1.GetString(file);
            var match = ByteRangePattern().Match(text);
            Assert.True(match.Success, "no /ByteRange");
            var range = match.Groups.Cast<Group>().Skip(1).Select(g => int.Parse(g.Value)).ToArray();

            var covered = file[range[0]..(range[0] + range[1])].Concat(file[range[2]..(range[2] + range[3])]).ToArray();
            // The reserved room is zero padded, so the DER's own length (not a trim of zeros, which could eat real bytes) says where it ends.
            var padded = Convert.FromHexString(text[(range[1] + 1)..(range[2] - 1)]);
            Assert.Equal(0x30, padded[0]);
            var lengthBytes = padded[1] < 0x80 ? 0 : padded[1] & 0x7F;
            var length = padded[1] < 0x80 ? padded[1] : padded.Skip(2).Take(lengthBytes).Aggregate(0, (n, b) => (n << 8) | b);
            return (covered, padded[..(2 + lengthBytes + length)], range);
        }

        [Fact]
        public void ASignedFile_HasAByteRangeThatCoversEverythingButTheSignature()
        {
            using var certificate = NewCertificate();
            var file = Save(Signed(certificate));
            var (_, _, range) = Read(file);

            Assert.Equal(0, range[0]);
            Assert.Equal((byte)'<', file[range[1]]);
            Assert.Equal((byte)'>', file[range[2] - 1]);
            Assert.Equal(file.Length, range[2] + range[3]);
        }

        [Theory]
        [InlineData(PdfSignatureFormat.Pkcs7Detached, false)]
        [InlineData(PdfSignatureFormat.CadesDetached, false)]
        [InlineData(PdfSignatureFormat.Pkcs7Detached, true)]
        public void TheSignatureVerifiesOverTheCoveredBytes(PdfSignatureFormat format, bool ecdsa)
        {
            using var certificate = NewCertificate(ecdsa);
            var file = Save(Signed(certificate, s => s.Format = format));
            var (covered, signature, _) = Read(file);

            var cms = new SignedCms(new ContentInfo(covered), detached: true);
            cms.Decode(signature);
            cms.CheckSignature(verifySignatureOnly: true);

            Assert.Single(cms.SignerInfos);
            Assert.Equal(certificate.Thumbprint, cms.SignerInfos[0].Certificate!.Thumbprint);
        }

        [Fact]
        public void ChangingASignedByte_BreaksTheSignature()
        {
            using var certificate = NewCertificate();
            var file = Save(Signed(certificate));
            var (_, signature, _) = Read(file);
            file[10] ^= 0x01;
            var (tampered, _, _) = Read(file);

            var cms = new SignedCms(new ContentInfo(tampered), detached: true);
            cms.Decode(signature);
            Assert.ThrowsAny<CryptographicException>(() => cms.CheckSignature(verifySignatureOnly: true));
        }

        [Fact]
        public void TheSignatureField_IsInTheFormAndTheDictionaryNamesTheSubFilter()
        {
            using var certificate = NewCertificate();
            var file = Save(Signed(certificate, s => { s.Format = PdfSignatureFormat.CadesDetached; s.Reason = "Approved"; s.Location = "Atlanta"; s.SignerName = "Peach"; }));
            var text = Encoding.Latin1.GetString(file);

            Assert.Contains("/FT /Sig", text);
            Assert.Contains("/SigFlags 3", text);
            Assert.Contains("/SubFilter /ETSI.CAdES.detached", text);
            Assert.Contains("/Reason", text);
            Assert.Contains("/Location", text);
            Assert.True(PdfPageSource.Open(file).HasSignatures);
        }

        [Fact]
        public void ThePageStillReadsAsAPage_AndTheDigestOptionsWork()
        {
            using var certificate = NewCertificate();
            foreach (var hash in new[] { HashAlgorithmName.SHA256, HashAlgorithmName.SHA384, HashAlgorithmName.SHA512 })
            {
                var file = Save(Signed(certificate, s => { s.HashAlgorithm = hash; s.Format = PdfSignatureFormat.CadesDetached; }), pages: 2);
                var (covered, signature, _) = Read(file);
                var cms = new SignedCms(new ContentInfo(covered), detached: true);
                cms.Decode(signature);
                cms.CheckSignature(true);
                Assert.Equal(2, PdfPageSource.Open(file).PageCount);
            }
        }

        [Fact]
        public void AnEncryptedFile_CanBeSigned_AndItsSignatureIsNotEncrypted()
        {
            using var certificate = NewCertificate();
            var options = Signed(certificate);
            options.Encryption = new PdfEncryptionOptions { UserPassword = "user", OwnerPassword = "owner" };
            var file = Save(options);
            var (covered, signature, _) = Read(file);

            var cms = new SignedCms(new ContentInfo(covered), detached: true);
            cms.Decode(signature);
            cms.CheckSignature(true);
        }

        [Fact]
        public void ALinearizedFile_CanBeSigned_AndStaysTheSameLength()
        {
            using var certificate = NewCertificate();
            var options = Signed(certificate);
            options.Linearize = true;
            var file = Save(options, pages: 3);
            var (covered, signature, range) = Read(file);

            Assert.Equal(file.Length, range[2] + range[3]);
            var cms = new SignedCms(new ContentInfo(covered), detached: true);
            cms.Decode(signature);
            cms.CheckSignature(true);
            Assert.Equal(3, PdfPageSource.Open(file).PageCount);
        }

        [Fact]
        public void SavingTwice_SignsEachFile_WithOneSignatureField()
        {
            using var certificate = NewCertificate();
            using var composer = new PdfComposer(new PdfRenderContext(), Signed(certificate));
            composer.AddPage(100, 100);

            using var first = new MemoryStream();
            using var second = new MemoryStream();
            composer.Save(first);
            composer.Save(second);

            var text = Encoding.Latin1.GetString(second.ToArray());
            Assert.Single(Regex.Matches(text, "/FT /Sig"));
            var (covered, signature, _) = Read(second.ToArray());
            var cms = new SignedCms(new ContentInfo(covered), detached: true);
            cms.Decode(signature);
            cms.CheckSignature(true);
        }

        [Fact]
        public void AnUnsignedFile_HasNoSignatureField()
        {
            var text = Encoding.Latin1.GetString(Save(new PdfComposerOptions()));
            Assert.DoesNotContain("/ByteRange", text);
            Assert.DoesNotContain("/SigFlags", text);
        }

        [Fact]
        public void ASignatureThatDoesNotFit_FailsTheSaveNamingTheOption()
        {
            using var certificate = NewCertificate();
            // Distinct certificates: the same one added twice is embedded once.
            var extras = Enumerable.Range(0, 6).Select(_ => NewCertificate()).ToList();
            var options = Signed(certificate, s => { s.ReservedSignatureBytes = 2048; foreach (var extra in extras) s.AdditionalCertificates.Add(extra); });

            var error = Assert.Throws<InvalidOperationException>(() => Save(options));
            Assert.Contains("ReservedSignatureBytes", error.Message);
        }

        [Fact]
        public void InvalidSigningOptions_AreRejectedWhenApplied()
        {
            using var certificate = NewCertificate();
            Assert.Throws<InvalidOperationException>(() => new PdfComposer(new PdfRenderContext(), new PdfComposerOptions { Signing = new PdfSigningOptions() }));
            Assert.Throws<InvalidOperationException>(() => new PdfComposer(new PdfRenderContext(), Signed(certificate, s => s.HashAlgorithm = HashAlgorithmName.SHA1)));
            Assert.Throws<InvalidOperationException>(() => new PdfComposer(new PdfRenderContext(), Signed(certificate, s => s.ReservedSignatureBytes = 10)));
            Assert.Throws<InvalidOperationException>(() => new PdfComposer(new PdfRenderContext(), Signed(certificate, s => s.Format = (PdfSignatureFormat)99)));

            using var publicOnly = X509CertificateLoader.LoadCertificate(certificate.RawData);
            Assert.Throws<InvalidOperationException>(() => new PdfComposer(new PdfRenderContext(), Signed(publicOnly)));
        }
    }
}
