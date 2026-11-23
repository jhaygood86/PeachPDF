using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Engine.Advanced;
using PeachDrawing.PDF.Import;
using PeachDrawing.PDF.Tests.Import;

namespace PeachDrawing.PDF.Tests
{
    /// <summary>The per-signature /VRI entries of the document security store: what belongs to which signature.</summary>
    public partial class PdfVriTests
    {
        [GeneratedRegex(@"/Contents <([0-9A-Fa-f]+)>")]
        private static partial Regex ContentsPattern();

        static X509Certificate2 NewCertificate(string name = "CN=Vri Signer")
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest(name, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
            return X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pfx), null);
        }

        static byte[] Signed(X509Certificate2 certificate, byte[]? file = null, string? password = null) =>
            PdfFileSigner.Sign(file ?? TestPdf.Build(""), new PdfSigningOptions { Certificate = certificate }, password);

        static PdfValidationData Data(params byte[][] certificates)
        {
            var data = new PdfValidationData();
            foreach (var certificate in certificates) data.Certificates.Add(certificate);
            data.OcspResponses.Add([0x30, 0x03, 0x0A, 0x01, 0x00]);
            data.Crls.Add([0x30, 0x03, 0x02, 0x01, 0x01]);
            return data;
        }

        /// <summary>The keys of the signatures, worked out here from the file's own bytes: SHA-1 of the hexadecimal text of each /Contents, in capitals.</summary>
        static string[] ExpectedKeys(byte[] file) =>
            [.. ContentsPattern().Matches(Encoding.Latin1.GetString(file))
                .Select(m => Convert.ToHexString(SHA1.HashData(Encoding.ASCII.GetBytes(m.Groups[1].Value.ToLowerInvariant()))))];

        static Dictionary<string, PdfDictionary> Entries(byte[] file, string? password = null)
        {
            var doc = PdfSourceDocument.Open(file, password);
            var root = (PdfDictionary)doc.Resolve(doc.Trailer.GetRaw("Root"))!;
            var vri = doc.DictOf(doc.DictOf(root, "DSS")!, "VRI");
            return vri is null ? [] : vri.ToDictionary(kv => kv.Key.TrimStart('/'), kv => (PdfDictionary)doc.Resolve(kv.Value)!, StringComparer.Ordinal);
        }

        static int Count(PdfSourceDocument doc, PdfDictionary entry, string key) => doc.ArrayOf(entry, key)?.Elements.Count ?? 0;

        [Fact]
        public void EverySignature_GetsAnEntryKeyedByItsContents()
        {
            using var first = NewCertificate();
            using var second = NewCertificate("CN=Second");
            var twice = PdfFileSigner.Sign(Signed(first), new PdfSigningOptions { Certificate = second });

            var stored = PdfFileSigner.AddValidationData(twice, Data(first.RawData, second.RawData));

            var entries = Entries(stored);
            Assert.Equal(ExpectedKeys(twice).Order(), entries.Keys.Order());
            Assert.Equal(2, entries.Count);

            var doc = PdfSourceDocument.Open(stored);
            foreach (var entry in entries.Values)
            {
                Assert.Equal("/VRI", doc.NameOf(entry.GetRaw("Type")));
                Assert.Equal(2, Count(doc, entry, "Cert"));
                Assert.Equal(1, Count(doc, entry, "CRL"));
                Assert.Equal(1, Count(doc, entry, "OCSP"));
                Assert.StartsWith("D:", ((PdfString)doc.Resolve(entry.GetRaw("TU"))!).Value, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void TheEntryListsTheSameObjectsTheStoreHolds()
        {
            using var signer = NewCertificate();
            var stored = PdfFileSigner.AddValidationData(Signed(signer), Data(signer.RawData));

            var doc = PdfSourceDocument.Open(stored);
            var root = (PdfDictionary)doc.Resolve(doc.Trailer.GetRaw("Root"))!;
            var dss = doc.DictOf(root, "DSS")!;
            var entry = Entries(stored).Values.Single();

            Assert.Equal(doc.ArrayOf(dss, "Certs")!.Elements.Select(e => ((PdfReference)e).ObjectNumber), doc.ArrayOf(entry, "Cert")!.Elements.Select(e => ((PdfReference)e).ObjectNumber));
            Assert.Equal(doc.ArrayOf(dss, "CRLs")!.Elements.Select(e => ((PdfReference)e).ObjectNumber), doc.ArrayOf(entry, "CRL")!.Elements.Select(e => ((PdfReference)e).ObjectNumber));
        }

        [Fact]
        public void ANamedSignature_IsTheOnlyOneGivenAnEntry_AndAnotherCanBeAddedLater()
        {
            using var first = NewCertificate();
            using var second = NewCertificate("CN=Second");
            var twice = PdfFileSigner.Sign(Signed(first), new PdfSigningOptions { Certificate = second });
            var keys = ExpectedKeys(twice);                                 // Signature1 then Signature2, in file order

            var onlySecond = PdfFileSigner.AddValidationData(twice, Data(second.RawData), signatureFieldName: "Signature2");
            Assert.Equal([keys[1]], Entries(onlySecond).Keys);

            var both = PdfFileSigner.AddValidationData(onlySecond, Data(first.RawData), signatureFieldName: "Signature1");
            Assert.Equal(keys.Order(), Entries(both).Keys.Order());
            var doc = PdfSourceDocument.Open(both);
            Assert.Equal(1, Count(doc, Entries(both)[keys[0]], "Cert"));
            Assert.Equal(1, Count(doc, Entries(both)[keys[1]], "Cert"));       // the earlier entry was kept as it was
            Assert.Equal(2, doc.ArrayOf(doc.DictOf((PdfDictionary)doc.Resolve(doc.Trailer.GetRaw("Root"))!, "DSS")!, "Certs")!.Elements.Count);
        }

        [Fact]
        public void ANameThatIsNotInTheFile_IsRefused()
        {
            using var signer = NewCertificate();
            var error = Assert.Throws<InvalidOperationException>(() => PdfFileSigner.AddValidationData(Signed(signer), Data(signer.RawData), signatureFieldName: "Nope"));
            Assert.Contains("Nope", error.Message);
        }

        [Fact]
        public void MoreDataForTheSameSignature_IsMergedIntoItsEntry()
        {
            using var signer = NewCertificate();
            using var other = NewCertificate("CN=Other");
            var first = PdfFileSigner.AddValidationData(Signed(signer), Data(signer.RawData));

            var more = PdfFileSigner.AddValidationData(first, Data(signer.RawData, other.RawData));

            var doc = PdfSourceDocument.Open(more);
            var entry = Entries(more).Values.Single();
            Assert.Equal(2, Count(doc, entry, "Cert"));                           // the old reference kept, the new one added once
            Assert.Equal(1, Count(doc, entry, "CRL"));
            Assert.Equal(1, Count(doc, entry, "OCSP"));
        }

        [Fact]
        public void TheSameCallTwice_ChangesNothing()
        {
            using var signer = NewCertificate();
            var first = PdfFileSigner.AddValidationData(Signed(signer), Data(signer.RawData));

            Assert.Equal(first, PdfFileSigner.AddValidationData(first, Data(signer.RawData)));
        }

        [Fact]
        public void ALaterSignature_GetsItsEntryWithoutTouchingTheEarlierOne()
        {
            using var first = NewCertificate();
            using var second = NewCertificate("CN=Second");
            var stored = PdfFileSigner.AddValidationData(Signed(first), Data(first.RawData));
            var before = Entries(stored).Single();
            var doc = PdfSourceDocument.Open(stored);
            var beforeTime = ((PdfString)doc.Resolve(before.Value.GetRaw("TU"))!).Value;

            var later = PdfFileSigner.Sign(stored, new PdfSigningOptions { Certificate = second });
            var both = PdfFileSigner.AddValidationData(later, Data(first.RawData, second.RawData));

            var entries = Entries(both);
            Assert.Equal(2, entries.Count);
            Assert.Equal(before.Key, ExpectedKeys(both)[0]);
            Assert.Equal(beforeTime, ((PdfString)PdfSourceDocument.Open(both).Resolve(entries[before.Key].GetRaw("TU"))!).Value);
        }

        [Fact]
        public async Task ADocumentTimestamp_IsNotGivenAnEntry()
        {
            using var signer = NewCertificate();
            using var authority = new TestTimestampAuthority();
            var stamped = await PdfFileSigner.AddDocumentTimestampAsync(Signed(signer), new PdfTimestampOptions { Exchange = authority.Exchange }, cancellationToken: TestContext.Current.CancellationToken);

            var stored = PdfFileSigner.AddValidationData(stamped, Data(signer.RawData));

            Assert.Equal([ExpectedKeys(stamped)[0]], Entries(stored).Keys);        // the signature's key only, not the timestamp's
        }

        [Fact]
        public void AFileWithNoSignature_GetsTheStoreAndNoEntries()
        {
            using var signer = NewCertificate();

            var stored = PdfFileSigner.AddValidationData(TestPdf.Build(""), Data(signer.RawData));

            Assert.Empty(Entries(stored));
            Assert.Single(PdfPageSource.Open(stored).GetValidationData().Certificates);
            Assert.DoesNotContain("/VRI", Encoding.Latin1.GetString(stored));
        }

        [Fact]
        public void AnEncryptedFile_KeepsTheEntryEncryptedAndReadable()
        {
            using var signer = NewCertificate();
            using var composer = new PdfComposer(new PdfRenderContext(), new PdfComposerOptions { Encryption = new PdfEncryptionOptions { UserPassword = "secret", OwnerPassword = "owner" } });
            composer.AddPage(100, 100);
            using var stream = new MemoryStream();
            composer.Save(stream);
            var signed = Signed(signer, stream.ToArray(), "secret");

            var stored = PdfFileSigner.AddValidationData(signed, Data(signer.RawData), "secret");

            var entry = Entries(stored, "secret").Values.Single();
            var doc = PdfSourceDocument.Open(stored, "secret");
            Assert.StartsWith("D:", ((PdfString)doc.Resolve(entry.GetRaw("TU"))!).Value, StringComparison.Ordinal);   // decrypts to a date
            Assert.DoesNotContain("D:2026", Encoding.Latin1.GetString(stored));                                         // and is not stored as one
        }
    }
}
