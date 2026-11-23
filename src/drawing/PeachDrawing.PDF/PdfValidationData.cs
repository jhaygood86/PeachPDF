using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using PeachDrawing.PDF.Engine.Security;

namespace PeachDrawing.PDF;

/// <summary>
/// What a reader needs to check a signature long after it was made, with no network and after the certificates have expired: the
/// certificates of the chain, and the proof that none was revoked (OCSP responses and certificate revocation lists). A file carries it in
/// its document security store; see <see cref="PdfFileSigner.AddValidationData"/> and <see cref="PdfPageSource.GetValidationData"/>.
/// </summary>
/// <remarks>
/// Adding this to a signed file, and then a document timestamp over it (<see cref="PdfFileSigner.AddDocumentTimestampAsync"/>), is what the
/// PAdES long-term profiles (B-LT and B-LTA) ask for. The data is stored as it is given: nothing here checks that a response is genuine or
/// current, so collect it close to the time of signing.
/// </remarks>
public sealed class PdfValidationData
{
    private readonly List<string> _problems = [];

    /// <summary>The DER-encoded certificates: the signer's and its issuers up to the root.</summary>
    public IList<byte[]> Certificates { get; } = [];

    /// <summary>The DER-encoded OCSP responses (RFC 6960 <c>OCSPResponse</c>), each saying whether a certificate was revoked at the time.</summary>
    public IList<byte[]> OcspResponses { get; } = [];

    /// <summary>The DER-encoded certificate revocation lists (RFC 5280 <c>CertificateList</c>).</summary>
    public IList<byte[]> Crls { get; } = [];

    /// <summary>What <see cref="CollectAsync"/> could not get (an unreachable responder, a refused request, an address it will not use); empty for data made by hand.</summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>Whether there is nothing in it.</summary>
    public bool IsEmpty => Certificates.Count == 0 && OcspResponses.Count == 0 && Crls.Count == 0;

    internal void AddProblem(string problem) => _problems.Add(problem);

    /// <summary>
    /// Collects the validation data for a certificate: its chain, the revocation lists its certificates name and an OCSP response from each
    /// responder they name, fetched over HTTP. What cannot be fetched is listed in <see cref="Problems"/> instead of failing the call, so a
    /// responder that is down leaves the other data usable.
    /// </summary>
    /// <param name="certificate">the certificate that signs</param>
    /// <param name="additionalCertificates">intermediate certificates the chain needs that the certificate does not carry, or <see langword="null"/></param>
    /// <param name="client">the client to fetch with, so the caller controls proxies, credentials and timeouts; it is not disposed</param>
    /// <param name="cancellationToken">cancels the requests</param>
    /// <returns>the data found</returns>
    /// <remarks>Only <c>http</c> and <c>https</c> addresses are used. The chain is built without trusting anything: a certificate that chains to a root the machine does not know is still collected.</remarks>
    public static Task<PdfValidationData> CollectAsync(X509Certificate2 certificate, IEnumerable<X509Certificate2>? additionalCertificates,
        HttpClient client, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        ArgumentNullException.ThrowIfNull(client);
        return PdfValidationCollector.CollectAsync(certificate, additionalCertificates, client, cancellationToken);
    }
}
