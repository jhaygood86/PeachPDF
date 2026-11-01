using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Engine.Security;

namespace PeachDrawing.PDF.Import;

/// <summary>
/// The Standard security handler of a PDF, opened with the empty user password (ISO 32000-1 7.6, ISO 32000-2 7.6.4), for the ciphers
/// .NET provides itself: AES-128 (version 4, revision 4) and AES-256 (version 5, revisions 5 and 6), with the crypt filters of those
/// files. RC4 is not supported - .NET has no RC4, and this library does not carry cryptography of its own - so a file encrypted with
/// it (versions 1 and 2, or an RC4 crypt filter) is refused, as is a handler this class does not know.
/// </summary>
internal sealed class PdfDecryptor
{
    enum Method { None, AesV2, AesV3 }

    static readonly byte[] PasswordPadding =
    [
        0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41, 0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
        0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80, 0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A,
    ];

    readonly byte[] _fileKey;
    readonly Method _streams, _strings;
    readonly bool _encryptMetadata;
    readonly int _revision;

    /// <summary>Whether something decrypted to data that cannot be AES output: the empty password is not the file's password.</summary>
    public bool SawInvalidPadding { get; private set; }

    PdfDecryptor(byte[] fileKey, Method streams, Method strings, bool encryptMetadata, int revision)
    {
        _fileKey = fileKey;
        _streams = streams;
        _strings = strings;
        _encryptMetadata = encryptMetadata;
        _revision = revision;
    }

    /// <summary>
    /// Reads the <c>/Encrypt</c> dictionary and derives the file key from the empty user password.
    /// </summary>
    /// <exception cref="NotSupportedException">the handler is not the Standard one, uses an unknown version, or needs a password</exception>
    public static PdfDecryptor Create(PdfSourceDocument doc, PdfDictionary encrypt, byte[] firstId)
    {
        if (doc.NameOf(encrypt.GetRaw("Filter")) != "/Standard")
            throw new NotSupportedException("The PDF uses a security handler other than the Standard one.");
        int v = doc.IntOf(encrypt.GetRaw("V"));
        int r = doc.IntOf(encrypt.GetRaw("R"));
        if (v is 1 or 2 || r is 2 or 3)
            throw new NotSupportedException("The PDF is encrypted with RC4, which is not supported: .NET does not provide it.");
        if (v is not (4 or 5) || r is not (4 or 5 or 6))
            throw new NotSupportedException($"The PDF's encryption (V {v}, R {r}) is not supported.");

        byte[] o = StringBytes(doc, encrypt, "O"), u = StringBytes(doc, encrypt, "U");
        int permissions = doc.IntOf(encrypt.GetRaw("P"));
        bool encryptMetadata = !(doc.Resolve(encrypt.GetRaw("EncryptMetadata")) is PdfBoolean { Value: false });

        // Crypt filters: StmF and StrF name which filter in /CF applies; Identity (or absent) means no encryption.
        var cf = doc.DictOf(encrypt, "CF");
        var (streams, streamsRc4) = Filter(doc, cf, doc.NameOf(encrypt.GetRaw("StmF")));
        var (strings, stringsRc4) = Filter(doc, cf, doc.NameOf(encrypt.GetRaw("StrF")));
        if (streamsRc4 || stringsRc4)
            throw new NotSupportedException("The PDF is encrypted with RC4, which is not supported: .NET does not provide it.");

        byte[] fileKey = r >= 5
            ? FileKeyR5R6(r, u, StringBytes(doc, encrypt, "UE"))
            : FileKeyR4(o, permissions, firstId, encryptMetadata);
        return new PdfDecryptor(fileKey, streams, strings, encryptMetadata, r);
    }

    static byte[] StringBytes(PdfSourceDocument doc, PdfDictionary d, string key) =>
        doc.Resolve(d.GetRaw(key)) is PdfString s ? Encoding.Latin1.GetBytes(s.Value) : [];

    /// <summary>The method a crypt filter names, and whether it is the RC4 one (<c>/V2</c>), which is refused.</summary>
    static (Method Method, bool IsRc4) Filter(PdfSourceDocument doc, PdfDictionary? cf, string? name)
    {
        if (name is null or "/Identity" || cf is null) return (Method.None, false);
        var f = doc.DictOf(cf, name.TrimStart('/'));
        if (f is null) return (Method.None, false);
        return doc.NameOf(f.GetRaw("CFM")) switch
        {
            "/V2" => (Method.None, true),
            "/AESV2" => (Method.AesV2, false),
            "/AESV3" => (Method.AesV3, false),
            _ => (Method.None, false),
        };
    }

    // ------------------------------------------------------------ keys (ISO 32000-1 7.6.3.3, 7.6.3.4; ISO 32000-2 7.6.4.3)

    /// <summary>
    /// The file key of a revision 4 file (Algorithm 2). The empty password is not checked against /U: doing so needs RC4. A file that
    /// does need a password therefore decrypts to noise, which the caller meets as data it cannot read.
    /// </summary>
    static byte[] FileKeyR4(byte[] o, int permissions, byte[] id, bool encryptMetadata)
    {
        using var md5 = MD5.Create();
        var input = new List<byte>(PasswordPadding);
        input.AddRange(o.AsSpan(0, Math.Min(32, o.Length)).ToArray());
        input.AddRange(BitConverter.GetBytes(permissions));
        input.AddRange(id);
        if (!encryptMetadata) input.AddRange([0xFF, 0xFF, 0xFF, 0xFF]);
        var hash = md5.ComputeHash(input.ToArray());
        for (int i = 0; i < 50; i++) hash = md5.ComputeHash(hash, 0, 16);
        return hash.AsSpan(0, 16).ToArray();
    }

