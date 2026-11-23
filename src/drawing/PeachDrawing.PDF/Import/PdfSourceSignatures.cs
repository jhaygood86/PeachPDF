using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using PeachDrawing.PDF.Engine;

namespace PeachDrawing.PDF.Import;

/// <summary>
/// Reads the signatures and document timestamps of a <see cref="PdfSourceDocument"/> and checks each against the file's own bytes. The
/// signature and its byte range are read from the file as it was written, not through the object model: a signature's
/// <c>/Contents</c> is never encrypted, and the bytes the range covers are what the signature was computed over.
/// </summary>
internal sealed class PdfSourceSignatures(PdfSourceDocument doc)
{
    const string SignatureTimeStampToken = "1.2.840.113549.1.9.16.2.14";

    /// <summary>The most signatures read from one file: each is checked against its own copy of the covered bytes, so a hostile file listing thousands would cost quadratic time and memory.</summary>
    internal const int MaxSignatures = 256;

    public IReadOnlyList<PdfSourceSignature> Read()
    {
        var found = new List<PdfSourceSignature>();
        foreach (var (name, value) in Enumerate())
        {
            if (found.Count >= MaxSignatures) break;
            found.Add(Check(name, value));
        }

        return found;
    }

    /// <summary>
    /// The signatures and timestamps of the file with the bytes of each one's <c>/Contents</c>, as the file stores them (zero padding
    /// included): what a validation-data entry is keyed by.
    /// </summary>
    public IReadOnlyList<(string FieldName, bool IsTimestamp, byte[] Contents)> ReadIdentities()
    {
        var found = new List<(string, bool, byte[])>();
        foreach (var (name, value) in Enumerate())
        {
            if (found.Count >= MaxSignatures) break;
            if (ValidRange(value) is not { } range) continue;

            try
            {
                var hex = new string(Encoding.ASCII.GetString(doc.Data, (int)range[1] + 1, (int)(range[2] - range[1] - 2)).Where(c => !char.IsWhiteSpace(c)).ToArray());
                var subFilter = doc.NameOf(value.GetRaw("SubFilter"))?.TrimStart('/');
                found.Add((name, doc.NameOf(value.GetRaw("Type")) == "/DocTimeStamp" || subFilter == "ETSI.RFC3161", Convert.FromHexString(hex)));
            }
            catch (FormatException)
            {
                // A /Contents that is not hexadecimal has no identity to key anything by.
            }
        }

        return found;
    }

    /// <summary>The signature fields of the form that hold a value, in the order the form lists them, with their fully qualified names.</summary>
    IEnumerable<(string Name, PdfDictionary Value)> Enumerate()
    {
        if (doc.Resolve(doc.Trailer.GetRaw("Root")) is not PdfDictionary root || doc.DictOf(root, "AcroForm") is not { } form)
            yield break;

        var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<(PdfDictionary Field, string Prefix, string? Type)>();
        if (doc.ArrayOf(form, "Fields") is { } roots)
        {
            // Pushed in reverse so the fields come off in the order the form lists them.
            foreach (var item in roots.Elements.Reverse())
                if (doc.Resolve(item) is PdfDictionary field) pending.Push((field, "", null));
        }

        for (int budget = 100_000; pending.Count > 0 && budget > 0; budget--)
        {
            var (field, prefix, inheritedType) = pending.Pop();
            var name = doc.Resolve(field.GetRaw("T")) is PdfString partial && partial.Value.Length > 0
                ? (prefix.Length == 0 ? "" : prefix + ".") + PdfSourceNavigation.TextOf(partial.Value)
                : prefix;
            var type = doc.NameOf(field.GetRaw("FT")) ?? inheritedType;

            if (type == "/Sig" && doc.Resolve(field.GetRaw("V")) is PdfDictionary value && seen.Add(value))
                yield return (name, value);

            if (doc.ArrayOf(field, "Kids") is { } kids)
            {
                foreach (var kid in kids.Elements.Reverse())
                    if (doc.Resolve(kid) is PdfDictionary child) pending.Push((child, name, type));
            }
        }
    }

