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
    /// <summary>
    /// The signing certificate. Required. Its private key must be available to the process, unless <see cref="PrivateKey"/> supplies the
    /// key to sign with (then the certificate only needs to be the public one that goes into the file).
    /// </summary>
    public X509Certificate2? Certificate { get; set; }

    /// <summary>
    /// The key to sign with, instead of the certificate's own private key, or <see langword="null"/> (the default) to use that. This is
    /// the way to sign with a key the process cannot export or open by itself: a key object that already holds its PIN, a wrapper around
    /// a hardware module (derive from <see cref="System.Security.Cryptography.RSA"/> or <see cref="System.Security.Cryptography.ECDsa"/> and
    /// forward the signing call), or a cloud key service. It must be an RSA or ECDSA key that matches <see cref="Certificate"/>, and it is
    /// not disposed: it belongs to the caller.
    /// </summary>
    public AsymmetricAlgorithm? PrivateKey { get; set; }

    /// <summary>
    /// The PIN of the certificate's private key, or <see langword="null"/> (the default). It is handed to the key's provider so that a
    /// smart card or token signs without asking. Only keys held by Windows CNG (<c>RSACng</c>, <c>ECDsaCng</c>) take a PIN this way; on
    /// another platform, or for a key held elsewhere, supply <see cref="PrivateKey"/> instead. It cannot be combined with
    /// <see cref="PrivateKey"/>, and it stays in memory as a string for as long as these options do.
    /// </summary>
    public string? KeyPin { get; set; }

    /// <summary>
    /// Whether signing may stop to ask the user for something the key needs (a PIN dialog, a smart card prompt). <see langword="false"/>
    /// (the default) makes a key that needs interaction fail the save with an exception, so a service with no one to answer never blocks;
    /// set it to <see langword="true"/> in a desktop application that wants the prompt.
    /// </summary>
    public bool AllowKeyPrompt { get; set; }

    /// <summary>Further certificates to embed beside <see cref="Certificate"/>, such as its intermediate issuers.</summary>
    public IList<X509Certificate2> AdditionalCertificates { get; } = [];

    /// <summary>The digest of the signature: SHA-256 (the default), SHA-384 or SHA-512.</summary>
    public HashAlgorithmName HashAlgorithm { get; set; } = HashAlgorithmName.SHA256;

    /// <summary>The signature format. Defaults to <see cref="PdfSignatureFormat.Pkcs7Detached"/>.</summary>
    public PdfSignatureFormat Format { get; set; } = PdfSignatureFormat.Pkcs7Detached;

    /// <summary>
    /// A time stamping authority to have vouch for the signing time, or <see langword="null"/> (the default) for none. A document
    /// that has one must be saved with <see cref="PdfComposer.SaveAsync"/>.
    /// </summary>
    public PdfTimestampOptions? Timestamp { get; set; }

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
            PrivateKey = PrivateKey,
            KeyPin = KeyPin,
            AllowKeyPrompt = AllowKeyPrompt,
            HashAlgorithm = HashAlgorithm,
            Format = Format,
            SignerName = SignerName,
            Reason = Reason,
            Location = Location,
            ContactInfo = ContactInfo,
            ReservedSignatureBytes = ReservedSignatureBytes,
            Timestamp = Timestamp?.Clone(),
        };
        foreach (var certificate in AdditionalCertificates)
            copy.AdditionalCertificates.Add(certificate);
        return copy;
    }

    internal void Validate()
    {
        if (Certificate is null)
            throw new InvalidOperationException("PdfSigningOptions.Certificate is required.");
        if (PrivateKey is not null && PrivateKey is not (RSA or ECDsa))
            throw new InvalidOperationException("PdfSigningOptions.PrivateKey must be an RSA or ECDSA key.");
        if (PrivateKey is not null && KeyPin is not null)
            throw new InvalidOperationException("Give PdfSigningOptions either a PrivateKey (which holds its own PIN) or a KeyPin, not both.");
        if (KeyPin is not null && !OperatingSystem.IsWindows())
            throw new InvalidOperationException("PdfSigningOptions.KeyPin works only for keys held by Windows CNG; supply a PrivateKey that holds its PIN instead.");
        if (PrivateKey is null && !Certificate.HasPrivateKey)
            throw new InvalidOperationException("PdfSigningOptions.Certificate has no private key to sign with; supply PdfSigningOptions.PrivateKey or a certificate that has one.");
        if (HashAlgorithm != HashAlgorithmName.SHA256 && HashAlgorithm != HashAlgorithmName.SHA384 && HashAlgorithm != HashAlgorithmName.SHA512)
            throw new InvalidOperationException("PdfSigningOptions.HashAlgorithm must be SHA-256, SHA-384 or SHA-512.");
        if (!Enum.IsDefined(Format))
            throw new InvalidOperationException("PdfSigningOptions.Format is not a defined format.");
        Timestamp?.Validate();
        if (ReservedSignatureBytes is < 2048 or > 1_000_000)
            throw new InvalidOperationException("PdfSigningOptions.ReservedSignatureBytes must be from 2048 to 1000000.");
    }
}
