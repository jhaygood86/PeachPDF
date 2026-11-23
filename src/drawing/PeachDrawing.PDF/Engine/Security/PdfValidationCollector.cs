using System;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace PeachDrawing.PDF.Engine.Security;

/// <summary>Collects a certificate's chain, the CRLs it names and an OCSP response from each responder it names.</summary>
internal static class PdfValidationCollector
{
    const string CrlDistributionPoints = "2.5.29.31";
    const long MaxResponseBytes = 10 * 1024 * 1024;

    public static async Task<PdfValidationData> CollectAsync(X509Certificate2 certificate, IEnumerable<X509Certificate2>? additional,
        HttpClient client, CancellationToken cancellationToken)
    {
        var data = new PdfValidationData();

        // Built with nothing trusted and no revocation check of its own: the point is to learn the chain, trusted or not.
        var chain = new List<X509Certificate2>();
        using (var builder = new X509Chain())
        {
            builder.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            builder.ChainPolicy.VerificationFlags = X509VerificationFlags.AllFlags;
            foreach (var extra in additional ?? [])
                builder.ChainPolicy.ExtraStore.Add(extra);
            builder.Build(certificate);
            foreach (var element in builder.ChainElements)
                chain.Add(X509CertificateLoader.LoadCertificate(element.Certificate.RawData));
        }

        if (chain.Count == 0)
            chain.Add(X509CertificateLoader.LoadCertificate(certificate.RawData));

        foreach (var link in chain)
            data.Certificates.Add(link.RawData);

        // A chain that stops short of a root has no issuer to ask about its last certificate; saying so beats a silently thin result.
        var top = chain[^1];
        if (!top.SubjectName.RawData.AsSpan().SequenceEqual(top.IssuerName.RawData))
            data.AddProblem($"The chain is incomplete: the issuer of \"{top.SubjectName.Name}\" was not found, so its revocation data could not be collected. Pass the missing certificates as additionalCertificates.");

        // A certificate is checked by the one above it; the root has no issuer to ask about it.
        for (int i = 0; i + 1 < chain.Count; i++)
        {
            foreach (var address in CrlAddresses(chain[i]))
                await FetchCrlAsync(address, client, data, cancellationToken).ConfigureAwait(false);
            foreach (var address in OcspAddresses(chain[i]))
                await FetchOcspAsync(chain[i], chain[i + 1], address, client, data, cancellationToken).ConfigureAwait(false);
        }

        foreach (var link in chain)
            link.Dispose();
        return data;
    }

    static async Task FetchCrlAsync(string address, HttpClient client, PdfValidationData data, CancellationToken cancellationToken)
    {
        // A handler that does not look at the token would otherwise let every remaining request run.
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsWebAddress(address, out var uri))
        {
            data.AddProblem($"CRL address {address} is not an http or https address.");
            return;
        }