    /// <summary>The certificates, OCSP responses and CRLs in the document security store (<c>/DSS</c>).</summary>
    public PdfValidationData ReadValidationData()
    {
        var found = new PdfValidationData();
        if (doc.Resolve(doc.Trailer.GetRaw("Root")) is not PdfDictionary root || doc.DictOf(root, "DSS") is not { } store)
            return found;

        void Read(string key, IList<byte[]> into)
        {
            if (doc.ArrayOf(store, key) is not { } array) return;
            for (int i = 0; i < array.Elements.Count && i < MaxStoreEntries; i++)
            {
                if (doc.Resolve(array.Elements[i]) is PdfDictionary { Stream: not null } stream)
                    into.Add(doc.DecodeStream(stream).Data);
            }
        }

        Read("Certs", found.Certificates);
        Read("OCSPs", found.OcspResponses);
        Read("CRLs", found.Crls);
        return found;
    }

    /// <summary>The most entries read from one list of the security store: a hostile file could name millions.</summary>
    const int MaxStoreEntries = 10_000;

    PdfSourceSignature Check(string fieldName, PdfDictionary value)
    {
        var subFilter = doc.NameOf(value.GetRaw("SubFilter"))?.TrimStart('/');
        bool timestamp = doc.NameOf(value.GetRaw("Type")) == "/DocTimeStamp" || subFilter == "ETSI.RFC3161";

        string? Text(string key) => doc.Resolve(value.GetRaw(key)) is PdfString s ? PdfSourceNavigation.TextOf(s.Value) : null;

        // Where the signed range ends, once it is known to fit the file: what a signature that does not reach the end of the file is compared at.
        long signedEnd = -1;
        PdfSourceSignature Result(bool covers, bool intact, string? problem, X509Certificate2? signer = null, DateTimeOffset? time = null,
            bool? timeValid = null, X509Certificate2? timeCertificate = null) => new()
        {
            FieldName = fieldName,
            IsDocumentTimestamp = timestamp,
            SubFilter = subFilter,
            SignerName = Text("Name"),
            Reason = Text("Reason"),
            Location = Text("Location"),
            ContactInfo = Text("ContactInfo"),
            ClaimedTime = ParseDate(Text("M")),
            CoversWholeFile = covers,
            ChangesAfter = covers ? PdfSignatureChanges.None
                : intact && signedEnd > 0 ? PdfRevisionComparer.Compare(doc, signedEnd)
                : PdfSignatureChanges.Unknown,
            IsIntact = intact,
            Problem = problem,
            SignerCertificate = signer,
            TimestampTime = time,
            TimestampIsValid = timeValid,
            TimestampCertificate = timeCertificate,
        };

        // The byte range: [0 a b c], so that the signed bytes are 0..a and b..b+c, with the signature between a and b.
        var data = doc.Data;
        if (ValidRange(value) is not { } range)
            return Result(false, false, "The signature's /ByteRange is missing or does not match the file.");

        bool covers = range[2] + range[3] == data.Length;
        signedEnd = range[2] + range[3];
        var covered = new byte[range[1] + range[3]];
        Buffer.BlockCopy(data, 0, covered, 0, (int)range[1]);
        Buffer.BlockCopy(data, (int)range[2], covered, (int)range[1], (int)range[3]);

        byte[] contents, hole;
        try
        {
            // PDF allows white space between the hexadecimal digits.
            var hex = new string(Encoding.ASCII.GetString(data, (int)range[1] + 1, (int)(range[2] - range[1] - 2)).Where(c => !char.IsWhiteSpace(c)).ToArray());
            hole = Convert.FromHexString(hex);
            contents = TrimStructure(hole);
        }
        catch (Exception error) when (error is FormatException or ArgumentException or InvalidOperationException)
        {
            return Result(covers, false, "The signature's /Contents is not readable.");
        }

        // What is checked is the part of the file the range leaves out, so it must be the signature the dictionary itself declares: a reader
        // that takes /Contents from the dictionary would otherwise check something other than what was verified here. An encrypted file's
        // strings are decrypted when read, which /Contents never is, so this comparison cannot be made there.
        if (!doc.IsEncrypted && (doc.Resolve(value.GetRaw("Contents")) is not PdfString declared || !Encoding.Latin1.GetBytes(declared.Value).AsSpan().SequenceEqual(hole)))
            return Result(covers, false, "The signature's /Contents is not the part of the file its /ByteRange leaves out.");

        return timestamp ? CheckTimestamp(covered, contents, covers, Result) : CheckSignature(covered, contents, covers, Result);
    }

