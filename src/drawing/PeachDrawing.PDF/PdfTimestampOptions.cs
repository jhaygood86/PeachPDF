using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace PeachDrawing.PDF;

/// <summary>
/// Has a time stamping authority (RFC 3161) vouch for when a signature was made. The authority's token is embedded in the signature,
/// so a reader can show that the document was signed by a time the signer cannot have chosen, and the signature can outlive the
/// signing certificate's validity.
/// </summary>
/// <remarks>
/// The package makes no network request of its own: it builds the request, hands its bytes to <see cref="Exchange"/>, and checks the
/// answer. <see cref="FromHttpAuthority"/> is the usual exchange; a host with its own transport (authentication, a proxy, a message
/// queue) supplies its own. A document that is timestamped must be saved with <see cref="PdfComposer.SaveAsync"/>, because the
/// exchange is asynchronous. The token adds several kilobytes to the signature (more when the authority embeds its certificates), so
/// raise <see cref="PdfSigningOptions.ReservedSignatureBytes"/> if a save reports it does not fit.
/// </remarks>
public sealed class PdfTimestampOptions
{
    /// <summary>
    /// Sends one DER-encoded time-stamp request to the authority and returns its DER-encoded response. Required.
    /// </summary>
    public Func<ReadOnlyMemory<byte>, CancellationToken, Task<byte[]>>? Exchange { get; set; }

    /// <summary>The digest the request asks the authority to timestamp: SHA-256 (the default), SHA-384 or SHA-512.</summary>
    public HashAlgorithmName HashAlgorithm { get; set; } = HashAlgorithmName.SHA256;

    /// <summary>Makes options whose exchange posts the request to an authority over HTTP (<c>application/timestamp-query</c>).</summary>
    /// <param name="authority">the authority's address</param>
    /// <param name="client">the client to send with, so the caller controls credentials, proxies and timeouts; it is not disposed</param>
    /// <returns>options that use the authority</returns>
    public static PdfTimestampOptions FromHttpAuthority(Uri authority, HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(client);

        return new PdfTimestampOptions
        {
            Exchange = async (request, cancellation) =>
            {
                using var content = new ByteArrayContent(request.ToArray());
                content.Headers.ContentType = new MediaTypeHeaderValue("application/timestamp-query");
                using var response = await client.PostAsync(authority, content, cancellation).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsByteArrayAsync(cancellation).ConfigureAwait(false);
            },
        };
    }

    internal PdfTimestampOptions Clone() => new() { Exchange = Exchange, HashAlgorithm = HashAlgorithm };

    internal void Validate()
    {
        if (Exchange is null)
            throw new InvalidOperationException("PdfTimestampOptions.Exchange is required.");
        if (HashAlgorithm != HashAlgorithmName.SHA256 && HashAlgorithm != HashAlgorithmName.SHA384 && HashAlgorithm != HashAlgorithmName.SHA512)
            throw new InvalidOperationException("PdfTimestampOptions.HashAlgorithm must be SHA-256, SHA-384 or SHA-512.");
    }
}