        try
        {
            using var response = await client.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var bytes = await ReadAsync(response, cancellationToken).ConfigureAwait(false);
            if (!StartsWithSequence(bytes))
                data.AddProblem($"The CRL at {address} is not a DER certificate list.");
            else if (!data.Crls.Any(existing => existing.AsSpan().SequenceEqual(bytes)))
                data.Crls.Add(bytes);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or InvalidOperationException && !cancellationToken.IsCancellationRequested)
        {
            data.AddProblem($"The CRL at {address} could not be fetched: {error.Message}");
        }
    }

    static async Task FetchOcspAsync(X509Certificate2 subject, X509Certificate2 issuer, string address, HttpClient client, PdfValidationData data,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsWebAddress(address, out var uri))
        {
            data.AddProblem($"OCSP address {address} is not an http or https address.");
            return;
        }

        try
        {
            using var content = new ByteArrayContent(OcspRequest(subject, issuer));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/ocsp-request");
            using var response = await client.PostAsync(uri, content, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var bytes = await ReadAsync(response, cancellationToken).ConfigureAwait(false);
            if (!IsSuccessfulOcspResponse(bytes))
                data.AddProblem($"The OCSP responder at {address} did not answer successfully.");
            else if (!data.OcspResponses.Any(existing => existing.AsSpan().SequenceEqual(bytes)))
                data.OcspResponses.Add(bytes);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or InvalidOperationException && !cancellationToken.IsCancellationRequested)
        {
            data.AddProblem($"The OCSP responder at {address} could not be reached: {error.Message}");
        }
    }

    static async Task<byte[]> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > MaxResponseBytes)
            throw new InvalidOperationException("The response is larger than the 10 MB this will take.");
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (bytes.Length > MaxResponseBytes)
            throw new InvalidOperationException("The response is larger than the 10 MB this will take.");
        return bytes;
    }

    static bool IsWebAddress(string address, out Uri uri) =>
        Uri.TryCreate(address, UriKind.Absolute, out uri!) && uri.Scheme is "http" or "https";

    static bool StartsWithSequence(byte[] bytes) => bytes.Length > 2 && bytes[0] == 0x30;

    /// <summary>An OCSPResponse is a sequence that starts with the response status; 0 is "successful".</summary>
    static bool IsSuccessfulOcspResponse(byte[] bytes)
    {
        try
        {
            var sequence = new AsnReader(bytes, AsnEncodingRules.DER).ReadSequence();
            return sequence.ReadEnumeratedBytes().Span is [0];
        }
        catch (AsnContentException)
        {
            return false;
        }
    }

    /// <summary>The DER <c>OCSPRequest</c> for <paramref name="subject"/> (RFC 6960 4.1.1): its <c>CertID</c> names the issuer by the SHA-1 of its name and of its key, as the protocol's default asks.</summary>
    internal static byte[] OcspRequest(X509Certificate2 subject, X509Certificate2 issuer)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())                   // OCSPRequest
        using (writer.PushSequence())                   // tbsRequest
        using (writer.PushSequence())                   // requestList
        using (writer.PushSequence())                   // Request
        using (writer.PushSequence())                   // reqCert: CertID
        {
            using (writer.PushSequence())
            {
                writer.WriteObjectIdentifier("1.3.14.3.2.26");      // SHA-1
                writer.WriteNull();
            }

            writer.WriteOctetString(SHA1.HashData(issuer.SubjectName.RawData));
            writer.WriteOctetString(SHA1.HashData(issuer.PublicKey.EncodedKeyValue.RawData));
            writer.WriteIntegerUnsigned(subject.SerialNumberBytes.Span);
        }

        return writer.Encode();
    }

    /// <summary>The OCSP responder addresses in a certificate's authority information access extension.</summary>
    static IEnumerable<string> OcspAddresses(X509Certificate2 certificate) =>
        certificate.Extensions.OfType<X509AuthorityInformationAccessExtension>().SelectMany(e => e.EnumerateOcspUris());

    /// <summary>The URI addresses in a certificate's CRL distribution points extension (RFC 5280 4.2.1.13).</summary>
    static IEnumerable<string> CrlAddresses(X509Certificate2 certificate)
    {
        var extension = certificate.Extensions[CrlDistributionPoints];
        if (extension is null)
            return [];

        var addresses = new List<string>();
        try
        {
            var points = new AsnReader(extension.RawData, AsnEncodingRules.DER).ReadSequence();
            while (points.HasData)
            {
                var point = points.ReadSequence();                              // DistributionPoint
                while (point.HasData)
                {
                    var tag = point.PeekTag();
                    if (tag != new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)) { point.ReadEncodedValue(); continue; }

                    var name = point.ReadSequence(tag);                         // [0] DistributionPointName
                    while (name.HasData)
                    {
                        var nameTag = name.PeekTag();
                        if (nameTag != new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)) { name.ReadEncodedValue(); continue; }

                        var names = name.ReadSequence(nameTag);                 // [0] fullName: GeneralNames
                        while (names.HasData)
                        {
                            var generalName = names.PeekTag();
                            if (generalName == new Asn1Tag(TagClass.ContextSpecific, 6))     // uniformResourceIdentifier
                                addresses.Add(names.ReadCharacterString(UniversalTagNumber.IA5String, generalName));
                            else
                                names.ReadEncodedValue();
                        }
                    }
                }
            }
        }
        catch (AsnContentException)
        {
            // A malformed extension names nothing usable; what was read before it stays.
        }

        return addresses;
    }
}