    /// <summary>
    /// The signature's byte range as four numbers, if it is one that fits the file: starting at 0, leaving out a <c>&lt;...&gt;</c> of at
    /// least two bytes, ending inside the file.
    /// </summary>
    long[]? ValidRange(PdfDictionary value)
    {
        if (doc.ArrayOf(value, "ByteRange") is not { Elements.Count: 4 } rangeArray)
            return null;
        var data = doc.Data;

        // Every entry must be a whole number inside the file before any arithmetic is done on it: a real such as 9.3e18 would saturate a
        // cast to long and make the sums below wrap around.
        var numbers = rangeArray.Elements.Select(e => doc.NumberOf(e, double.NaN)).ToArray();
        if (numbers.Any(n => !double.IsFinite(n) || n < 0 || n > data.Length || n != Math.Floor(n)))
            return null;
        var range = numbers.Select(n => (long)n).ToArray();
        if (range[0] != 0 || range[2] < range[1] + 2 || range[2] > data.Length || range[3] > data.Length - range[2]
            || data[range[1]] != (byte)'<' || data[range[2] - 1] != (byte)'>')
            return null;
        return range;
    }

    static PdfSourceSignature CheckSignature(byte[] covered, byte[] contents, bool covers,
        Func<bool, bool, string?, X509Certificate2?, DateTimeOffset?, bool?, X509Certificate2?, PdfSourceSignature> result)
    {
        SignedCms cms;
        try
        {
            cms = new SignedCms(new ContentInfo(covered), detached: true);
            cms.Decode(contents);
        }
        catch (CryptographicException error)
        {
            return result(covers, false, "The signature is not a readable CMS structure: " + error.Message, null, null, null, null);
        }

        var signer = cms.SignerInfos.Count == 1 ? cms.SignerInfos[0] : null;
        X509Certificate2? certificate = null;
        DateTimeOffset? time = null;
        bool? timeValid = null;
        X509Certificate2? timeCertificate = null;
        string? problem = null;

        // Reading the signer's certificate and the timestamp attributes can throw for a damaged signature, which is a finding, not a failure.
        try
        {
            certificate = signer?.Certificate;
            cms.CheckSignature(verifySignatureOnly: true);
        }
        catch (CryptographicException error)
        {
            problem = error.Message;
        }

        try
        {
            if (signer is not null)
            {
                foreach (var attribute in signer.UnsignedAttributes.Cast<CryptographicAttributeObject>().Where(a => a.Oid?.Value == SignatureTimeStampToken))
                {
                    foreach (var value in attribute.Values)
                    {
                        if (!Rfc3161TimestampToken.TryDecode(value.RawData, out var token, out _))
                        {
                            timeValid = false;
                            continue;
                        }

                        time = token.TokenInfo.Timestamp;
                        timeValid = token.VerifySignatureForSignerInfo(signer, out timeCertificate);
                    }
                }
            }
        }
        catch (CryptographicException)
        {
            timeValid = false;
        }

        return result(covers, problem is null, problem, certificate, time, timeValid, timeCertificate);
    }

    static PdfSourceSignature CheckTimestamp(byte[] covered, byte[] contents, bool covers,
        Func<bool, bool, string?, X509Certificate2?, DateTimeOffset?, bool?, X509Certificate2?, PdfSourceSignature> result)
    {
        if (!Rfc3161TimestampToken.TryDecode(contents, out var token, out _))
            return result(covers, false, "The document timestamp is not a readable RFC 3161 token.", null, null, false, null);

        var cms = token.AsSignedCms();
        X509Certificate2? certificate = null;
        try
        {
            certificate = cms.SignerInfos.Count == 1 ? cms.SignerInfos[0].Certificate : null;
        }
        catch (CryptographicException)
        {
            // A damaged token may name a certificate it does not hold; the check below reports what is wrong.
        }

        var algorithm = HashFor(token.TokenInfo.HashAlgorithmId.Value);
        if (algorithm is null)
            return result(covers, false, "The document timestamp uses a digest this does not know.", null, token.TokenInfo.Timestamp, false, certificate);

        string? problem = null;
        if (!HashData(algorithm.Value, covered).AsSpan().SequenceEqual(token.TokenInfo.GetMessageHash().Span))
            problem = "The document timestamp's digest does not match the bytes it covers.";
        else
        {
            try
            {
                cms.CheckSignature(verifySignatureOnly: true);
            }
            catch (CryptographicException error)
            {
                problem = error.Message;
            }
        }

        return result(covers, problem is null, problem, null, token.TokenInfo.Timestamp, problem is null, certificate);
    }

