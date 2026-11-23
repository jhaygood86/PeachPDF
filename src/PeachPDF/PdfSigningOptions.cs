using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PeachPDF;

/// <summary>The CMS signature format a signed PDF carries.</summary>
public enum PdfSignatureFormat
{
    /// <summary>A detached PKCS#7 signature (<c>adbe.pkcs7.detached</c>), the widest-supported format.</summary>
    Pkcs7Detached,

    /// <summary>A detached CAdES signature (<c>ETSI.CAdES.detached</c>, the PAdES baseline form).</summary>
    CadesDetached,
}

/// <summary>Where a visible signature is drawn: a rectangle on a page, in points from the page's top-left corner.</summary>
public sealed class PdfSignatureBox
{
    /// <summary>The page the box is on, counted from 1. Defaults to the first page.</summary>
    public int PageNumber { get; set; } = 1;

    /// <summary>The distance of the box's left edge from the page's left edge, in points.</summary>
    public double X { get; set; }

    /// <summary>The distance of the box's top edge from the page's top edge, in points.</summary>
    public double Y { get; set; }

    /// <summary>The box's width in points.</summary>
    public double Width { get; set; }

    /// <summary>The box's height in points.</summary>
    public double Height { get; set; }

    /// <summary>The text shown in the box. When <see langword="null"/> it names the signer and the signing time.</summary>
    public string? Text { get; set; }

    /// <summary>The size of the text in points, from 4 to 72. Defaults to 9.</summary>
    public double FontSize { get; set; } = 9;
}

/// <summary>
/// Signs a generated PDF with a certificate's private key, and optionally timestamps the signature with an RFC 3161 authority.
/// </summary>
/// <remarks>
/// <para>
/// Set it on <see cref="PdfGenerateConfig.Signing"/>. The signature is written last, once the rest of the file is final, so the
/// file must not be changed afterwards. Linearized and encrypted files can be signed. PDF/A files can be signed only with no visible
/// <see cref="Box"/>. A timestamp is fetched over the network, so save such a document with
/// <see cref="PeachPdfDocument.SaveAsync"/>.
/// </para>
/// <para>
/// Signing never prompts. A key that needs a PIN fails with an explanation unless the PIN is given in <see cref="KeyPin"/>.
/// </para>
/// </remarks>
public sealed class PdfSigningOptions
{
    /// <summary>The certificate to sign with. It must have a private key unless <see cref="PrivateKey"/> is given.</summary>
    public X509Certificate2? Certificate { get; set; }

    /// <summary>A private key to sign with instead of the one in <see cref="Certificate"/>, such as a key held by a hardware token.</summary>
    public AsymmetricAlgorithm? PrivateKey { get; set; }

    /// <summary>The PIN of a smart card or token key, supplied so signing does not wait for a person to type it.</summary>
    public string? KeyPin { get; set; }

    /// <summary>Whether the operating system may ask a person for a key's PIN. Defaults to <see langword="false"/>: signing fails instead.</summary>
    public bool AllowKeyPrompt { get; set; }

    /// <summary>Further certificates to embed, such as the issuers that complete the chain.</summary>
    public IList<X509Certificate2> AdditionalCertificates { get; } = [];

    /// <summary>The digest algorithm. Defaults to SHA-256.</summary>
    public HashAlgorithmName HashAlgorithm { get; set; } = HashAlgorithmName.SHA256;

    /// <summary>The signature format. Defaults to <see cref="PdfSignatureFormat.Pkcs7Detached"/>.</summary>
    public PdfSignatureFormat Format { get; set; } = PdfSignatureFormat.Pkcs7Detached;

    /// <summary>The address of an RFC 3161 timestamp authority. When set the signature is timestamped, and the document must be saved with <see cref="PeachPdfDocument.SaveAsync"/>.</summary>
    public Uri? TimestampAuthority { get; set; }

    /// <summary>The client used to reach <see cref="TimestampAuthority"/>, for a proxy or credentials. A default client is used when <see langword="null"/>.</summary>
    public HttpClient? TimestampClient { get; set; }

    /// <summary>Where a visible signature is drawn. When <see langword="null"/> the signature is invisible.</summary>
    public PdfSignatureBox? Box { get; set; }

    /// <summary>The signer's name as shown in a reader. Taken from the certificate when <see langword="null"/>.</summary>
    public string? SignerName { get; set; }

    /// <summary>Why the document is signed.</summary>
    public string? Reason { get; set; }

    /// <summary>Where the document is signed.</summary>
    public string? Location { get; set; }

    /// <summary>How to reach the signer.</summary>
    public string? ContactInfo { get; set; }
}
