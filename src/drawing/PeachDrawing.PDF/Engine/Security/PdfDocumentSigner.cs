using System;
using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Text;
using PeachDrawing.PDF.Engine.AcroForms;
using PeachDrawing.PDF.Engine.Annotations;
using PeachDrawing.PDF.Engine.Internal;

namespace PeachDrawing.PDF.Engine.Security;

/// <summary>
/// Signs a PDF file (ISO 32000-1 §12.8). The signature field is added to the document with a fixed-width <c>/ByteRange</c> and
/// <c>/Contents</c> placeholder, so writing the file gives final offsets; <see cref="Seal"/> then fills both in without moving a byte.
/// </summary>
internal static class PdfDocumentSigner
{
    const string ByteRangePlaceholder = "[0 0000000000 0000000000 0000000000]";
    const string ContentTypeSigningCertificateV2 = "1.2.840.113549.1.9.16.2.47";

    /// <summary>Adds the invisible signature field (and its value dictionary with the placeholders) to the first page and the form.</summary>
    public static PdfSignatureField AddField(PdfDocument document, PdfSigningOptions options)
    {
        var value = new PdfDictionary(document);
        document.Internals.AddObject(value);
        value.Elements.SetName("/Type", "/Sig");
        value.Elements.SetName("/Filter", "/Adobe.PPKLite");
        value.Elements.SetName("/SubFilter", options.Format == PdfSignatureFormat.CadesDetached ? "/ETSI.CAdES.detached" : "/adbe.pkcs7.detached");
        value.Elements["/ByteRange"] = new PdfLiteral(ByteRangePlaceholder);
        value.Elements["/Contents"] = new PdfLiteral("<" + new string('0', options.ReservedSignatureBytes * 2) + ">");
        value.Elements.SetDateTime("/M", PdfEntropy.Now());
        SetText(value, "/Name", options.SignerName);
        SetText(value, "/Reason", options.Reason);
        SetText(value, "/Location", options.Location);
        SetText(value, "/ContactInfo", options.ContactInfo);

        var field = new PdfSignatureField(document)
        {
            PartialFieldName = "Signature1",
            Rectangle = new PdfRectangle(0, 0, 0, 0),
            Flags = (PdfAnnotationFlags)(4 | 128), // print + locked
        };
        field.Elements.SetReference("/V", value);

        document.Pages[0].Annotations.Add(field);
        var form = document.Catalog.AcroForm;
        form.AddField(field);
        form.Elements.SetInteger(PdfAcroForm.Keys.SigFlags, 3); // signatures exist, and the file is only appended to from here
        return field;
    }

    static void SetText(PdfDictionary dictionary, string key, string? text)
    {
        if (!string.IsNullOrEmpty(text))
            dictionary.Elements.SetString(key, text, PdfStringEncoding.Unicode);
    }

    /// <summary>
    /// Signs <paramref name="file"/> in place: patches its <c>/ByteRange</c>, hashes the bytes that range covers and writes the CMS
    /// signature over the <c>/Contents</c> placeholder.
    /// </summary>
    /// <exception cref="InvalidOperationException">the placeholder is not in the file exactly once, or the signature does not fit in it</exception>
    public static void Seal(byte[] file, PdfSigningOptions options)
    {
        int reserved = options.ReservedSignatureBytes * 2;
        var contentsNeedle = Encoding.ASCII.GetBytes("<" + new string('0', reserved) + ">");
        int contentsStart = Single(file, contentsNeedle, "/Contents");
        int contentsEnd = contentsStart + contentsNeedle.Length;
        int rangeStart = Single(file, Encoding.ASCII.GetBytes(ByteRangePlaceholder), "/ByteRange");

        var range = Encoding.ASCII.GetBytes($"[0 {contentsStart:D10} {contentsEnd:D10} {file.Length - contentsEnd:D10}]");
        Buffer.BlockCopy(range, 0, file, rangeStart, range.Length);

        var covered = new byte[file.Length - contentsNeedle.Length];
        Buffer.BlockCopy(file, 0, covered, 0, contentsStart);
        Buffer.BlockCopy(file, contentsEnd, covered, contentsStart, file.Length - contentsEnd);

        var signature = Sign(covered, options);
        if (signature.Length * 2 > reserved)
        {
            throw new InvalidOperationException(
                $"The signature is {signature.Length} bytes but only {options.ReservedSignatureBytes} are reserved; raise PdfSigningOptions.ReservedSignatureBytes.");
        }

        var hex = Encoding.ASCII.GetBytes(Convert.ToHexString(signature));
        Buffer.BlockCopy(hex, 0, file, contentsStart + 1, hex.Length);
    }

    static int Single(byte[] file, byte[] needle, string name)
    {
        int first = file.AsSpan().IndexOf(needle);
        if (first < 0 || file.AsSpan(first + 1).IndexOf(needle) >= 0)
            throw new InvalidOperationException($"The signature placeholder for {name} was not found exactly once in the written file.");
        return first;
    }

    /// <summary>The DER-encoded detached CMS signature of <paramref name="data"/>.</summary>
    internal static byte[] Sign(byte[] data, PdfSigningOptions options)
    {
        var certificate = options.Certificate!;
        var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, certificate)
        {
            DigestAlgorithm = new Oid(DigestOid(options.HashAlgorithm)),
            IncludeOption = System.Security.Cryptography.X509Certificates.X509IncludeOption.EndCertOnly,
        };
        foreach (var extra in options.AdditionalCertificates)
            signer.Certificates.Add(extra);

        if (options.Format == PdfSignatureFormat.CadesDetached)
            signer.SignedAttributes.Add(SigningCertificateV2(certificate.RawDataMemory.Span, options.HashAlgorithm));
        else
            signer.SignedAttributes.Add(new Pkcs9SigningTime(PdfEntropy.UtcNow().UtcDateTime));

        var cms = new SignedCms(new ContentInfo(data), detached: true);
        cms.ComputeSignature(signer);
        return cms.Encode();
    }

    /// <summary>The ESS <c>signing-certificate-v2</c> attribute (RFC 5035) that binds the signature to one certificate.</summary>
    static AsnEncodedData SigningCertificateV2(ReadOnlySpan<byte> certificate, HashAlgorithmName algorithm)
    {
        var hash = HashData(algorithm, certificate);
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())           // SigningCertificateV2
        using (writer.PushSequence())           // certs
        using (writer.PushSequence())           // ESSCertIDv2
        {
            if (algorithm != HashAlgorithmName.SHA256) // SHA-256 is the default and is left out
            {
                using (writer.PushSequence())
                {
                    writer.WriteObjectIdentifier(DigestOid(algorithm));
                    writer.WriteNull();
                }
            }

            writer.WriteOctetString(hash);
        }

        return new AsnEncodedData(new Oid(ContentTypeSigningCertificateV2), writer.Encode());
    }

    static byte[] HashData(HashAlgorithmName algorithm, ReadOnlySpan<byte> data) =>
        algorithm == HashAlgorithmName.SHA384 ? SHA384.HashData(data)
        : algorithm == HashAlgorithmName.SHA512 ? SHA512.HashData(data)
        : SHA256.HashData(data);

    static string DigestOid(HashAlgorithmName algorithm) =>
        algorithm == HashAlgorithmName.SHA384 ? "2.16.840.1.101.3.4.2.2"
        : algorithm == HashAlgorithmName.SHA512 ? "2.16.840.1.101.3.4.2.3"
        : "2.16.840.1.101.3.4.2.1";
}
