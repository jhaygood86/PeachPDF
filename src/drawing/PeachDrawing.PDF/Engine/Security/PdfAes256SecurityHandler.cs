using PeachDrawing.PDF.Engine.Internal;
using System;
using System.Security.Cryptography;
using System.Text;

namespace PeachDrawing.PDF.Engine.Security;

/// <summary>
/// The PDF standard security handler, version 5 revision 6 (ISO 32000-2, 7.6.4.3 and 7.6.5): AES-256 in CBC mode
/// for strings and streams, with the password-derived key wrapping and the iterated SHA-2 hash of "Algorithm 2.B".
/// Everything is built from <see cref="System.Security.Cryptography"/> primitives (AES, SHA-256/384/512, the system
/// random number generator); neither RC4 nor MD5, which earlier revisions need, is involved.
/// </summary>
/// <remarks>
/// One instance covers one save: it draws a fresh file encryption key, so two saves of the same document never
/// share a key or salts.
/// </remarks>
internal sealed class PdfAes256SecurityHandler
{
    /// <summary>PDF 2.0 caps a password at 127 UTF-8 bytes.</summary>
    const int MaxPasswordBytes = 127;

    const int AesBlockSize = 16;

    readonly byte[] _fileKey;
    readonly byte[] _u;
    readonly byte[] _ue;
    readonly byte[] _o;
    readonly byte[] _oe;
    readonly byte[] _perms;
    readonly int _permissionWord;
    readonly bool _encryptMetadata;

    public PdfAes256SecurityHandler(PdfEncryptionOptions options)
        : this(options, RandomNumberGenerator.GetBytes(32), RandomNumberGenerator.GetBytes(8), RandomNumberGenerator.GetBytes(8),
            RandomNumberGenerator.GetBytes(8), RandomNumberGenerator.GetBytes(8), RandomNumberGenerator.GetBytes(4))
    {
    }

    /// <summary>
    /// Builds a handler from explicit randomness, so the derived values can be checked against a fixed expectation.
    /// </summary>
    internal PdfAes256SecurityHandler(PdfEncryptionOptions options, byte[] fileKey, byte[] userValidationSalt,
        byte[] userKeySalt, byte[] ownerValidationSalt, byte[] ownerKeySalt, byte[] permsRandom)
    {
        ArgumentNullException.ThrowIfNull(options);

        _fileKey = fileKey;
        _encryptMetadata = options.EncryptMetadata;
        _permissionWord = ToPermissionWord(options.Permissions);

        byte[] userPassword = PreparePassword(options.UserPassword);
        // With no owner password the restrictions must still be unliftable, so key the owner entries off randomness
        // nobody keeps.
        byte[] ownerPassword = string.IsNullOrEmpty(options.OwnerPassword)
            ? RandomNumberGenerator.GetBytes(32)
            : PreparePassword(options.OwnerPassword);

        // U = hash(password || validation salt) || validation salt || key salt; UE wraps the file key.
        _u = Concat(Hash(userPassword, userValidationSalt, []), userValidationSalt, userKeySalt);
        _ue = EncryptCbcNoPadding(Hash(userPassword, userKeySalt, []), _fileKey);

        // O additionally binds the 48-byte U, so the owner entries cannot be lifted onto another document.
        _o = Concat(Hash(ownerPassword, ownerValidationSalt, _u), ownerValidationSalt, ownerKeySalt);
        _oe = EncryptCbcNoPadding(Hash(ownerPassword, ownerKeySalt, _u), _fileKey);

        _perms = BuildPerms(_fileKey, _permissionWord, _encryptMetadata, permsRandom);
    }

    /// <summary>Whether the XMP metadata stream is left in the clear.</summary>
    public bool LeavesMetadataPlain => !_encryptMetadata;

    /// <summary>The signed 32-bit /P value.</summary>
    public int PermissionWord => _permissionWord;

    /// <summary>Encrypts a string or stream: a fresh random IV followed by the AES-256-CBC (PKCS#7) ciphertext.</summary>
    public byte[] Encrypt(byte[] plaintext)
    {
        byte[] iv = RandomNumberGenerator.GetBytes(AesBlockSize);
        using Aes aes = Aes.Create();
        aes.Key = _fileKey;
        byte[] cipher = aes.EncryptCbc(plaintext, iv, PaddingMode.PKCS7);

        byte[] result = new byte[iv.Length + cipher.Length];
        Buffer.BlockCopy(iv, 0, result, 0, iv.Length);
        Buffer.BlockCopy(cipher, 0, result, iv.Length, cipher.Length);
        return result;
    }

    /// <summary>Builds the direct /Encrypt dictionary that goes in the trailer.</summary>
    public PdfDictionary CreateEncryptDictionary(PdfDocument document)
    {
        var encrypt = new PdfDictionary(document);
        encrypt.Elements.SetName("/Filter", "/Standard");
        encrypt.Elements.SetInteger("/V", 5);
        encrypt.Elements.SetInteger("/R", 6);
        encrypt.Elements.SetInteger("/Length", 256);

        var stdCf = new PdfDictionary(document);
        stdCf.Elements.SetName("/CFM", "/AESV3");
        stdCf.Elements.SetName("/AuthEvent", "/DocOpen");
        stdCf.Elements.SetInteger("/Length", 32);
        var cf = new PdfDictionary(document);
        cf.Elements["/StdCF"] = stdCf;
        encrypt.Elements["/CF"] = cf;
        encrypt.Elements.SetName("/StmF", "/StdCF");
        encrypt.Elements.SetName("/StrF", "/StdCF");

        encrypt.Elements["/O"] = HexString(_o);
        encrypt.Elements["/U"] = HexString(_u);
        encrypt.Elements["/OE"] = HexString(_oe);
        encrypt.Elements["/UE"] = HexString(_ue);
        encrypt.Elements.SetInteger("/P", _permissionWord);
        encrypt.Elements["/Perms"] = HexString(_perms);
        if (!_encryptMetadata)
            encrypt.Elements.SetBoolean("/EncryptMetadata", false);

        return encrypt;
    }

