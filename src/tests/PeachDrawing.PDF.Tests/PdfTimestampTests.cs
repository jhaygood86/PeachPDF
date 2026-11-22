using System.Formats.Asn1;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using PeachDrawing.Core;

namespace PeachDrawing.PDF.Tests
{
    /// <summary>A time stamping authority built from the BCL, which can read RFC 3161 tokens but not make them.</summary>
    internal sealed class TestTimestampAuthority : IDisposable
    {
        const string TimestampTokenInfo = "1.2.840.113549.1.9.16.1.4";
        const string SigningCertificateV2 = "1.2.840.113549.1.9.16.2.47";

        readonly X509Certificate2 _certificate;
        public int Requests { get; private set; }
        public Func<byte[], byte[]>? Tamper { get; set; }

        public TestTimestampAuthority()
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=Test TSA", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.8")], critical: true));
            using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
            _certificate = X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pfx), null);
        }

        public X509Certificate2 Certificate => _certificate;

        public byte[] Respond(ReadOnlyMemory<byte> requestBytes)
        {
            Requests++;
            // TimeStampReq: version, messageImprint { algorithm, hash }, optional policy, optional nonce.
            var reader = new AsnReader(requestBytes, AsnEncodingRules.DER).ReadSequence();
            reader.ReadInteger();
            var imprint = reader.ReadSequence();
            var hashAlgorithm = imprint.ReadSequence().ReadObjectIdentifier();
            var hash = imprint.ReadOctetString();
            byte[]? nonce = null;
            while (reader.HasData)
            {
                if (reader.PeekTag().TagValue == (int)UniversalTagNumber.Integer && reader.PeekTag().TagClass == TagClass.Universal)
                    nonce = reader.ReadIntegerBytes().ToArray();
                else
                    reader.ReadEncodedValue();
            }

            var info = new AsnWriter(AsnEncodingRules.DER);
            using (info.PushSequence())
            {
                info.WriteInteger(1);
                info.WriteObjectIdentifier("1.2.3.4.5");
                using (info.PushSequence())
                {
                    using (info.PushSequence())
                    {
                        info.WriteObjectIdentifier(hashAlgorithm);
                        info.WriteNull();
                    }

                    info.WriteOctetString(hash);
                }

                info.WriteInteger(42);
                info.WriteGeneralizedTime(DateTimeOffset.UtcNow, omitFractionalSeconds: true);
                if (nonce is not null)
                    info.WriteIntegerUnsigned(nonce.AsSpan().TrimStart((byte)0));
            }

            var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, _certificate)
            {
                DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1"),
                IncludeOption = X509IncludeOption.EndCertOnly,
            };
            var essId = new AsnWriter(AsnEncodingRules.DER);
            using (essId.PushSequence())
            using (essId.PushSequence())
            using (essId.PushSequence())
                essId.WriteOctetString(SHA256.HashData(_certificate.RawData));
            signer.SignedAttributes.Add(new AsnEncodedData(new Oid(SigningCertificateV2), essId.Encode()));

            var cms = new SignedCms(new ContentInfo(new Oid(TimestampTokenInfo), info.Encode()), detached: false);
            cms.ComputeSignature(signer);

            var response = new AsnWriter(AsnEncodingRules.DER);
            using (response.PushSequence())
            {
                using (response.PushSequence())
                    response.WriteInteger(0);
                response.WriteEncodedValue(cms.Encode());
            }

            var bytes = response.Encode();
            return Tamper?.Invoke(bytes) ?? bytes;
        }

        public Task<byte[]> Exchange(ReadOnlyMemory<byte> request, CancellationToken cancellation) => Task.FromResult(Respond(request));

        public void Dispose() => _certificate.Dispose();
    }

    public partial class PdfTimestampTests
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

        static PdfComposerOptions Options(X509Certificate2 certificate, PdfTimestampOptions? timestamp, Action<PdfSigningOptions>? more = null)
        {
            var signing = new PdfSigningOptions { Certificate = certificate, Timestamp = timestamp, Format = PdfSignatureFormat.CadesDetached };
            more?.Invoke(signing);
            return new PdfComposerOptions { Signing = signing };
        }

        static Task<byte[]> SaveAsync(PdfComposerOptions options) => SaveWithTokenAsync(options, TestContext.Current.CancellationToken);

        static async Task<byte[]> SaveWithTokenAsync(PdfComposerOptions options, CancellationToken cancellation)
        {
            using var composer = new PdfComposer(new PdfRenderContext(), options);
            var page = composer.AddPage(100, 100);
            page.Canvas.DrawRectangle(page.Canvas.GetSolidBrush(PaintColor.FromArgb(255, 20, 90, 200)), 0, 0, 100, 100);
            using var stream = new MemoryStream();
            await composer.SaveAsync(stream, cancellation);
            return stream.ToArray();
        }

        static (SignedCms Cms, byte[] Covered) Read(byte[] file)
        {
            var text = Encoding.Latin1.GetString(file);
            var range = ByteRangePattern().Match(text).Groups.Cast<Group>().Skip(1).Select(g => int.Parse(g.Value)).ToArray();
            var covered = file[range[0]..(range[0] + range[1])].Concat(file[range[2]..(range[2] + range[3])]).ToArray();
            var padded = Convert.FromHexString(text[(range[1] + 1)..(range[2] - 1)]);
            var lengthBytes = padded[1] < 0x80 ? 0 : padded[1] & 0x7F;
            var length = padded[1] < 0x80 ? padded[1] : padded.Skip(2).Take(lengthBytes).Aggregate(0, (n, b) => (n << 8) | b);

            var cms = new SignedCms(new ContentInfo(covered), detached: true);
            cms.Decode(padded[..(2 + lengthBytes + length)]);
            return (cms, covered);
        }

        [Fact]
        public async Task ATimestampedSignature_CarriesATokenForItsOwnSignatureValue()
        {
            using var certificate = NewCertificate();
            using var authority = new TestTimestampAuthority();
            var file = await SaveAsync(Options(certificate, new PdfTimestampOptions { Exchange = authority.Exchange }));

            var (cms, _) = Read(file);
            cms.CheckSignature(verifySignatureOnly: true);

            var signer = cms.SignerInfos[0];
            var attribute = signer.UnsignedAttributes.Cast<CryptographicAttributeObject>().Single(a => a.Oid!.Value == "1.2.840.113549.1.9.16.2.14");
            Assert.True(Rfc3161TimestampToken.TryDecode(attribute.Values[0].RawData, out var token, out _));
            Assert.True(token!.VerifySignatureForSignerInfo(signer, out _));
            Assert.Equal(1, authority.Requests);
            Assert.True(PdfPageSource.Open(file).HasSignatures);
        }

        [Theory]
        [InlineData("SHA384")]
        [InlineData("SHA512")]
        public async Task TheTimestampDigestCanBeChosen(string hash)
        {
            using var certificate = NewCertificate();
            using var authority = new TestTimestampAuthority();
            var algorithm = new HashAlgorithmName(hash);
            var file = await SaveAsync(Options(certificate, new PdfTimestampOptions { Exchange = authority.Exchange, HashAlgorithm = algorithm }));

            var signer = Read(file).Cms.SignerInfos[0];
            var attribute = signer.UnsignedAttributes.Cast<CryptographicAttributeObject>().Single(a => a.Oid!.Value == "1.2.840.113549.1.9.16.2.14");
            Assert.True(Rfc3161TimestampToken.TryDecode(attribute.Values[0].RawData, out var token, out _));
            Assert.Equal(algorithm.Name, token!.TokenInfo.HashAlgorithmId.FriendlyName!.Replace("sha", "SHA").Replace("-", ""), ignoreCase: true);
        }

        [Fact]
        public async Task ThePkcs7Format_CanBeTimestampedToo()
        {
            using var certificate = NewCertificate();
            using var authority = new TestTimestampAuthority();
            var file = await SaveAsync(Options(certificate, new PdfTimestampOptions { Exchange = authority.Exchange }, s => s.Format = PdfSignatureFormat.Pkcs7Detached));

            var (cms, _) = Read(file);
            cms.CheckSignature(true);
            Assert.Single(cms.SignerInfos[0].UnsignedAttributes);
        }

        [Fact]
        public void Save_RefusesATimestampedDocument_AndNamesSaveAsync()
        {
            using var certificate = NewCertificate();
            using var authority = new TestTimestampAuthority();
            using var composer = new PdfComposer(new PdfRenderContext(), Options(certificate, new PdfTimestampOptions { Exchange = authority.Exchange }));
            composer.AddPage(100, 100);

            var error = Assert.Throws<InvalidOperationException>(() => composer.Save(new MemoryStream()));
            Assert.Contains("SaveAsync", error.Message);
            Assert.Equal(0, authority.Requests);
        }

        [Fact]
        public async Task SaveAsync_WritesAnUnsignedAndAnUntimestampedDocumentToo()
        {
            using var certificate = NewCertificate();
            var plain = await SaveAsync(new PdfComposerOptions());
            Assert.DoesNotContain("/ByteRange", Encoding.Latin1.GetString(plain));

            var signed = await SaveAsync(Options(certificate, null));
            Read(signed).Cms.CheckSignature(true);
        }

        [Fact]
        public async Task AResponseThatIsNotAToken_IsRefused()
        {
            using var certificate = NewCertificate();
            var options = Options(certificate, new PdfTimestampOptions { Exchange = (_, _) => Task.FromResult(new byte[] { 1, 2, 3 }) });

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => SaveAsync(options));
            Assert.Contains("refused", error.Message);
        }

        [Fact]
        public async Task AResponseForAnotherRequest_IsRefused()
        {
            using var certificate = NewCertificate();
            using var authority = new TestTimestampAuthority();
            byte[]? other = null;
            var options = Options(certificate, new PdfTimestampOptions
            {
                Exchange = (request, cancellation) =>
                {
                    // The authority answers a different request (another message and nonce) than the one it was sent.
                    other ??= Rfc3161TimestampRequest.CreateFromData([9, 9, 9], HashAlgorithmName.SHA256, nonce: new byte[] { 7, 7, 7, 7 }).Encode().ToArray();
                    return Task.FromResult(authority.Respond(other));
                },
            });

            await Assert.ThrowsAsync<InvalidOperationException>(() => SaveAsync(options));
        }

        [Fact]
        public async Task ATokenThatDoesNotFit_NamesTheReservedSize()
        {
            using var certificate = NewCertificate();
            using var authority = new TestTimestampAuthority();
            var options = Options(certificate, new PdfTimestampOptions { Exchange = authority.Exchange }, s => s.ReservedSignatureBytes = 2048);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => SaveAsync(options));
            Assert.Contains("ReservedSignatureBytes", error.Message);
        }

        [Fact]
        public async Task CancellationReachesTheExchange()
        {
            using var certificate = NewCertificate();
            using var cancellation = new CancellationTokenSource();
            var options = Options(certificate, new PdfTimestampOptions
            {
                Exchange = (_, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    return Task.FromResult(Array.Empty<byte>());
                },
            });

            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SaveWithTokenAsync(options, cancellation.Token));
        }

        [Fact]
        public void InvalidTimestampOptions_AreRejectedWhenApplied()
        {
            using var certificate = NewCertificate();
            Assert.Throws<InvalidOperationException>(() => new PdfComposer(new PdfRenderContext(), Options(certificate, new PdfTimestampOptions())));
            Assert.Throws<InvalidOperationException>(() => new PdfComposer(new PdfRenderContext(),
                Options(certificate, new PdfTimestampOptions { Exchange = (_, _) => Task.FromResult(Array.Empty<byte>()), HashAlgorithm = HashAlgorithmName.SHA1 })));
        }

        sealed class Handler(TestTimestampAuthority authority) : HttpMessageHandler
        {
            public string? ContentType { get; private set; }
            public Uri? Address { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                ContentType = request.Content!.Headers.ContentType!.MediaType;
                Address = request.RequestUri;
                var body = await request.Content.ReadAsByteArrayAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(authority.Respond(body)) };
            }
        }

        [Fact]
        public async Task FromHttpAuthority_PostsATimestampQuery_AndTheTokenIsEmbedded()
        {
            using var certificate = NewCertificate();
            using var authority = new TestTimestampAuthority();
            var handler = new Handler(authority);
            using var client = new HttpClient(handler);
            var address = new Uri("https://tsa.example/stamp");

            var file = await SaveAsync(Options(certificate, PdfTimestampOptions.FromHttpAuthority(address, client)));

            Assert.Equal("application/timestamp-query", handler.ContentType);
            Assert.Equal(address, handler.Address);
            Assert.Single(Read(file).Cms.SignerInfos[0].UnsignedAttributes);
        }

        [Fact]
        public async Task FromHttpAuthority_ReportsAFailedRequest()
        {
            using var certificate = NewCertificate();
            using var client = new HttpClient(new FailingHandler());

            var options = Options(certificate, PdfTimestampOptions.FromHttpAuthority(new Uri("https://tsa.example/"), client));
            await Assert.ThrowsAsync<HttpRequestException>(() => SaveAsync(options));
        }

        sealed class FailingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }

        [Fact]
        public void FromHttpAuthority_RequiresItsArguments()
        {
            using var client = new HttpClient();
            Assert.Throws<ArgumentNullException>(() => PdfTimestampOptions.FromHttpAuthority(null!, client));
            Assert.Throws<ArgumentNullException>(() => PdfTimestampOptions.FromHttpAuthority(new Uri("https://tsa.example/"), null!));
        }
    }
}
