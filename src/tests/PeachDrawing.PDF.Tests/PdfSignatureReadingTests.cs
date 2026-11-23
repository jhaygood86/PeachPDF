using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using PeachDrawing.Core;
using PeachDrawing.PDF.Tests.Import;

namespace PeachDrawing.PDF.Tests
{
    public class PdfSignatureReadingTests
    {
        static X509Certificate2 NewCertificate()
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=Peach Signer", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
            return X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pfx), null);
        }

        static PdfSigningOptions Options(X509Certificate2 certificate, Action<PdfSigningOptions>? more = null)
        {
            var options = new PdfSigningOptions { Certificate = certificate };
            more?.Invoke(options);
            return options;
        }

        static byte[] Compose(PdfComposerOptions options)
        {
            using var composer = new PdfComposer(new PdfRenderContext(), options);
            var page = composer.AddPage(100, 100);
            page.Canvas.DrawRectangle(page.Canvas.GetSolidBrush(PaintColor.FromArgb(255, 90, 40, 160)), 0, 0, 100, 100);
            using var stream = new MemoryStream();
            composer.Save(stream);
            return stream.ToArray();
        }

        static byte[] Signed(X509Certificate2 certificate, Action<PdfSigningOptions>? more = null, Action<PdfComposerOptions>? file = null)
        {
            var options = new PdfComposerOptions { Signing = Options(certificate, more) };
            file?.Invoke(options);
            return Compose(options);
        }

        static IReadOnlyList<PdfSourceSignature> Read(byte[] file) => PdfPageSource.Open(file).GetSignatures();

        static int ContentsStart(byte[] file) => Encoding.Latin1.GetString(file).IndexOf("/Contents <", StringComparison.Ordinal) + "/Contents <".Length;

        [Fact]
        public void AnUnsignedFile_HasNoSignatures() =>
            Assert.Empty(Read(Compose(new PdfComposerOptions())));

        [Theory]
        [InlineData(PdfSignatureFormat.Pkcs7Detached, "adbe.pkcs7.detached")]
        [InlineData(PdfSignatureFormat.CadesDetached, "ETSI.CAdES.detached")]
        public void ASignedFile_ReportsWhatTheSignatureStatesAndThatItIsIntact(PdfSignatureFormat format, string subFilter)
        {
            using var certificate = NewCertificate();
            var file = Signed(certificate, o => { o.Format = format; o.Reason = "Approved"; o.Location = "Atlanta"; o.SignerName = "Peach"; o.ContactInfo = "peach@example.com"; });

            var signature = Assert.Single(Read(file));

            Assert.True(signature.IsIntact, signature.Problem);
            Assert.Null(signature.Problem);
            Assert.True(signature.CoversWholeFile);
            Assert.False(signature.IsDocumentTimestamp);
            Assert.Equal("Signature1", signature.FieldName);
            Assert.Equal(subFilter, signature.SubFilter);
            Assert.Equal("Approved", signature.Reason);
            Assert.Equal("Atlanta", signature.Location);
            Assert.Equal("Peach", signature.SignerName);
            Assert.Equal("peach@example.com", signature.ContactInfo);
            Assert.Equal(certificate.Thumbprint, signature.SignerCertificate!.Thumbprint);
            Assert.True(Math.Abs((DateTimeOffset.UtcNow - signature.ClaimedTime!.Value).TotalDays) < 1.5);
            Assert.Null(signature.TimestampTime);
            Assert.Null(signature.TimestampIsValid);
        }

        [Fact]
        public void ChangingASignedByte_IsReportedAsNotIntact()
        {
            using var certificate = NewCertificate();
            var file = Signed(certificate);
            file[7] ^= 0x01; // a digit of the header version, inside the covered range

            var signature = Assert.Single(Read(file));

            Assert.False(signature.IsIntact);
            Assert.False(string.IsNullOrEmpty(signature.Problem));
            Assert.True(signature.CoversWholeFile);
        }

        [Fact]
        public void ADamagedSignatureValue_IsReportedAsNotIntact()
        {
            using var certificate = NewCertificate();
            var file = Signed(certificate);
            file[ContentsStart(file) + 400] = (byte)(file[ContentsStart(file) + 400] == (byte)'A' ? 'B' : 'A');

            Assert.False(Assert.Single(Read(file)).IsIntact);
        }

        [Fact]
        public void ContentsThatAreNotAStructure_AreReportedAsUnreadable()
        {
            using var certificate = NewCertificate();
            var file = Signed(certificate);
            file[ContentsStart(file)] = (byte)'F'; // the sequence tag 30 becomes F0

            var signature = Assert.Single(Read(file));
            Assert.False(signature.IsIntact);
            Assert.Contains("not readable", signature.Problem);
        }

        [Fact]
        public void BytesAppendedAfterTheSignature_AreReportedAsNotCoveredButTheSignatureIsIntact()
        {
            using var certificate = NewCertificate();
            var file = Signed(certificate).Concat(Encoding.ASCII.GetBytes("\n% added later\n")).ToArray();

            var signature = Assert.Single(Read(file));

            Assert.True(signature.IsIntact, signature.Problem);
            Assert.False(signature.CoversWholeFile);
        }

        [Fact]
        public void AnAppendedSignature_LeavesTheFirstIntactButNotWhole_AndListsThemInOrder()
        {
            using var first = NewCertificate();
            using var second = NewCertificate();
            var file = PdfFileSigner.Sign(Signed(first), Options(second));

            var signatures = Read(file);

            Assert.Equal(["Signature1", "Signature2"], signatures.Select(s => s.FieldName));
            Assert.All(signatures, s => Assert.True(s.IsIntact, s.Problem));
            Assert.False(signatures[0].CoversWholeFile);
            Assert.True(signatures[1].CoversWholeFile);
            Assert.Equal(first.Thumbprint, signatures[0].SignerCertificate!.Thumbprint);
            Assert.Equal(second.Thumbprint, signatures[1].SignerCertificate!.Thumbprint);
        }

        [Fact]
        public async Task AnEmbeddedTimestamp_IsReadAndChecked()
        {
            using var certificate = NewCertificate();
            using var authority = new TestTimestampAuthority();
            var options = new PdfComposerOptions { Signing = Options(certificate, o => o.Timestamp = new PdfTimestampOptions { Exchange = authority.Exchange }) };
            using var composer = new PdfComposer(new PdfRenderContext(), options);
            composer.AddPage(100, 100);
            using var stream = new MemoryStream();
            await composer.SaveAsync(stream, TestContext.Current.CancellationToken);

            var signature = Assert.Single(Read(stream.ToArray()));

            Assert.True(signature.IsIntact, signature.Problem);
            Assert.True(signature.TimestampIsValid);
            Assert.True(Math.Abs((DateTimeOffset.UtcNow - signature.TimestampTime!.Value).TotalMinutes) < 5);
            Assert.Equal(authority.Certificate.Thumbprint, signature.TimestampCertificate!.Thumbprint);
        }

        [Fact]
        public async Task ADocumentTimestamp_IsReadAsOne_AndCheckedAgainstTheWholeFile()
        {
            using var certificate = NewCertificate();
            using var authority = new TestTimestampAuthority();
            var stamped = await PdfFileSigner.AddDocumentTimestampAsync(Signed(certificate), new PdfTimestampOptions { Exchange = authority.Exchange }, cancellationToken: TestContext.Current.CancellationToken);

            var signatures = Read(stamped);

            Assert.Equal(2, signatures.Count);
            var timestamp = signatures[1];
            Assert.True(timestamp.IsDocumentTimestamp);
            Assert.Equal("ETSI.RFC3161", timestamp.SubFilter);
            Assert.Equal("Timestamp1", timestamp.FieldName);
            Assert.True(timestamp.IsIntact, timestamp.Problem);
            Assert.True(timestamp.CoversWholeFile);
            Assert.True(timestamp.TimestampIsValid);
            Assert.NotNull(timestamp.TimestampTime);
            Assert.Equal(authority.Certificate.Thumbprint, timestamp.TimestampCertificate!.Thumbprint);
            Assert.Null(timestamp.SignerCertificate);

            // Changing a byte the timestamp covers (inside the first signature's file) breaks the timestamp as well as that signature.
            stamped[7] ^= 0x01;
            var tampered = Read(stamped);
            Assert.False(tampered[1].IsIntact);
            Assert.Contains("digest", tampered[1].Problem);
        }

        [Fact]
        public async Task ADamagedDocumentTimestamp_IsReportedNotThrown()
        {
            using var certificate = NewCertificate();
            using var authority = new TestTimestampAuthority();
            var stamped = await PdfFileSigner.AddDocumentTimestampAsync(Signed(certificate), new PdfTimestampOptions { Exchange = authority.Exchange }, cancellationToken: TestContext.Current.CancellationToken);
            var at = Encoding.Latin1.GetString(stamped).LastIndexOf("/Contents <", StringComparison.Ordinal) + "/Contents <".Length;

            foreach (var offset in new[] { 60, 300, 700 })
            {
                var copy = (byte[])stamped.Clone();
                copy[at + offset] = (byte)(copy[at + offset] == (byte)'A' ? 'B' : 'A');
                var timestamp = Read(copy)[1];
                Assert.False(timestamp.IsIntact);
                Assert.NotNull(timestamp.Problem);
            }
        }

        [Fact]
        public void AnEncryptedSignedFile_IsReadThroughItsRawBytes()
        {
            using var certificate = NewCertificate();
            var file = Signed(certificate, o => o.Reason = "Sealed", f => f.Encryption = new PdfEncryptionOptions { OwnerPassword = "owner" });

            var signature = Assert.Single(Read(file));

            Assert.True(signature.IsIntact, signature.Problem);
            Assert.Equal("Sealed", signature.Reason);
        }

        const string ValueWith = "<< /Type /Sig /Filter /Adobe.PPKLite /SubFilter /adbe.pkcs7.detached {0} /Contents <00> /M (D:20200102030405+05'30') /Name (Someone) >>";

        static byte[] Hand(string value, string field = "<< /FT /Sig /T (Sig) /V 7 0 R >>") =>
            TestPdf.Build("", catalogExtras: "/AcroForm 5 0 R", extraObjects: ["<< /Fields [6 0 R] >>", field, value]);

        [Theory]
        [InlineData("/ByteRange [0 1 2 3]")]            // not within the file
        [InlineData("/ByteRange [1 1 2 3]")]            // does not start at 0
        [InlineData("/ByteRange [0 1 2]")]              // wrong length
        [InlineData("")]                                // missing
        public void AByteRangeThatDoesNotFitTheFile_IsReportedAsAProblem(string range)
        {
            var signature = Assert.Single(Read(Hand(string.Format(ValueWith, range))));

            Assert.False(signature.IsIntact);
            Assert.False(signature.CoversWholeFile);
            Assert.Contains("ByteRange", signature.Problem);
            Assert.Equal("Someone", signature.SignerName);
        }

        [Theory]
        [InlineData("/ByteRange [0 5 9300000000000000000.0 1]")]          // saturates a cast to long and wraps the sums
        [InlineData("/ByteRange [0 -5 10 1]")]
        [InlineData("/ByteRange [0 5.5 100 1]")]                           // not whole numbers
        [InlineData("/ByteRange [0 99999999999 100000000000 1]")]          // beyond the file
        [InlineData("/ByteRange [0 100 50 1]")]                            // a hole that ends before it starts
        [InlineData("/ByteRange [0 (a) 50 1]")]                            // not numbers
        public void AHostileByteRange_IsReportedAndNeverThrows(string range)
        {
            var signature = Assert.Single(Read(Hand(string.Format(ValueWith, range))));

            Assert.False(signature.IsIntact);
            Assert.Contains("ByteRange", signature.Problem);
        }

        [Fact]
        public void AtMost256SignaturesAreRead_SoAHostileFileCannotMakeTheCheckQuadratic()
        {
            const int count = 300;
            var objects = new List<string> { "<< /Fields [" + string.Join(" ", Enumerable.Range(0, count).Select(i => $"{6 + i} 0 R")) + "] >>" };
            objects.AddRange(Enumerable.Range(0, count).Select(i => $"<< /FT /Sig /T (S{i}) /V {6 + count + i} 0 R >>"));
            objects.AddRange(Enumerable.Range(0, count).Select(_ => "<< /Type /Sig /SubFilter /adbe.pkcs7.detached /ByteRange [0 1 2 3] >>"));
            var file = TestPdf.Build("", catalogExtras: "/AcroForm 5 0 R", extraObjects: [.. objects]);

            var signatures = Read(file);

            Assert.Equal(PeachDrawing.PDF.Import.PdfSourceSignatures.MaxSignatures, signatures.Count);
            Assert.Equal("S0", signatures[0].FieldName);
        }

        [Fact]
        public void WhiteSpaceBetweenTheHexDigits_IsAllowed()
        {
            using var certificate = NewCertificate();
            var file = Signed(certificate);
            var start = ContentsStart(file);
            var end = Encoding.Latin1.GetString(file).IndexOf('>', start);
            for (var i = end - 20; i < end - 10; i++)         // inside the zero padding after the signature
                file[i] = (byte)(i % 2 == 0 ? ' ' : 10);

            var signature = Assert.Single(Read(file));

            Assert.True(signature.IsIntact, signature.Problem);
        }

        [Fact]
        public void ASignatureWrittenWithIndefiniteLengths_IsReadAsBerShouldBe()
        {
            using var certificate = NewCertificate();
            var file = Signed(certificate);
            var start = ContentsStart(file);
            var hexEnd = Encoding.Latin1.GetString(file).IndexOf('>', start);
            var padded = Convert.FromHexString(Encoding.Latin1.GetString(file, start, hexEnd - start));

            // The same ContentInfo, re-framed as a BER producer may write it: the outer sequence and its [0] with indefinite lengths.
            var outer = new AsnReader(padded, AsnEncodingRules.BER).ReadSequence();
            var oid = outer.ReadEncodedValue().ToArray();
            var signedData = outer.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0)).ReadEncodedValue().ToArray();
            byte[] ber = [0x30, 0x80, .. oid, 0xA0, 0x80, .. signedData, 0, 0, 0, 0];
            Assert.True(ber.Length < (hexEnd - start) / 2);

            Array.Fill(file, (byte)'0', start, hexEnd - start);
            Encoding.ASCII.GetBytes(Convert.ToHexString(ber)).CopyTo(file, start);

            var signature = Assert.Single(Read(file));

            Assert.True(signature.IsIntact, signature.Problem);
            Assert.Equal(certificate.Thumbprint, signature.SignerCertificate!.Thumbprint);
        }

        [Theory]
        [InlineData("3080")]                // indefinite length that is never closed
        [InlineData("30840000")]            // a length longer than the contents
        [InlineData("3085000000000A")]      // a length field wider than four bytes
        [InlineData("30")]                  // nothing after the tag
        public void BrokenStructureLengths_AreReportedAsUnreadable(string hex)
        {
            using var certificate = NewCertificate();
            var file = Signed(certificate);
            var start = ContentsStart(file);
            var hexEnd = Encoding.Latin1.GetString(file).IndexOf('>', start);
            Array.Fill(file, (byte)'0', start, hexEnd - start);
            Encoding.ASCII.GetBytes(hex).CopyTo(file, start);

            var signature = Assert.Single(Read(file));

            Assert.False(signature.IsIntact);
            Assert.Matches("not (a )?readable", signature.Problem);
        }

        [Fact]
        public void TheClaimedTime_IsParsedFromAPdfDate()
        {
            var signature = Assert.Single(Read(Hand(string.Format(ValueWith, ""))));

            Assert.Equal(new DateTimeOffset(2020, 1, 2, 3, 4, 5, new TimeSpan(5, 30, 0)), signature.ClaimedTime);
        }

        [Theory]
        [InlineData("(D:2021)", 2021, 1, 1)]
        [InlineData("(D:20210305Z)", 2021, 3, 5)]
        [InlineData("(garbage)", 0, 0, 0)]
        [InlineData("(D:20211399)", 0, 0, 0)]
        public void PartialAndBadDates_AreHandled(string date, int year, int month, int day)
        {
            var value = $"<< /Type /Sig /SubFilter /adbe.pkcs7.detached /M {date} >>";
            var signature = Assert.Single(Read(Hand(value)));

            if (year == 0)
                Assert.Null(signature.ClaimedTime);
            else
                Assert.Equal(new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero), signature.ClaimedTime);
        }

        [Fact]
        public void SignatureFieldsWithNoValue_AreNotListed_AndASharedValueIsListedOnce()
        {
            var empty = TestPdf.Build("", catalogExtras: "/AcroForm 5 0 R", extraObjects: ["<< /Fields [6 0 R] >>", "<< /FT /Sig /T (Sig) >>"]);
            Assert.Empty(Read(empty));

            var shared = TestPdf.Build("", catalogExtras: "/AcroForm 5 0 R", extraObjects:
                ["<< /Fields [6 0 R 7 0 R] >>", "<< /FT /Sig /T (A) /V 8 0 R >>", "<< /FT /Sig /T (B) /V 8 0 R >>", string.Format(ValueWith, "")]);
            Assert.Single(Read(shared));
        }

        [Fact]
        public void KidsOfASignatureField_AreNamedByTheirParents()
        {
            var file = TestPdf.Build("", catalogExtras: "/AcroForm 5 0 R", extraObjects:
                ["<< /Fields [6 0 R] >>", "<< /FT /Sig /T (group) /Kids [7 0 R] >>", "<< /Parent 6 0 R /T (inner) /V 8 0 R >>", string.Format(ValueWith, "")]);

            Assert.Equal("group.inner", Assert.Single(Read(file)).FieldName);
        }
    }
}