    static byte[] FileKeyR5R6(int r, byte[] u, byte[] ue)
    {
        if (u.Length < 48 || ue.Length < 32) throw new NotSupportedException("The PDF's encryption data is incomplete.");
        byte[] empty = [];
        var validationSalt = u.AsSpan(32, 8).ToArray();
        var keySalt = u.AsSpan(40, 8).ToArray();
        var check = r == 5 ? Sha256(empty, validationSalt) : PdfAes256SecurityHandler.Hash(empty, validationSalt, []);
        if (!check.AsSpan().SequenceEqual(u.AsSpan(0, 32)))
            throw new NotSupportedException("The PDF needs a password to open.");
        var intermediate = r == 5 ? Sha256(empty, keySalt) : PdfAes256SecurityHandler.Hash(empty, keySalt, []);
        using var aes = Aes.Create();
        aes.Key = intermediate;
        return aes.DecryptCbc(ue.AsSpan(0, 32), new byte[16], PaddingMode.None);
    }

    static byte[] Sha256(byte[] password, byte[] salt)
    {
        var data = new byte[password.Length + salt.Length];
        password.CopyTo(data, 0);
        salt.CopyTo(data, password.Length);
        return SHA256.HashData(data);
    }

    // ------------------------------------------------------------ decrypting

    /// <summary>Decrypts every string inside <paramref name="item"/> (a dictionary or array, recursively), and the stream of a dictionary.</summary>
    public void DecryptObject(PdfItem item, int number, int generation)
    {
        if (item is PdfDictionary dict)
        {
            // Cross-reference streams are never encrypted, and the document metadata only when the handler says so.
            string? type = dict.GetRaw("Type") is PdfName t ? t.Value : null;
            bool streamEncrypted = type != "/XRef" && (_encryptMetadata || type != "/Metadata") && !HasCryptIdentity(dict);
            DecryptStrings(dict, number, generation);
            if (dict.Stream is not null && streamEncrypted && _streams != Method.None)
                dict.Stream = new PdfDictionary.PdfStream(Decrypt(dict.Stream.Value, _streams, number, generation), dict);
        }
        else DecryptStrings(item, number, generation);
    }

    static bool HasCryptIdentity(PdfDictionary dict)
    {
        // A stream whose own filter list starts with /Crypt (Identity) is stored in the clear.
        var filter = dict.GetRaw("Filter");
        return filter is PdfName { Value: "/Crypt" } || (filter is PdfArray { Elements.Count: > 0 } a && a.Elements[0] is PdfName { Value: "/Crypt" });
    }

    void DecryptStrings(PdfItem item, int number, int generation)
    {
        if (_strings == Method.None) return;
        if (item is PdfDictionary d)
        {
            foreach (var key in new List<string>(d.Elements.Keys))
            {
                var value = d.Elements[key];
                if (value is PdfString s) d.Elements[key] = Reencode(s, number, generation);
                else if (value is PdfDictionary or PdfArray) DecryptStrings(value, number, generation);
            }
        }
        else if (item is PdfArray a)
        {
            for (int i = 0; i < a.Elements.Count; i++)
            {
                var value = a.Elements[i];
                if (value is PdfString s) a.Elements[i] = Reencode(s, number, generation);
                else if (value is PdfDictionary or PdfArray) DecryptStrings(value, number, generation);
            }
        }
    }

    PdfString Reencode(PdfString s, int number, int generation) =>
        new(Encoding.Latin1.GetString(Decrypt(Encoding.Latin1.GetBytes(s.Value), _strings, number, generation)), PdfStringEncoding.LatinOneByteEncoding);

    byte[] Decrypt(byte[] data, Method method, int number, int generation)
    {
        byte[] key = ObjectKey(method, number, generation);
        switch (method)
        {
            case Method.AesV2 or Method.AesV3:
            {
                if (data.Length < 32) return [];
                int cipherLength = (data.Length - 16) / 16 * 16;
                if (cipherLength <= 0) return [];
                using var aes = Aes.Create();
                aes.Key = key;
                var plain = aes.DecryptCbc(data.AsSpan(16, cipherLength), data.AsSpan(0, 16), PaddingMode.None);
                int pad = plain.Length > 0 ? plain[^1] : 0;
                bool valid = pad is >= 1 and <= 16 && pad <= plain.Length;
                for (int i = 1; valid && i <= pad; i++) valid = plain[^i] == pad;
                if (!valid)
                {
                    // Valid padding is what a correct key always produces and a wrong one almost never does.
                    SawInvalidPadding = true;
                    return plain;
                }
                return plain.AsSpan(0, plain.Length - pad).ToArray();
            }
            default: return data;
        }
    }

    byte[] ObjectKey(Method method, int number, int generation)
    {
        if (method == Method.AesV3 || _revision >= 5) return _fileKey;
        Span<byte> input = _fileKey.Length + 9 <= 64 ? stackalloc byte[64] : new byte[_fileKey.Length + 9];
        _fileKey.CopyTo(input);
        int length = _fileKey.Length;
        input[length++] = (byte)number;
        input[length++] = (byte)(number >> 8);
        input[length++] = (byte)(number >> 16);
        input[length++] = (byte)generation;
        input[length++] = (byte)(generation >> 8);
        if (method == Method.AesV2)
        {
            "sAlT"u8.CopyTo(input[length..]);
            length += 4;
        }

        Span<byte> hash = stackalloc byte[16];
        MD5.HashData(input[..length], hash);
        return hash[..Math.Min(_fileKey.Length + 5, 16)].ToArray();
    }
}
