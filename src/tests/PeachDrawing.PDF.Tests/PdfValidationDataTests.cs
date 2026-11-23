using System.Formats.Asn1;
using System.Net;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using PeachDrawing.Core;
using PeachDrawing.PDF.Tests.Import;

namespace PeachDrawing.PDF.Tests
{
    /// <summary>The document security store: certificates, OCSP responses and CRLs a signature can be checked with later.</summary>
    public class PdfValidationDataTests
    {
        static readonly byte[] SuccessfulOcsp = [0x30, 0x03, 0x0A, 0x01, 0x00];       // OCSPResponse { responseStatus successful }
        static readonly byte[] RefusedOcsp = [0x30, 0x03, 0x0A, 0x01, 0x01];          // malformedRequest

        /// <summary>A CA, and a leaf it issued whose extensions name a CRL and an OCSP responder.</summary>
        sealed class Pki : IDisposable
        {
            public X509Certificate2 Ca { get; }
            public X509Certificate2 Leaf { get; }
            public byte[] Crl { get; }

            public Pki(string crlAddress = "http://pki.test/ca.crl", string ocspAddress = "http://pki.test/ocsp")
            {
                using var caKey = RSA.Create(2048);
                var caRequest = new CertificateRequest("CN=Test CA", caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
                caRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
                using var caCertificate = caRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(60));
                Ca = X509CertificateLoader.LoadPkcs12(caCertificate.Export(X509ContentType.Pfx), null);

                using var leafKey = RSA.Create(2048);
                var leafRequest = new CertificateRequest("CN=Test Leaf", leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                leafRequest.CertificateExtensions.Add(CertificateRevocationListBuilder.BuildCrlDistributionPointExtension([crlAddress]));
                leafRequest.CertificateExtensions.Add(new X509AuthorityInformationAccessExtension([ocspAddress], null));
                var issuer = X509SignatureGenerator.CreateForRSA(caKey, RSASignaturePadding.Pkcs1);
                using var issued = leafRequest.Create(Ca.SubjectName, issuer, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30), RandomNumberGenerator.GetBytes(8));
                using var withKey = issued.CopyWithPrivateKey(leafKey);
                Leaf = X509CertificateLoader.LoadPkcs12(withKey.Export(X509ContentType.Pfx), null);

                Crl = new CertificateRevocationListBuilder().Build(Ca, BigInteger.One, DateTimeOffset.UtcNow.AddDays(7), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }

            public void Dispose()
            {
                Ca.Dispose();
                Leaf.Dispose();
            }
        }

        sealed class Responder(byte[] crl, byte[] ocsp, bool crlMissing = false) : HttpMessageHandler
        {
            public List<byte[]> OcspRequests { get; } = [];

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (request.RequestUri!.AbsolutePath == "/ca.crl")
                    return crlMissing ? new HttpResponseMessage(HttpStatusCode.NotFound) : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(crl) };

