using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PeachDrawing.PDF;

/// <summary>The CMS signature format a signed PDF carries.</summary>
public enum PdfSignatureFormat
{
    /// <summary>A detached PKCS#7 signature (<c>adbe.pkcs7.detached</c>) with the signing time as a signed attribute. The widest-supported format.</summary>
    Pkcs7Detached,

    /// <summary>
    /// A detached CAdES signature (<c>ETSI.CAdES.detached</c>, the PAdES baseline form): the signing certificate is bound by a signed
    /// attribute and the signing time is the document's own <c>/M</c> entry.
    /// </summary>
    CadesDetached,
}

/// <summary>
/// Signs the document <see cref="PdfComposer.Save"/> writes with a certificate's private key. The file carries an invisible signature
/// field whose value covers every byte of the file except the signature itself, so any later change to the file breaks it.
/// </summary>
/// <remarks>
/// <para>
/// The signature is written last, once the rest of the file is final: the composer reserves room for it, writes the file, then
/// fills the room in. A signed file therefore must not be changed afterwards (opening and saving it again with another tool
/// invalidates the signature).
/// </para>
/// <para>
/// Only the certificate in <see cref="Certificate"/> (and any in <see cref="AdditionalCertificates"/>) is embedded; the chain is not
/// built or fetched, so a reader needs the issuers in its own trust store, or they are listed in <see cref="AdditionalCertificates"/>.
/// </para>
/// </remarks>
public sealed class PdfSigningOptions
{
    /// <summary>The signing certificate, which must have its private key available. Required.</summary>
    public X509Certificate2? Certificate { get; set; }

    /// <summary>Further certificates to embed beside <see cref="Certificate"/>, such as its intermediate issuers.</summary>
    public IList<X509Certificate2> AdditionalCertificates { get; } = [];

    /// <summary>The digest of the signature: SHA-256 (the default), SHA-384 or SHA-512.</summary>
    public HashAlgorithmName HashAlgorithm { get; set; } = HashAlgorithmName.SHA256;

    /// <summary>The signature format. Defaults to <see cref="PdfSignatureFormat.Pkcs7Detached"/>.</summary>
    public PdfSignatureFormat Format { get; set; } = PdfSignatureFormat.Pkcs7Detached;

    /// <summary>The signer's name as the signature field shows it, or <see langword="null"/> to leave it out.</summary>
    public string? SignerName { get; set; }

    /// <summary>Why the document was signed, or <see langword="null"/> to leave it out.</summary>
    public string? Reason { get; set; }

    /// <summary>Where the document was signed, or <see langword="null"/> to leave it out.</summary>
    public string? Location { get; set; }

    /// <summary>How to reach the signer, or <see langword="null"/> to leave it out.</summary>
    public string? ContactInfo { get; set; }

    /// <summary>
    /// The size of the room reserved for the signature, in bytes: from 2 048 to 1 000 000, 16 384 by default. A signature that does not
    /// fit makes the save fail; raise this when many <see cref="AdditionalCertificates"/> are embedded.
    /// </summary>
    public int ReservedSignatureBytes { get; set; } = 16384;

    internal PdfSigningOptions Clone()
    {
        var copy = new PdfSigningOptions
        {
            Certificate = Certificate,
            HashAlgorithm = HashAlgorithm,
            Format = Format,
            SignerName = SignerName,
            Reason = Reason,
            Location = Location,
            ContactInfo = ContactInfo,
            ReservedSignatureBytes = ReservedSignatureBytes,
        };
        foreach (var certificate in AdditionalCertificates)
            copy.AdditionalCertificates.Add(certificate);
        return copy;
    }

    internal void Validate()
    {
        if (Certificate is null)
            throw new InvalidOperationException("PdfSigningOptions.Certificate is required.");
        if (!Certificate.HasPrivateKey)
            throw new InvalidOperationException("PdfSigningOptions.Certificate has no private key to sign with.");
        if (HashAlgorithm != HashAlgorithmName.SHA256 && HashAlgorithm != HashAlgorithmName.SHA384 && HashAlgorithm != HashAlgorithmName.SHA512)
            throw new InvalidOperationException("PdfSigningOptions.HashAlgorithm must be SHA-256, SHA-384 or SHA-512.");
        if (!Enum.IsDefined(Format))
            throw new InvalidOperationException("PdfSigningOptions.Format is not a defined format.");
        if (ReservedSignatureBytes is < 2048 or > 1_000_000)
            throw new InvalidOperationException("PdfSigningOptions.ReservedSignatureBytes must be from 2048 to 1000000.");
    }
}
