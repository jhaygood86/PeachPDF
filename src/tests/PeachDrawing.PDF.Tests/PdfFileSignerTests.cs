using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using PeachDrawing.Core;
using PeachDrawing.PDF.Tests.Import;

namespace PeachDrawing.PDF.Tests
{
    public partial class PdfFileSignerTests
    {
        [GeneratedRegex(@"/ByteRange \[(\d+) (\d+) (\d+) (\d+)\]")]
        private static partial Regex ByteRangePattern();

        static X509Certificate2 NewCertificate()
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=Peach Signer", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
            return X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pfx), null);
        }

        static PdfSigningOptions Options(X509Certificate2 certificate, Action<PdfSigningOptions>? more = null)
        {
            var options = new PdfSigningOptions { Certificate = certificate, Format = PdfSignatureFormat.CadesDetached };
            more?.Invoke(options);
            return options;
        }

        static byte[] Compose(PdfComposerOptions? options = null, int pages = 2)
        {
            using var composer = new PdfComposer(new PdfRenderContext(), options ?? new PdfComposerOptions());
            for (var i = 0; i < pages; i++)
            {
                var page = composer.AddPage(100, 100);
                page.Canvas.DrawRectangle(page.Canvas.GetSolidBrush(PaintColor.FromArgb(255, 30, 160, 60)), 0, 0, 100, 100);
            }

            using var stream = new MemoryStream();
            composer.Save(stream);
            return stream.ToArray();
        }

        /// <summary>Every signature in the file: what its /ByteRange covers and the DER bytes in its /Contents.</summary>
        static List<(byte[] Covered, byte[] Contents, int[] Range)> ReadAll(byte[] file)
        {
            var text = Encoding.Latin1.GetString(file);
            var all = new List<(byte[], byte[], int[])>();
            foreach (Match match in ByteRangePattern().Matches(text))
            {
                var range = match.Groups.Cast<Group>().Skip(1).Select(g => int.Parse(g.Value)).ToArray();
                var covered = file[range[0]..(range[0] + range[1])].Concat(file[range[2]..(range[2] + range[3])]).ToArray();
                var padded = Convert.FromHexString(text[(range[1] + 1)..(range[2] - 1)]);
                var lengthBytes = padded[1] < 0x80 ? 0 : padded[1] & 0x7F;
                var length = padded[1] < 0x80 ? padded[1] : padded.Skip(2).Take(lengthBytes).Aggregate(0, (n, b) => (n << 8) | b);
                all.Add((covered, padded[..(2 + lengthBytes + length)], range));
            }

            return all;
        }

        static void AssertSignatureVerifies((byte[] Covered, byte[] Contents, int[] Range) signature)
        {
            var cms = new SignedCms(new ContentInfo(signature.Covered), detached: true);
            cms.Decode(signature.Contents);
            cms.CheckSignature(verifySignatureOnly: true);
        }

        /// <summary>The names of the signature fields, in the order the file lists them (a name is a literal or hexadecimal string).</summary>
        static string Fields(byte[] file) => string.Join(",", Regex.Matches(Encoding.Latin1.GetString(file), @"/T (?:\((\w+)\)|<([0-9A-F]+)>)")
            .Select(m => m.Groups[1].Success ? m.Groups[1].Value : Encoding.Latin1.GetString(Convert.FromHexString(m.Groups[2].Value)))
            .Distinct());

        [Fact]
        public void SigningAPeachPdfFile_KeepsEveryOriginalByte_AndTheSignatureVerifies()
        {
            using var certificate = NewCertificate();
            var original = Compose();
            var signed = PdfFileSigner.Sign(original, Options(certificate));

            Assert.Equal(original, signed[..original.Length]);
            var signature = Assert.Single(ReadAll(signed));
            Assert.Equal(signed.Length, signature.Range[2] + signature.Range[3]);
            AssertSignatureVerifies(signature);

            var source = PdfPageSource.Open(signed);
            Assert.True(source.HasSignatures);
            Assert.Equal(2, source.PageCount);
            Assert.False(PdfPageSource.Open(original).HasSignatures);
        }

        [Fact]
        public void TheInputIsNotModified()
        {
            using var certificate = NewCertificate();
            var original = Compose();
            var copy = (byte[])original.Clone();

            PdfFileSigner.Sign(original, Options(certificate));

            Assert.Equal(copy, original);
        }

        [Fact]
        public void AnEarlierSignatureStaysValid_WhenAnotherIsAppended()
        {
            using var first = NewCertificate();
            using var second = NewCertificate();
            var once = Compose(new PdfComposerOptions { Signing = Options(first) });
            var twice = PdfFileSigner.Sign(once, Options(second));

            Assert.Equal(once, twice[..once.Length]);
            var signatures = ReadAll(twice);
            Assert.Equal(2, signatures.Count);
            foreach (var signature in signatures)
                AssertSignatureVerifies(signature);

            Assert.Equal("Signature1,Signature2", Fields(twice));
            Assert.Contains("/SigFlags 3", Encoding.Latin1.GetString(twice));
        }

        [Fact]
        public void SigningThreeTimes_NumbersTheFieldsApart()
        {
            using var certificate = NewCertificate();
            var file = Compose();
            for (var i = 0; i < 3; i++)
                file = PdfFileSigner.Sign(file, Options(certificate));

            var names = Fields(file).Split(',');
            Assert.Equal(["Signature1", "Signature2", "Signature3"], names);
            Assert.All(ReadAll(file), AssertSignatureVerifies);
        }

        [Fact]
        public void ALinearizedFile_CanBeSigned()
        {
            using var certificate = NewCertificate();
            var original = Compose(new PdfComposerOptions { Linearize = true }, pages: 3);
            var signed = PdfFileSigner.Sign(original, Options(certificate));

            Assert.Equal(original, signed[..original.Length]);
            AssertSignatureVerifies(Assert.Single(ReadAll(signed)));
            Assert.Equal(3, PdfPageSource.Open(signed).PageCount);
        }

        [Theory]
        [InlineData("", "")]                                                                       // no form, no annotations
        [InlineData("/AcroForm << /Fields [] >>", "")]                                             // a direct form
        [InlineData("/AcroForm 5 0 R", "")]                                                        // an indirect form
        [InlineData("/AcroForm 5 0 R", "/Annots [6 0 R]")]                                         // and a direct annotation list
        [InlineData("/AcroForm << /Fields 6 0 R /SigFlags 1 >>", "/Annots 7 0 R")]                 // fields and annotations in their own objects
        public void TheFormAndAnnotationShapesAFileCanHave_AreAllExtended(string catalog, string page)
        {
            using var certificate = NewCertificate();
            var extras = catalog switch
            {
                "/AcroForm 5 0 R" when page == "" => new[] { "<< /Fields [] >>" },
                "/AcroForm 5 0 R" => new[] { "<< /Fields [6 0 R] /SigFlags 1 >>", "<< /Type /Annot /Subtype /Widget /FT /Tx /T (name) /Rect [0 0 10 10] >>" },
                _ when catalog.Contains("6 0 R") => new[] { "<< /Type /Pages /Count 0 >>", "[ ]", "[ ]" },
                _ => [],
            };
            // Object 5 is the first extra: the form, with the widget (when there is one) as object 6.
            var file = TestPdf.Build("", pageExtras: page, catalogExtras: catalog, extraObjects: extras);
            if (catalog.Contains("6 0 R") && !catalog.Contains("5 0 R"))
                file = TestPdf.Build("", pageExtras: page, catalogExtras: catalog, extraObjects: ["<< >>", "[ ]", "[ ]"]);

            var signed = PdfFileSigner.Sign(file, Options(certificate));

            Assert.Equal(file, signed[..file.Length]);
            AssertSignatureVerifies(Assert.Single(ReadAll(signed)));
            var source = PdfPageSource.Open(signed);
            Assert.True(source.HasSignatures);
            Assert.Equal(1, source.PageCount);
            Assert.Contains("/SigFlags 3", Encoding.Latin1.GetString(signed));
        }

        /// <summary>A one-page file whose cross-reference data is a stream, like a modern producer's.</summary>
        static byte[] WithXrefStream()
        {
            var sb = new StringBuilder("%PDF-1.5\n");
            var offsets = new List<int>();
            void Obj(string body)
            {
                offsets.Add(sb.Length);
                sb.Append(offsets.Count).Append(" 0 obj\n").Append(body).Append("\nendobj\n");
            }

            Obj("<< /Type /Catalog /Pages 2 0 R >>");
            Obj("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
            Obj("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] >>");
            var xrefPosition = sb.Length;
            offsets.Add(xrefPosition);

            var rows = new MemoryStream();
            rows.Write([0, 0, 0, 0, 0, 0xFF, 0xFF]);
            foreach (var offset in offsets)
                rows.Write([1, (byte)(offset >> 24), (byte)(offset >> 16), (byte)(offset >> 8), (byte)offset, 0, 0]);
            sb.Append("4 0 obj\n<< /Type /XRef /Size 5 /W [1 4 2] /Root 1 0 R /Length ").Append(rows.Length).Append(" >>\nstream\n");
            sb.Append(Encoding.Latin1.GetString(rows.ToArray())).Append("\nendstream\nendobj\n");
            sb.Append("startxref\n").Append(xrefPosition).Append("\n%%EOF\n");
            return Encoding.Latin1.GetBytes(sb.ToString());
        }

        [Fact]
        public void AFileWithACrossReferenceStream_GetsACrossReferenceStreamUpdate()
        {
            using var certificate = NewCertificate();
            var file = WithXrefStream();
            Assert.Equal(1, PdfPageSource.Open(file).PageCount);

            var signed = PdfFileSigner.Sign(file, Options(certificate));
            var text = Encoding.Latin1.GetString(signed, file.Length, signed.Length - file.Length);

            Assert.Equal(file, signed[..file.Length]);
            Assert.Contains("/Type /XRef", text);
            Assert.DoesNotContain("\nxref\n", text);
            AssertSignatureVerifies(Assert.Single(ReadAll(signed)));
            Assert.True(PdfPageSource.Open(signed).HasSignatures);

            // And it can be extended again, now that the newest section is an update.
            var again = PdfFileSigner.Sign(signed, Options(certificate));
            Assert.All(ReadAll(again), AssertSignatureVerifies);
        }

        [Fact]
        public void AnEncryptedFile_IsRefused()
        {
            using var certificate = NewCertificate();
            var file = Compose(new PdfComposerOptions { Encryption = new PdfEncryptionOptions { OwnerPassword = "owner" } });

            Assert.Throws<NotSupportedException>(() => PdfFileSigner.Sign(file, Options(certificate)));
        }

        [Fact]
        public void AFileThatNeedsAPassword_IsRefusedAsEncrypted()
        {
            using var certificate = NewCertificate();
            var file = Compose(new PdfComposerOptions { Encryption = new PdfEncryptionOptions { UserPassword = "secret", OwnerPassword = "owner" } });

            Assert.Throws<NotSupportedException>(() => PdfFileSigner.Sign(file, Options(certificate)));
        }

        [Fact]
        public void BytesThatAreNotAPdf_AreRefused()
        {
            using var certificate = NewCertificate();
            Assert.Throws<InvalidDataException>(() => PdfFileSigner.Sign(Encoding.ASCII.GetBytes("this is not a pdf"), Options(certificate)));
        }

        [Fact]
        public void InvalidArgumentsAndOptions_AreRejected()
        {
            using var certificate = NewCertificate();
            Assert.Throws<ArgumentNullException>(() => PdfFileSigner.Sign(null!, Options(certificate)));
            Assert.Throws<ArgumentNullException>(() => PdfFileSigner.Sign([], null!));
            Assert.Throws<InvalidOperationException>(() => PdfFileSigner.Sign(Compose(), new PdfSigningOptions()));
        }

        [Fact]
        public async Task ATimestampedSignature_NeedsTheAsyncMethod_AndCarriesAToken()
        {
            using var certificate = NewCertificate();
            using var authority = new TestTimestampAuthority();
            var file = Compose();
            var options = Options(certificate, o => o.Timestamp = new PdfTimestampOptions { Exchange = authority.Exchange });

            Assert.Throws<InvalidOperationException>(() => PdfFileSigner.Sign(file, options));

            var signed = await PdfFileSigner.SignAsync(file, options, TestContext.Current.CancellationToken);
            var signature = Assert.Single(ReadAll(signed));
            var cms = new SignedCms(new ContentInfo(signature.Covered), detached: true);
            cms.Decode(signature.Contents);
            cms.CheckSignature(true);
            Assert.Single(cms.SignerInfos[0].UnsignedAttributes);
        }

        [Fact]
        public async Task ADocumentTimestamp_CoversTheWholeFile_AndLeavesTheSignatureValid()
        {
            using var certificate = NewCertificate();
            using var authority = new TestTimestampAuthority();
            var signed = PdfFileSigner.Sign(Compose(), Options(certificate));

            var stamped = await PdfFileSigner.AddDocumentTimestampAsync(signed, new PdfTimestampOptions { Exchange = authority.Exchange }, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(signed, stamped[..signed.Length]);
            var all = ReadAll(stamped);
            Assert.Equal(2, all.Count);
            AssertSignatureVerifies(all[0]);

            Assert.True(Rfc3161TimestampToken.TryDecode(all[1].Contents, out var token, out _));
            Assert.Equal(SHA256.HashData(all[1].Covered), token!.TokenInfo.GetMessageHash().ToArray());
            Assert.Equal(stamped.Length, all[1].Range[2] + all[1].Range[3]);

            var text = Encoding.Latin1.GetString(stamped);
            Assert.Contains("/Type /DocTimeStamp", text);
            Assert.Contains("/SubFilter /ETSI.RFC3161", text);
            Assert.Equal("Signature1,Timestamp1", Fields(stamped));
        }

        [Fact]
        public async Task ADocumentTimestamp_Validates_ItsArguments()
        {
            using var authority = new TestTimestampAuthority();
            var good = new PdfTimestampOptions { Exchange = authority.Exchange };

            await Assert.ThrowsAsync<ArgumentNullException>(() => PdfFileSigner.AddDocumentTimestampAsync(null!, good, cancellationToken: TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<ArgumentNullException>(() => PdfFileSigner.AddDocumentTimestampAsync([], null!, cancellationToken: TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<InvalidOperationException>(() => PdfFileSigner.AddDocumentTimestampAsync(Compose(), new PdfTimestampOptions(), cancellationToken: TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<InvalidOperationException>(() => PdfFileSigner.AddDocumentTimestampAsync(Compose(), good, 10, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task ARefusedDocumentTimestampResponse_IsReported()
        {
            var options = new PdfTimestampOptions { Exchange = (_, _) => Task.FromResult(new byte[] { 1, 2, 3 }) };
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => PdfFileSigner.AddDocumentTimestampAsync(Compose(), options, cancellationToken: TestContext.Current.CancellationToken));
            Assert.Contains("refused", error.Message);
        }
    }
}
