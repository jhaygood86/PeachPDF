using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Engine.Security;

namespace PeachDrawing.PDF.Import;

/// <summary>
/// The Standard security handler of a PDF, opened with the empty user password (ISO 32000-1 7.6, ISO 32000-2 7.6.4): RC4 with 40 or
/// more bits, AES-128 and AES-256 (revisions 2 to 6), with the crypt filters of version 4 and 5 files. A file that needs a password
/// is refused, as is a handler this class does not know.
/// </summary>
internal sealed class PdfDecryptor
{
    enum Method { None, Rc4, AesV2, AesV3 }

    static readonly byte[] PasswordPadding =
    [
        0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41, 0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
        0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80, 0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A,
    ];

    readonly byte[] _fileKey;
    readonly Method _streams, _strings;
    readonly bool _encryptMetadata;
    readonly int _revision;

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
        if (v is < 1 or > 5 || r is < 2 or > 6)
            throw new NotSupportedException($"The PDF's encryption (V {v}, R {r}) is not supported.");

        byte[] o = StringBytes(doc, encrypt, "O"), u = StringBytes(doc, encrypt, "U");
        int permissions = doc.IntOf(encrypt.GetRaw("P"));
        bool encryptMetadata = !(doc.Resolve(encrypt.GetRaw("EncryptMetadata")) is PdfBoolean { Value: false });

        Method streams = Method.Rc4, strings = Method.Rc4;
        int keyBytes = Math.Max(5, doc.IntOf(encrypt.GetRaw("Length")) / 8);
        if (v == 1) keyBytes = 5;
        if (v >= 4)
        {
            // Crypt filters: StmF and StrF name which filter in /CF applies; Identity (or absent) means no encryption.
            var cf = doc.DictOf(encrypt, "CF");
            (streams, _) = Filter(doc, cf, doc.NameOf(encrypt.GetRaw("StmF")));
            (strings, _) = Filter(doc, cf, doc.NameOf(encrypt.GetRaw("StrF")));
            keyBytes = v == 5 ? 32 : 16;
        }

        byte[] fileKey = r >= 5
            ? FileKeyR5R6(r, u, StringBytes(doc, encrypt, "UE"))
            : FileKeyR2R4(r, keyBytes, o, u, permissions, firstId, encryptMetadata);
        return new PdfDecryptor(fileKey, streams, strings, encryptMetadata, r);
    }

    static byte[] StringBytes(PdfSourceDocument doc, PdfDictionary d, string key) =>
        doc.Resolve(d.GetRaw(key)) is PdfString s ? Encoding.Latin1.GetBytes(s.Value) : [];

    static (Method Method, int Length) Filter(PdfSourceDocument doc, PdfDictionary? cf, string? name)
    {
        if (name is null or "/Identity" || cf is null) return (Method.None, 0);
        var f = doc.DictOf(cf, name.TrimStart('/'));
        if (f is null) return (Method.None, 0);
        int length = doc.IntOf(f.GetRaw("Length"));
        var method = doc.NameOf(f.GetRaw("CFM")) switch
        {
            "/V2" => Method.Rc4,
            "/AESV2" => Method.AesV2,
            "/AESV3" => Method.AesV3,
            _ => Method.None,
        };
        return (method, length);
    }

    // ------------------------------------------------------------ keys (ISO 32000-1 7.6.3.3, 7.6.3.4; ISO 32000-2 7.6.4.3)

    static byte[] FileKeyR2R4(int r, int keyBytes, byte[] o, byte[] u, int permissions, byte[] id, bool encryptMetadata)
    {
        using var md5 = MD5.Create();
        var input = new List<byte>(PasswordPadding);
        input.AddRange(o.AsSpan(0, Math.Min(32, o.Length)).ToArray());
        input.AddRange(BitConverter.GetBytes(permissions));
        input.AddRange(id);
        if (r >= 4 && !encryptMetadata) input.AddRange([0xFF, 0xFF, 0xFF, 0xFF]);
        var hash = md5.ComputeHash(input.ToArray());
        if (r >= 3)
            for (int i = 0; i < 50; i++) hash = md5.ComputeHash(hash, 0, keyBytes);
        var key = hash.AsSpan(0, keyBytes).ToArray();

        // The empty user password must open the file: recompute /U and compare (Algorithms 4 and 5).
        byte[] check;
        if (r == 2) check = Rc4(key, PasswordPadding);
        else
        {
            var seed = new List<byte>(PasswordPadding);
            seed.AddRange(id);
            var h = md5.ComputeHash(seed.ToArray());
            check = Rc4(key, h);
            for (int i = 1; i <= 19; i++)
            {
                var k = new byte[key.Length];
                for (int j = 0; j < k.Length; j++) k[j] = (byte)(key[j] ^ i);
                check = Rc4(k, check);
            }
        }
        int compare = r == 2 ? 32 : 16;
        if (u.Length < compare || !check.AsSpan(0, compare).SequenceEqual(u.AsSpan(0, compare)))
            throw new NotSupportedException("The PDF needs a password to open.");
        return key;
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
            case Method.Rc4: return Rc4(key, data);
            case Method.AesV2 or Method.AesV3:
            {
                if (data.Length < 32) return [];
                int cipherLength = (data.Length - 16) / 16 * 16;
                if (cipherLength <= 0) return [];
                using var aes = Aes.Create();
                aes.Key = key;
                var plain = aes.DecryptCbc(data.AsSpan(16, cipherLength), data.AsSpan(0, 16), PaddingMode.None);
                int pad = plain.Length > 0 ? plain[^1] : 0;
                return pad is >= 1 and <= 16 && pad <= plain.Length ? plain.AsSpan(0, plain.Length - pad).ToArray() : plain;
            }
            default: return data;
        }
    }

    byte[] ObjectKey(Method method, int number, int generation)
    {
        if (method == Method.AesV3 || _revision >= 5) return _fileKey;
        var input = new List<byte>(_fileKey)
        {
            (byte)number, (byte)(number >> 8), (byte)(number >> 16), (byte)generation, (byte)(generation >> 8),
        };
        if (method == Method.AesV2) input.AddRange("sAlT"u8.ToArray());
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(input.ToArray());
        return hash.AsSpan(0, Math.Min(_fileKey.Length + 5, 16)).ToArray();
    }

    /// <summary>RC4, which is its own inverse.</summary>
    internal static byte[] Rc4(byte[] key, byte[] data)
    {
        var s = new byte[256];
        for (int i = 0; i < 256; i++) s[i] = (byte)i;
        for (int i = 0, j = 0; i < 256; i++)
        {
            j = (j + s[i] + key[i % key.Length]) & 0xFF;
            (s[i], s[j]) = (s[j], s[i]);
        }
        var output = new byte[data.Length];
        for (int n = 0, i = 0, j = 0; n < data.Length; n++)
        {
            i = (i + 1) & 0xFF;
            j = (j + s[i]) & 0xFF;
            (s[i], s[j]) = (s[j], s[i]);
            output[n] = (byte)(data[n] ^ s[(s[i] + s[j]) & 0xFF]);
        }
        return output;
    }
}
