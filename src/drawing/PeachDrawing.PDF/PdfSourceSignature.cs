using System;
using System.Security.Cryptography.X509Certificates;

namespace PeachDrawing.PDF;

/// <summary>
/// A digital signature or document timestamp a PDF carries, and what checking it found. See <see cref="PdfPageSource.GetSignatures"/>.
/// </summary>
/// <remarks>
/// What is checked is the signature itself: that the CMS signature (or the timestamp token) is well formed and matches the bytes of the
/// file its byte range names, and, for a timestamp inside a signature, that the token vouches for that signature. Whether a certificate
/// can be trusted (a chain to a root you accept, revocation, the signing time against the validity period) is not decided here: build an
/// <see cref="X509Chain"/> from <see cref="SignerCertificate"/> or <see cref="TimestampCertificate"/> with the policy you need.
/// </remarks>
public sealed class PdfSourceSignature
{
    /// <summary>The fully qualified name of the signature field.</summary>
    public string FieldName { get; init; } = "";

    /// <summary><see langword="true"/> for a document timestamp (an RFC 3161 token that covers the file), <see langword="false"/> for a signature.</summary>
    public bool IsDocumentTimestamp { get; init; }

    /// <summary>The signature's sub-filter, such as <c>adbe.pkcs7.detached</c>, <c>ETSI.CAdES.detached</c> or <c>ETSI.RFC3161</c>.</summary>
    public string? SubFilter { get; init; }

    /// <summary>The signer's name as the signature states it, if it does.</summary>
    public string? SignerName { get; init; }

    /// <summary>Why the signature was made, as it states.</summary>
    public string? Reason { get; init; }

    /// <summary>Where the signature was made, as it states.</summary>
    public string? Location { get; init; }

    /// <summary>How to reach the signer, as the signature states.</summary>
    public string? ContactInfo { get; init; }

    /// <summary>The time the signature's own <c>/M</c> entry claims. The signer chose it; a timestamp is the time that can be relied on.</summary>
    public DateTimeOffset? ClaimedTime { get; init; }

    /// <summary>
    /// Whether the signature covers the whole file. <see langword="false"/> means bytes follow the signed range: a later signature, a
    /// document timestamp, or other changes, which this does not tell apart.
    /// </summary>
    public bool CoversWholeFile { get; init; }

    /// <summary>
    /// Whether the signature (or timestamp token) is well formed and matches the bytes its byte range names. <see langword="false"/>
    /// means the covered bytes were changed or the signature is damaged; <see cref="Problem"/> says which.
    /// </summary>
    public bool IsIntact { get; init; }

    /// <summary>Why <see cref="IsIntact"/> is <see langword="false"/>, or <see langword="null"/>.</summary>
    public string? Problem { get; init; }

    /// <summary>The certificate that signed, when the signature embeds it.</summary>
    public X509Certificate2? SignerCertificate { get; init; }

    /// <summary>
    /// The time an embedded timestamp token (or, for a document timestamp, the token itself) vouches for, or <see langword="null"/> when
    /// there is none or it could not be read.
    /// </summary>
    public DateTimeOffset? TimestampTime { get; init; }

    /// <summary>
    /// Whether the embedded timestamp token vouches for this signature's value, or <see langword="null"/> when there is none. For a
    /// document timestamp this is <see cref="IsIntact"/>.
    /// </summary>
    public bool? TimestampIsValid { get; init; }

    /// <summary>The certificate that signed the timestamp token, when the token embeds it.</summary>
    public X509Certificate2? TimestampCertificate { get; init; }
}