                Assert.Equal("application/ocsp-request", request.Content!.Headers.ContentType!.MediaType);
                OcspRequests.Add(await request.Content.ReadAsByteArrayAsync(cancellationToken));
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(ocsp) };
            }
        }

        static X509Certificate2 SelfSigned(string name = "CN=Solo")
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest(name, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
            return X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pfx), null);
        }

        static byte[] SignedHandFile(X509Certificate2 certificate, byte[]? file = null) =>
            PdfFileSigner.Sign(file ?? TestPdf.Build(""), new PdfSigningOptions { Certificate = certificate });

        static PdfValidationData Sample(params byte[][] certificates)
        {
            var data = new PdfValidationData();
            foreach (var certificate in certificates) data.Certificates.Add(certificate);
            data.OcspResponses.Add(SuccessfulOcsp);
            data.Crls.Add([0x30, 0x03, 0x02, 0x01, 0x01]);
            return data;
        }

        // ------------------------------------------------------------------ collecting

        [Fact]
        public async Task TheChainTheCrlAndTheOcspResponse_AreCollectedOverHttp()
        {
            using var pki = new Pki();
            var responder = new Responder(pki.Crl, SuccessfulOcsp);
            using var client = new HttpClient(responder);

            var data = await PdfValidationData.CollectAsync(pki.Leaf, [pki.Ca], client, TestContext.Current.CancellationToken);

            Assert.Empty(data.Problems);
            Assert.Equal([pki.Leaf.RawData, pki.Ca.RawData], data.Certificates);
            Assert.Equal([pki.Crl], data.Crls);
            Assert.Equal([SuccessfulOcsp], data.OcspResponses);
            Assert.False(data.IsEmpty);

            // The request names the issuer by the SHA-1 of its name and key, and the certificate by its serial number (RFC 6960 4.1.1).
            var certId = new AsnReader(Assert.Single(responder.OcspRequests), AsnEncodingRules.DER).ReadSequence().ReadSequence().ReadSequence().ReadSequence().ReadSequence();
            Assert.Equal("1.3.14.3.2.26", certId.ReadSequence().ReadObjectIdentifier());
            Assert.Equal(SHA1.HashData(pki.Ca.SubjectName.RawData), certId.ReadOctetString());
            Assert.Equal(SHA1.HashData(pki.Ca.PublicKey.EncodedKeyValue.RawData), certId.ReadOctetString());
            Assert.Equal(pki.Leaf.SerialNumberBytes.ToArray().SkipWhile(b => b == 0), certId.ReadIntegerBytes().ToArray().SkipWhile(b => b == 0));
        }

        [Fact]
        public async Task ACertificateThatNamesNothing_GivesJustItself()
        {
            using var solo = SelfSigned();
            using var client = new HttpClient(new Responder([], []));

            var data = await PdfValidationData.CollectAsync(solo, null, client, TestContext.Current.CancellationToken);

            Assert.Equal([solo.RawData], data.Certificates);
            Assert.Empty(data.Crls);
            Assert.Empty(data.OcspResponses);
            Assert.Empty(data.Problems);
        }

        [Fact]
        public async Task AnIntermediateTheCertificateDoesNotCarry_CanBeSuppliedAsAnExtra()
        {
            using var pki = new Pki();
            using var publicLeaf = X509CertificateLoader.LoadCertificate(pki.Leaf.RawData);
            using var client = new HttpClient(new Responder(pki.Crl, SuccessfulOcsp));

            var without = await PdfValidationData.CollectAsync(publicLeaf, null, client, TestContext.Current.CancellationToken);
            var with = await PdfValidationData.CollectAsync(publicLeaf, [pki.Ca], client, TestContext.Current.CancellationToken);

            Assert.Single(without.Certificates);
            Assert.Contains(without.Problems, p => p.Contains("incomplete"));
            Assert.Empty(with.Problems);
            Assert.Equal(2, with.Certificates.Count);
            Assert.Single(with.Crls);
        }

        [Fact]
        public async Task WhatCannotBeFetched_IsListedAsAProblem_AndTheRestIsKept()
        {
            using var pki = new Pki();
            using var client = new HttpClient(new Responder(pki.Crl, RefusedOcsp, crlMissing: true));

            var data = await PdfValidationData.CollectAsync(pki.Leaf, [pki.Ca], client, TestContext.Current.CancellationToken);

            Assert.Equal(2, data.Certificates.Count);
            Assert.Empty(data.Crls);
            Assert.Empty(data.OcspResponses);
            Assert.Equal(2, data.Problems.Count);
            Assert.Contains(data.Problems, p => p.Contains("CRL"));
            Assert.Contains(data.Problems, p => p.Contains("OCSP") && p.Contains("successfully"));
        }

        [Fact]
        public async Task AnAddressThatIsNotWeb_IsNotFetched()
        {
            using var pki = new Pki("ftp://pki.test/ca.crl", "file:///c:/ocsp");
            var responder = new Responder(pki.Crl, SuccessfulOcsp);
            using var client = new HttpClient(responder);

            var data = await PdfValidationData.CollectAsync(pki.Leaf, [pki.Ca], client, TestContext.Current.CancellationToken);

            Assert.Empty(responder.OcspRequests);
            Assert.Equal(2, data.Problems.Count);
            Assert.All(data.Problems, p => Assert.Contains("not an http or https address", p));
        }

        [Fact]
        public async Task ANetworkFailure_IsAProblem_ButCancellationAbortsTheCall()
        {
            using var pki = new Pki();
            using var failing = new HttpClient(new FailingHandler());
            var data = await PdfValidationData.CollectAsync(pki.Leaf, [pki.Ca], failing, TestContext.Current.CancellationToken);
            Assert.Equal(2, data.Problems.Count);

            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();
            using var client = new HttpClient(new Responder(pki.Crl, SuccessfulOcsp));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PdfValidationData.CollectAsync(pki.Leaf, [pki.Ca], client, cancellation.Token));
        }

        sealed class FailingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                throw new HttpRequestException("no route to host");
        }

        [Fact]
        public void CollectingRequiresItsArguments()
        {
            using var client = new HttpClient();
            using var solo = SelfSigned();
            Assert.Throws<ArgumentNullException>(() => { _ = PdfValidationData.CollectAsync(null!, null, client, TestContext.Current.CancellationToken); });
            Assert.Throws<ArgumentNullException>(() => { _ = PdfValidationData.CollectAsync(solo, null, null!, TestContext.Current.CancellationToken); });
        }

        // ------------------------------------------------------------------ storing

        [Fact]
        public void ValidationData_IsStoredInTheSecurityStore_AndReadBack()
        {
            using var signer = SelfSigned();
            var signed = SignedHandFile(signer);
            using var extra = SelfSigned("CN=Intermediate");
            var data = Sample(signer.RawData, extra.RawData);

            var stored = PdfFileSigner.AddValidationData(signed, data);

            Assert.Equal(signed, stored[..signed.Length]);
            var read = PdfPageSource.Open(stored).GetValidationData();
            Assert.Equal(data.Certificates, read.Certificates);
            Assert.Equal(data.OcspResponses, read.OcspResponses);
            Assert.Equal(data.Crls, read.Crls);
            Assert.True(PdfPageSource.Open(signed).GetValidationData().IsEmpty);
        }

        [Fact]
        public void TheSignatureStaysIntact_AndTheStoreIsNotAChangeToWhatItSigned()
        {
            using var signer = SelfSigned();
            var stored = PdfFileSigner.AddValidationData(SignedHandFile(signer), Sample(signer.RawData));

            var signature = Assert.Single(PdfPageSource.Open(stored).GetSignatures());

            Assert.True(signature.IsIntact, signature.Problem);
            Assert.Equal(PdfSignatureChanges.SignaturesAndTimestamps, signature.ChangesAfter);
        }

        [Fact]
        public void AFileOlderThanPdf20_SaysItUsesTheStore_AndANewerOneDoesNot()
        {
            using var signer = SelfSigned();
            var old = PdfFileSigner.AddValidationData(SignedHandFile(signer), Sample(signer.RawData));
            Assert.Contains("/ESIC", Encoding.Latin1.GetString(old));
            Assert.Contains("/ExtensionLevel 1", Encoding.Latin1.GetString(old));

            using var composer = new PdfComposer(new PdfRenderContext());
            composer.AddPage(100, 100);
            using var stream = new MemoryStream();
            composer.Save(stream);
            Assert.StartsWith("%PDF-2", Encoding.Latin1.GetString(stream.ToArray(), 0, 10));
            var modern = PdfFileSigner.AddValidationData(SignedHandFile(signer, stream.ToArray()), Sample(signer.RawData));
            Assert.DoesNotContain("/ESIC", Encoding.Latin1.GetString(modern));
            Assert.Contains("/DSS", Encoding.Latin1.GetString(modern));
        }

        [Fact]
        public void AnExistingExtensionsDictionary_KeepsItsEntries()
        {
            using var signer = SelfSigned();
            var file = TestPdf.Build("", catalogExtras: "/Extensions << /ADBE << /BaseVersion /1.7 /ExtensionLevel 3 >> >>");

            var text = Encoding.Latin1.GetString(PdfFileSigner.AddValidationData(SignedHandFile(signer, file), Sample(signer.RawData)));

            Assert.Contains("/ADBE", text);
            Assert.Contains("/ESIC", text);
        }

        [Fact]
        public void DataAddedTwice_IsStoredOnce_AndNewDataIsAppendedToTheStore()
        {
            using var signer = SelfSigned();
            using var other = SelfSigned("CN=Other");
            var first = PdfFileSigner.AddValidationData(SignedHandFile(signer), Sample(signer.RawData));

            var again = PdfFileSigner.AddValidationData(first, Sample(signer.RawData));
            Assert.Equal(first, again);                        // nothing new: the file comes back as it was

            var more = PdfFileSigner.AddValidationData(first, Sample(signer.RawData, other.RawData));
            var read = PdfPageSource.Open(more).GetValidationData();
            Assert.Equal([signer.RawData, other.RawData], read.Certificates);
            Assert.Single(read.OcspResponses);
            Assert.Single(read.Crls);
            Assert.Equal(first, more[..first.Length]);
        }

        [Fact]
        public void AnEncryptedFile_StoresItsValidationDataEncrypted()
        {
            using var signer = SelfSigned();
            using var composer = new PdfComposer(new PdfRenderContext(), new PdfComposerOptions { Encryption = new PdfEncryptionOptions { UserPassword = "secret", OwnerPassword = "owner" } });
            composer.AddPage(100, 100);
            using var stream = new MemoryStream();
            composer.Save(stream);
            var signed = PdfFileSigner.Sign(stream.ToArray(), new PdfSigningOptions { Certificate = signer }, "secret");

            var stored = PdfFileSigner.AddValidationData(signed, Sample(signer.RawData), "secret");

            Assert.Throws<PdfPasswordException>(() => PdfFileSigner.AddValidationData(signed, Sample(signer.RawData)));
            Assert.Equal([signer.RawData], PdfPageSource.Open(stored, "secret").GetValidationData().Certificates);
            Assert.True(Assert.Single(PdfPageSource.Open(stored, "secret").GetSignatures()).IsIntact);
            // The certificate's bytes are not in the file in the clear.
            var clear = Encoding.Latin1.GetString(signer.RawData, 40, 24);
            Assert.DoesNotContain(clear, Encoding.Latin1.GetString(stored, signed.Length, stored.Length - signed.Length));
        }

        [Fact]
        public void NothingToAdd_OrNoFile_IsRefused()
        {
            Assert.Throws<ArgumentNullException>(() => PdfFileSigner.AddValidationData(null!, Sample()));
            Assert.Throws<ArgumentNullException>(() => PdfFileSigner.AddValidationData([], null!));
            Assert.Throws<InvalidOperationException>(() => PdfFileSigner.AddValidationData(TestPdf.Build(""), new PdfValidationData()));
        }

        [Fact]
        public async Task LongTermValidation_IsASignatureThenItsValidationDataThenADocumentTimestamp()
        {
            using var pki = new Pki();
            using var authority = new TestTimestampAuthority();
            using var client = new HttpClient(new Responder(pki.Crl, SuccessfulOcsp));

            var signed = SignedHandFile(pki.Leaf);
            var data = await PdfValidationData.CollectAsync(pki.Leaf, [pki.Ca], client, TestContext.Current.CancellationToken);
            var lt = PdfFileSigner.AddValidationData(signed, data);
            var lta = await PdfFileSigner.AddDocumentTimestampAsync(lt, new PdfTimestampOptions { Exchange = authority.Exchange }, cancellationToken: TestContext.Current.CancellationToken);

            var signatures = PdfPageSource.Open(lta).GetSignatures();
            Assert.Equal(["Signature1", "Timestamp1"], signatures.Select(s => s.FieldName));
            Assert.All(signatures, s => Assert.True(s.IsIntact, s.Problem));
            Assert.Equal(PdfSignatureChanges.SignaturesAndTimestamps, signatures[0].ChangesAfter);
            Assert.Equal(2, PdfPageSource.Open(lta).GetValidationData().Certificates.Count);
        }
    }
}