    static HashAlgorithmName? HashFor(string? oid) => oid switch
    {
        "2.16.840.1.101.3.4.2.1" => HashAlgorithmName.SHA256,
        "2.16.840.1.101.3.4.2.2" => HashAlgorithmName.SHA384,
        "2.16.840.1.101.3.4.2.3" => HashAlgorithmName.SHA512,
        _ => null,
    };

    static byte[] HashData(HashAlgorithmName algorithm, byte[] data) =>
        algorithm == HashAlgorithmName.SHA384 ? SHA384.HashData(data)
        : algorithm == HashAlgorithmName.SHA512 ? SHA512.HashData(data)
        : SHA256.HashData(data);

    /// <summary>
    /// The structure at the start of a zero-padded <c>/Contents</c>: its own length says where it ends. Producers other than this package
    /// may write BER, where a length can be indefinite (<c>80</c>, closed by <c>00 00</c>), so the structure is walked rather than read as one length.
    /// </summary>
    static byte[] TrimStructure(byte[] padded)
    {
        if (padded.Length < 2 || padded[0] != 0x30)
            throw new FormatException("Not a sequence.");
        return padded[..ElementEnd(padded, 0, 0)];
    }

    /// <summary>The offset just past the BER element that starts at <paramref name="start"/>.</summary>
    static int ElementEnd(byte[] data, int start, int depth)
    {
        if (depth > 64 || start + 2 > data.Length)
            throw new FormatException("Bad BER structure.");
        byte tag = data[start];
        if ((tag & 0x1F) == 0x1F)
            throw new FormatException("High tag numbers are not used here.");

        int at = start + 1;
        byte first = data[at++];
        if (first < 0x80)
            return Checked(data, at, first);

        if (first == 0x80)
        {
            // Indefinite length: the children follow until a pair of zero bytes. Only a constructed element may use it.
            if ((tag & 0x20) == 0)
                throw new FormatException("A primitive element cannot have an indefinite length.");
            while (true)
            {
                if (at + 2 > data.Length)
                    throw new FormatException("Unterminated indefinite length.");
                if (data[at] == 0 && data[at + 1] == 0)
                    return at + 2;
                at = ElementEnd(data, at, depth + 1);
            }
        }

        int lengthBytes = first & 0x7F;
        if (lengthBytes > 4 || at + lengthBytes > data.Length)
            throw new FormatException("Bad BER length.");
        long length = 0;
        for (int i = 0; i < lengthBytes; i++)
            length = (length << 8) | data[at++];
        return Checked(data, at, length);
    }

    static int Checked(byte[] data, int at, long length)
    {
        if (length < 0 || at + length > data.Length)
            throw new FormatException("The element's length exceeds the contents.");
        return (int)(at + length);
    }

    /// <summary>A PDF date string, <c>D:YYYYMMDDHHmmSSOHH'mm</c>, with every part after the year optional.</summary>
    static DateTimeOffset? ParseDate(string? text)
    {
        if (text is null) return null;
        text = text.StartsWith("D:", StringComparison.Ordinal) ? text[2..] : text;
        if (text.Length < 4 || !text.Take(4).All(char.IsAsciiDigit)) return null;

        int Part(int start, int length, int fallback) =>
            text.Length >= start + length && text.Skip(start).Take(length).All(char.IsAsciiDigit)
                ? int.Parse(text.AsSpan(start, length), CultureInfo.InvariantCulture)
                : fallback;

        try
        {
            var local = new DateTime(Part(0, 4, 1), Part(4, 2, 1), Part(6, 2, 1), Part(8, 2, 0), Part(10, 2, 0), Part(12, 2, 0), DateTimeKind.Unspecified);
            var offset = TimeSpan.Zero;
            if (text.Length > 14 && text[14] is '+' or '-')
            {
                var sign = text[14] == '-' ? -1 : 1;
                var digits = new string(text[15..].Where(char.IsAsciiDigit).ToArray());
                var hours = digits.Length >= 2 ? int.Parse(digits.AsSpan(0, 2), CultureInfo.InvariantCulture) : 0;
                var minutes = digits.Length >= 4 ? int.Parse(digits.AsSpan(2, 2), CultureInfo.InvariantCulture) : 0;
                offset = sign * new TimeSpan(hours, minutes, 0);
            }

            return new DateTimeOffset(local, offset);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
