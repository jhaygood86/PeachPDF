using System;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PeachDrawing.PDF.Engine.AcroForms;
using PeachDrawing.PDF.Engine.Advanced;
using PeachDrawing.PDF.Engine.Annotations;
using PeachDrawing.PDF.Engine.Internal;

namespace PeachDrawing.PDF.Engine.Security;

/// <summary>
/// Signs a PDF file (ISO 32000-1 §12.8). The signature field is added to the document with a fixed-width <c>/ByteRange</c> and
/// <c>/Contents</c> placeholder, so writing the file gives final offsets; <see cref="Seal"/> then fills both in without moving a byte.
/// </summary>
internal static class PdfDocumentSigner
{
    internal const string ByteRangePlaceholder = "[0 0000000000 0000000000 0000000000]";
    const string ContentTypeSigningCertificateV2 = "1.2.840.113549.1.9.16.2.47";
    const string SignatureTimeStampToken = "1.2.840.113549.1.9.16.2.14";

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

        var form = document.Catalog.AcroForm;
        var field = new PdfSignatureField(document)
        {
            PartialFieldName = FreeFieldName(form),
            Rectangle = new PdfRectangle(0, 0, 0, 0),
            Flags = (PdfAnnotationFlags)(4 | 128), // print + locked
        };
        field.Elements.SetReference("/V", value);