    static PdfString HexString(byte[] bytes)
        => new PdfString(PdfEncoders.LatinOneByteEncoding.GetString(bytes, 0, bytes.Length), PdfStringFlags.HexLiteral);

    /// <summary>
    /// Maps the public flags to the /P word: bits 1-2 clear, 7-8 and 13-32 set (reserved, ISO 32000-2 Table 22), and the
    /// permission bits taken as they are. The result is read as a signed 32-bit number, as the file stores it.
    /// </summary>
    internal static int ToPermissionWord(PdfPermissions permissions)
        => unchecked((int)(0xFFFFF0C0u | ((uint)permissions & (uint)PdfPermissions.All)));

    /// <summary>
    /// UTF-8 of the password, NFKC-normalized (the compatible part of the SASLprep profile that R6 asks for) and cut
    /// at 127 bytes. In an invariant-globalization host (the AOT CLI) non-ASCII normalization is unavailable and the
    /// text is used unnormalized.
    /// </summary>
    internal static byte[] PreparePassword(string? password)
    {
        if (string.IsNullOrEmpty(password))
            return [];

        string text = password;
        bool ascii = true;
        foreach (char ch in text)
        {
            if (ch > 0x7F) { ascii = false; break; }
        }
        if (!ascii)
        {
            try { text = text.Normalize(NormalizationForm.FormKC); }
            catch (Exception ex) when (ex is PlatformNotSupportedException or ArgumentException) { }
        }

        byte[] utf8 = Encoding.UTF8.GetBytes(text);
        if (utf8.Length <= MaxPasswordBytes)
            return utf8;

        // Exactly the first 127 bytes, even through the middle of a character: that is what a reader keeps, and a
        // password has to hash to the same bytes on both sides.
        return utf8.AsSpan(0, MaxPasswordBytes).ToArray();
    }

    /// <summary>
    /// "Algorithm 2.B" of ISO 32000-2: a SHA-256 seed hardened through at least 64 rounds of AES-128-CBC over a
    /// 64-fold repetition of password, running hash and extra data, with each round's SHA-2 variant chosen by its output.
    /// </summary>
    internal static byte[] Hash(byte[] password, byte[] salt, byte[] userKey)
    {
        byte[] k = SHA256.HashData(Concat(password, salt, userKey));

        int round = 0;
        byte[] e;
        do
        {
            byte[] unit = Concat(password, k, userKey);
            byte[] k1 = new byte[unit.Length * 64];
            for (int i = 0; i < 64; i++)
                Buffer.BlockCopy(unit, 0, k1, i * unit.Length, unit.Length);

            using (Aes aes = Aes.Create())
            {
                aes.Key = k.AsSpan(0, 16).ToArray();
                e = aes.EncryptCbc(k1, k.AsSpan(16, 16), PaddingMode.None);
            }

            // The first 16 bytes read as a big-endian number mod 3 equal their byte sum mod 3 (256 = 1 mod 3).
            int mod = 0;
            for (int i = 0; i < 16; i++)
                mod += e[i];
            mod %= 3;

            k = mod switch
            {
                0 => SHA256.HashData(e),
                1 => SHA384.HashData(e),
                _ => SHA512.HashData(e),
            };
            round++;
        }
        while (round < 64 || e[^1] > round - 32);

        return k.AsSpan(0, 32).ToArray();
    }

    /// <summary>AES-256-CBC with a zero IV and no padding, for wrapping the 32-byte file key.</summary>
    static byte[] EncryptCbcNoPadding(byte[] key, byte[] data)
    {
        using Aes aes = Aes.Create();
        aes.Key = key;
        return aes.EncryptCbc(data, new byte[AesBlockSize], PaddingMode.None);
    }

    /// <summary>
    /// The 16-byte /Perms block: the permission word (little-endian, widened to 64 bits with ones), an T/F flag for
    /// EncryptMetadata, the literal "adb" and four random bytes, encrypted in AES-256-ECB with the file key.
    /// </summary>
    internal static byte[] BuildPerms(byte[] fileKey, int permissionWord, bool encryptMetadata, byte[] random)
    {
        byte[] block = new byte[16];
        block[0] = (byte)permissionWord;
        block[1] = (byte)(permissionWord >> 8);
        block[2] = (byte)(permissionWord >> 16);
        block[3] = (byte)(permissionWord >> 24);
        block[4] = block[5] = block[6] = block[7] = 0xFF;
        block[8] = (byte)(encryptMetadata ? 'T' : 'F');
        block[9] = (byte)'a';
        block[10] = (byte)'d';
        block[11] = (byte)'b';
        Buffer.BlockCopy(random, 0, block, 12, 4);

        using Aes aes = Aes.Create();
        aes.Key = fileKey;
        return aes.EncryptEcb(block, PaddingMode.None);
    }

    static byte[] Concat(params byte[][] parts)
    {
        int total = 0;
        foreach (byte[] part in parts)
            total += part.Length;
        byte[] result = new byte[total];
        int offset = 0;
        foreach (byte[] part in parts)
        {
            Buffer.BlockCopy(part, 0, result, offset, part.Length);
            offset += part.Length;
        }
        return result;
    }

    // Exposed for tests that decrypt the output independently.
    internal byte[] FileKey => _fileKey;
    internal byte[] U => _u;
    internal byte[] UE => _ue;
    internal byte[] O => _o;
    internal byte[] OE => _oe;
    internal byte[] Perms => _perms;
}
