using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using PeachDrawing.PDF.Tests.Import;

namespace PeachDrawing.PDF.Tests
{
    /// <summary>What a signature says about the document after it: later signatures are harmless, a changed page is not.</summary>
    public partial class PdfSignatureChangesTests
    {
        static X509Certificate2 NewCertificate()
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=Change Signer", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
            return X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pfx), null);
        }

        static PdfSigningOptions Options(X509Certificate2 certificate) => new() { Certificate = certificate };

        static PdfSignatureChanges FirstChanges(byte[] file, string? password = null) => PdfPageSource.Open(file, password).GetSignatures()[0].ChangesAfter;

        /// <summary>A one-page file with a text field (object 6) whose widget is on the page.</summary>
        static byte[] WithTextField() => TestPdf.Build("", pageExtras: "/Annots [6 0 R]", catalogExtras: "/AcroForm 5 0 R",
            extraObjects: ["<< /Fields [6 0 R] >>", "<< /Type /Annot /Subtype /Widget /FT /Tx /T (name) /V (before) /Rect [10 10 90 30] /P 3 0 R >>"]);

        static byte[] Signed(byte[]? file = null)
        {
            using var certificate = NewCertificate();
            return PdfFileSigner.Sign(file ?? TestPdf.Build(""), Options(certificate));
        }

        [GeneratedRegex(@"/Root (\d+) (\d+) R")]
        private static partial Regex RootPattern();

        [GeneratedRegex(@"/Size (\d+)")]
        private static partial Regex SizePattern();

        /// <summary>
        /// Appends a revision that defines the given objects (replacing the ones with those numbers), as another tool editing a signed file
        /// would: the objects, a classic cross-reference section for them and a trailer that points back at the last one.
        /// </summary>
        static byte[] AppendRevision(byte[] file, params (int Number, string Body)[] objects)
        {
            var text = Encoding.Latin1.GetString(file);
            var startxref = text.LastIndexOf("startxref", StringComparison.Ordinal);
            var previous = long.Parse(text[(startxref + 9)..].Trim().Split('\n')[0].Trim());
            var root = RootPattern().Matches(text)[^1];
            var size = int.Parse(SizePattern().Matches(text)[^1].Groups[1].Value);

            var sb = new StringBuilder(text);
            if (!text.EndsWith('\n')) sb.Append('\n');
            var offsets = new SortedDictionary<int, int>();
            foreach (var (number, body) in objects)
            {
                offsets[number] = sb.Length;
                sb.Append(number).Append(" 0 obj\n").Append(body).Append("\nendobj\n");
            }

            var xref = sb.Length;
            sb.Append("xref\n");
            foreach (var (number, offset) in offsets)
                sb.Append(number).Append(" 1\n").Append(offset.ToString("D10")).Append(" 00000 n \n");
            sb.Append("trailer\n<< /Size ").Append(Math.Max(size, offsets.Keys.Max() + 1)).Append(" /Root ").Append(root.Groups[1].Value).Append(' ').Append(root.Groups[2].Value)
                .Append(" R /Prev ").Append(previous).Append(" >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
            return Encoding.Latin1.GetBytes(sb.ToString());
        }

        /// <summary>The text of the last definition of object <paramref name="number"/> in the file.</summary>
        static string LastBody(byte[] file, int number)
        {
            var match = Regex.Matches(Encoding.Latin1.GetString(file), $@"(?<=\n){number} 0 obj\n(.*?)\nendobj", RegexOptions.Singleline)[^1];
            return match.Groups[1].Value;
        }

        [Fact]
        public void ASignatureThatCoversTheWholeFile_HasNoChangesAfterIt()
        {
            Assert.Equal(PdfSignatureChanges.None, FirstChanges(Signed()));
        }

        [Fact]
        public void ALaterSignature_IsNotAChangeToWhatTheFirstSigned()
        {
            using var second = NewCertificate();
            var twice = PdfFileSigner.Sign(Signed(), Options(second));

            var signatures = PdfPageSource.Open(twice).GetSignatures();

            Assert.Equal(PdfSignatureChanges.SignaturesAndTimestamps, signatures[0].ChangesAfter);
            Assert.Equal(PdfSignatureChanges.None, signatures[1].ChangesAfter);
        }

        [Fact]
        public async Task ASignatureAddedToAFileThatAlreadyHasAForm_IsNotAChangeEither()
        {
            // The composer writes its own form (with an indirect list of fields); appending to it grows that list rather than adding a form.
            using var first = NewCertificate();
            using var second = NewCertificate();
            using var authority = new TestTimestampAuthority();
            using var composer = new PdfComposer(new PdfRenderContext(), new PdfComposerOptions { Signing = Options(first) });
            composer.AddPage(100, 100);
            using var stream = new MemoryStream();
            composer.Save(stream);

            var twice = PdfFileSigner.Sign(stream.ToArray(), Options(second));
            var stamped = await PdfFileSigner.AddDocumentTimestampAsync(twice, new PdfTimestampOptions { Exchange = authority.Exchange }, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal([PdfSignatureChanges.SignaturesAndTimestamps, PdfSignatureChanges.SignaturesAndTimestamps, PdfSignatureChanges.None],
                PdfPageSource.Open(stamped).GetSignatures().Select(s => s.ChangesAfter));
        }

        [Fact]
        public async Task ADocumentTimestamp_IsNotAChange_AndSignaturesCanPileUp()
        {
            using var second = NewCertificate();
            using var authority = new TestTimestampAuthority();
            var file = PdfFileSigner.Sign(Signed(), Options(second));
            file = await PdfFileSigner.AddDocumentTimestampAsync(file, new PdfTimestampOptions { Exchange = authority.Exchange }, cancellationToken: TestContext.Current.CancellationToken);

            var signatures = PdfPageSource.Open(file).GetSignatures();

            Assert.Equal([PdfSignatureChanges.SignaturesAndTimestamps, PdfSignatureChanges.SignaturesAndTimestamps, PdfSignatureChanges.None], signatures.Select(s => s.ChangesAfter));
        }

        [Fact]
        public void BytesThatAreNotARevision_ChangeNothing()
        {
            var file = Signed().Concat(Encoding.ASCII.GetBytes("\n% a comment added later\n")).ToArray();

            var signature = Assert.Single(PdfPageSource.Open(file).GetSignatures());

            Assert.False(signature.CoversWholeFile);
            Assert.Equal(PdfSignatureChanges.None, signature.ChangesAfter);
        }

        [Fact]
        public void ARevisionThatChangesNothing_ChangesNothing()
        {
            var signed = Signed();

            Assert.Equal(PdfSignatureChanges.None, FirstChanges(AppendRevision(signed, (99, "<< /Unused true >>"))));       // a new, unreferenced object
        }

        [Fact]
        public void ReplacingThePageContent_IsAnOtherChange()
        {
            var signed = Signed();

            var edited = AppendRevision(signed, (4, "<< /Length 4 >>\nstream\nq Q\nendstream"));

            Assert.Equal(PdfSignatureChanges.Other, FirstChanges(edited));
        }

        [Fact]
        public void ChangingThePage_IsAnOtherChange()
        {
            var signed = Signed();
            var page = LastBody(signed, 3).Replace("/MediaBox [0 0 100 100]", "/MediaBox [0 0 100 200]", StringComparison.Ordinal);

            Assert.Equal(PdfSignatureChanges.Other, FirstChanges(AppendRevision(signed, (3, page))));
        }

        [Fact]
        public void FillingAFormField_IsFormFilling()
        {
            var signed = Signed(WithTextField());

            var filled = AppendRevision(signed, (6, "<< /Type /Annot /Subtype /Widget /FT /Tx /T (name) /V (after) /Rect [10 10 90 30] /P 3 0 R >>"));

            Assert.Equal(PdfSignatureChanges.FormFilling, FirstChanges(filled));
        }

        [Fact]
        public void ChangingAFieldsNameOrPosition_IsAnOtherChange()
        {
            var signed = Signed(WithTextField());

            var renamed = AppendRevision(signed, (6, "<< /Type /Annot /Subtype /Widget /FT /Tx /T (other) /V (before) /Rect [10 10 90 30] /P 3 0 R >>"));
            var moved = AppendRevision(signed, (6, "<< /Type /Annot /Subtype /Widget /FT /Tx /T (name) /V (before) /Rect [0 0 20 20] /P 3 0 R >>"));

            Assert.Equal(PdfSignatureChanges.Other, FirstChanges(renamed));
            Assert.Equal(PdfSignatureChanges.Other, FirstChanges(moved));
        }

        [Fact]
        public void AddingAnAnnotation_IsAnAnnotationChange()
        {
            var signed = Signed();
            var page = Regex.Replace(LastBody(signed, 3), @"/Annots \[([^\]]*)\]", "/Annots [$1 50 0 R]");

            var annotated = AppendRevision(signed, (3, page), (50, "<< /Type /Annot /Subtype /Text /Rect [0 0 10 10] /Contents (note) >>"));

            Assert.Equal(PdfSignatureChanges.Annotations, FirstChanges(annotated));
        }

        [Fact]
        public void RemovingAnAnnotationOrDroppingTheForm_IsReported()
        {
            var signed = Signed(WithTextField());
            var page = Regex.Replace(LastBody(signed, 3), @"/Annots \[[^\]]*\]", "/Annots []");

            Assert.True(FirstChanges(AppendRevision(signed, (3, page))) >= PdfSignatureChanges.Annotations);
        }

        [Fact]
        public void ATamperedSignature_HasUnknownChanges()
        {
            var signed = Signed();
            var tampered = AppendRevision(signed, (99, "<< >>"));
            tampered[7] ^= 0x01;

            var signature = Assert.Single(PdfPageSource.Open(tampered).GetSignatures());

            Assert.False(signature.IsIntact);
            Assert.Equal(PdfSignatureChanges.Unknown, signature.ChangesAfter);
        }

        [Fact]
        public void AnEncryptedFile_IsComparedToo()
        {
            using var first = NewCertificate();
            using var second = NewCertificate();
            using var composer = new PdfComposer(new PdfRenderContext(), new PdfComposerOptions { Encryption = new PdfEncryptionOptions { UserPassword = "secret", OwnerPassword = "owner" } });
            composer.AddPage(100, 100);
            using var stream = new MemoryStream();
            composer.Save(stream);

            var once = PdfFileSigner.Sign(stream.ToArray(), Options(first), "secret");
            var twice = PdfFileSigner.Sign(once, Options(second), "secret");

            var signatures = PdfPageSource.Open(twice, "secret").GetSignatures();
            Assert.Equal([PdfSignatureChanges.SignaturesAndTimestamps, PdfSignatureChanges.None], signatures.Select(s => s.ChangesAfter));
        }

        [Fact]
        public void ASourceWithNoChangeInformation_DefaultsToUnknown() =>
            Assert.Equal(PdfSignatureChanges.Unknown, new PdfSourceSignature().ChangesAfter);
    }
}