        document.Pages[0].Annotations.Add(field);
        form.AddField(field);
        form.Elements.SetInteger(PdfAcroForm.Keys.SigFlags, 3); // signatures exist, and the file is only appended to from here
        return field;
    }

    /// <summary>The first "SignatureN" that no field already in the form (a form control the document drew, say) has.</summary>
    static string FreeFieldName(PdfAcroForm form)
    {
        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in form.Fields.Elements)
        {
            if (item is PdfReference { Value: PdfDictionary field })
                taken.Add(field.Elements.GetString("/T"));
        }

        for (int n = 1; ; n++)
        {
            var name = "Signature" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!taken.Contains(name))
                return name;
        }
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
        var (covered, contentsStart) = PatchByteRange(file, options.ReservedSignatureBytes);
        Write(file, contentsStart, options.ReservedSignatureBytes, BuildCms(covered, options).Encode());
    }

    /// <summary>As <see cref="Seal"/>, and embeds the time stamping authority's token when the options ask for one.</summary>
    public static async Task SealAsync(byte[] file, PdfSigningOptions options, CancellationToken cancellationToken)
    {
        var (covered, contentsStart) = PatchByteRange(file, options.ReservedSignatureBytes);
        var cms = BuildCms(covered, options);
        if (options.Timestamp is { } timestamp)
            await AddTimestampAsync(cms, timestamp, cancellationToken).ConfigureAwait(false);
        Write(file, contentsStart, options.ReservedSignatureBytes, cms.Encode());
    }

    /// <summary>
    /// Fills a document timestamp's placeholders (RFC 3161 as a PDF signature, <c>ETSI.RFC3161</c>): the authority timestamps the digest of the
    /// bytes the <c>/ByteRange</c> covers, and the token itself is the <c>/Contents</c>.
    /// </summary>
    public static async Task SealTimestampAsync(byte[] file, PdfTimestampOptions options, int reservedBytes, CancellationToken cancellationToken)
    {
        var (covered, contentsStart) = PatchByteRange(file, reservedBytes);
        var request = Rfc3161TimestampRequest.CreateFromData(covered, options.HashAlgorithm, nonce: PdfEntropy.GetBytes(8), requestSignerCertificates: true);
        var response = await options.Exchange!(request.Encode(), cancellationToken).ConfigureAwait(false);

        Rfc3161TimestampToken token;
        try
        {
            token = request.ProcessResponse(response, out _);
        }
        catch (CryptographicException error)
        {
            throw new InvalidOperationException("The time stamping authority's response was refused: " + error.Message, error);
        }

        Write(file, contentsStart, reservedBytes, token.AsSignedCms().Encode());
    }

    /// <summary>The text of a signature value dictionary with its placeholders, for a signature appended to an existing file.</summary>
    public static string SignatureValueText(PdfSigningOptions options)
    {
        var text = new StringBuilder("<< /Type /Sig /Filter /Adobe.PPKLite /SubFilter ");
        text.Append(options.Format == PdfSignatureFormat.CadesDetached ? "/ETSI.CAdES.detached" : "/adbe.pkcs7.detached");
        text.Append(" /ByteRange ").Append(ByteRangePlaceholder);
        text.Append(" /Contents <").Append('0', options.ReservedSignatureBytes * 2).Append('>');
        text.Append(" /M (D:").Append(PdfEntropy.UtcNow().ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture)).Append("Z)");
        AppendText(text, "/Name", options.SignerName);
        AppendText(text, "/Reason", options.Reason);
        AppendText(text, "/Location", options.Location);
        AppendText(text, "/ContactInfo", options.ContactInfo);
        return text.Append(" >>").ToString();
    }

    /// <summary>The text of a document timestamp's value dictionary with its placeholders.</summary>
    public static string TimestampValueText(int reservedBytes) =>
        "<< /Type /DocTimeStamp /Filter /Adobe.PPKLite /SubFilter /ETSI.RFC3161 /ByteRange " + ByteRangePlaceholder
        + " /Contents <" + new string('0', reservedBytes * 2) + "> >>";

    static void AppendText(StringBuilder text, string key, string? value)
    {
        if (!string.IsNullOrEmpty(value))
            text.Append(' ').Append(key).Append(" <FEFF").Append(Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(value))).Append('>');
    }

    static (byte[] Covered, int ContentsStart) PatchByteRange(byte[] file, int reservedBytes)
    {
        int reserved = reservedBytes * 2;
        var contentsNeedle = Encoding.ASCII.GetBytes("<" + new string('0', reserved) + ">");
        int contentsStart = Single(file, contentsNeedle, "/Contents");
        int contentsEnd = contentsStart + contentsNeedle.Length;
        int rangeStart = Single(file, Encoding.ASCII.GetBytes(ByteRangePlaceholder), "/ByteRange");

        var range = Encoding.ASCII.GetBytes($"[0 {contentsStart:D10} {contentsEnd:D10} {file.Length - contentsEnd:D10}]");
        Buffer.BlockCopy(range, 0, file, rangeStart, range.Length);

        var covered = new byte[file.Length - contentsNeedle.Length];
        Buffer.BlockCopy(file, 0, covered, 0, contentsStart);
        Buffer.BlockCopy(file, contentsEnd, covered, contentsStart, file.Length - contentsEnd);
        return (covered, contentsStart);
    }

    static void Write(byte[] file, int contentsStart, int reservedBytes, byte[] signature)
    {
        if (signature.Length > reservedBytes)
        {
            throw new InvalidOperationException(
                $"The signature is {signature.Length} bytes but only {reservedBytes} are reserved; raise the reserved size (PdfSigningOptions.ReservedSignatureBytes).");
        }

        var hex = Encoding.ASCII.GetBytes(Convert.ToHexString(signature));
        Buffer.BlockCopy(hex, 0, file, contentsStart + 1, hex.Length);
    }

    /// <summary>
    /// Asks the authority to timestamp the signature value (RFC 3161) and adds its token as the <c>signature-time-stamp-token</c>
    /// unsigned attribute (RFC 3161 Appendix A), which a signature's own digest does not cover.
    /// </summary>
    static async Task AddTimestampAsync(SignedCms cms, PdfTimestampOptions options, CancellationToken cancellationToken)
    {
        var signer = cms.SignerInfos[0];
        var request = Rfc3161TimestampRequest.CreateFromData(signer.GetSignature(), options.HashAlgorithm, nonce: PdfEntropy.GetBytes(8), requestSignerCertificates: true);
        var response = await options.Exchange!(request.Encode(), cancellationToken).ConfigureAwait(false);

        Rfc3161TimestampToken token;
        try
        {
            token = request.ProcessResponse(response, out _);
        }
        catch (CryptographicException error)
        {
            throw new InvalidOperationException("The time stamping authority's response was refused: " + error.Message, error);
        }

        signer.AddUnsignedAttribute(new AsnEncodedData(new Oid(SignatureTimeStampToken), token.AsSignedCms().Encode()));
    }

    static int Single(byte[] file, byte[] needle, string name)
    {
        int first = file.AsSpan().IndexOf(needle);
        if (first < 0 || file.AsSpan(first + 1).IndexOf(needle) >= 0)
            throw new InvalidOperationException($"The signature placeholder for {name} was not found exactly once in the written file.");
        return first;
    }

    /// <summary>The detached CMS signature of <paramref name="data"/>.</summary>
    internal static SignedCms BuildCms(byte[] data, PdfSigningOptions options)
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
        return cms;
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
