using PeachDrawing.PDF;
using PeachDrawing.PDF.Engine.Security;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PeachPDF.Tests.PdfEngineTests.Security
{
    /// <summary>The AES-256 security handler itself: key wrapping, the permission word and password preparation.</summary>
    public class PdfAes256SecurityHandlerTests
    {
        static byte[] Hex(string text) => Convert.FromHexString(Regex.Replace(text, @"\s+", ""));

        static byte[] EncryptEntry(string pdf, string key)
        {
            var match = Regex.Match(pdf, $@"/Encrypt\s*<<.*?{key}\s*<([0-9A-Fa-f\s]+)>", RegexOptions.Singleline);
            Assert.True(match.Success, $"{key} not found in /Encrypt");
            return Hex(match.Groups[1].Value);
        }

        /// <summary>Algorithm 2.A: recover the file key from /UE (user) or /OE (owner) given the matching password.</summary>
        static byte[]? RecoverFileKey(string pdf, string password, bool owner)
        {
            var u = EncryptEntry(pdf, "/U");
            var pw = Encoding.UTF8.GetBytes(password);
            byte[] entry = owner ? EncryptEntry(pdf, "/O") : u;
            byte[] extra = owner ? u : [];
            var validationSalt = entry[32..40];
            var keySalt = entry[40..48];

            if (!PdfAes256SecurityHandler.Hash(pw, validationSalt, extra).AsSpan().SequenceEqual(entry.AsSpan(0, 32)))
                return null;

            var wrapped = EncryptEntry(pdf, owner ? "/OE" : "/UE");
            using var aes = Aes.Create();
            aes.Key = PdfAes256SecurityHandler.Hash(pw, keySalt, extra);
            return aes.DecryptCbc(wrapped, new byte[16], PaddingMode.None);
        }

        [Theory]
        [InlineData(PdfPermissions.All, -4)]
        [InlineData(PdfPermissions.None, -3904)]
        [InlineData(PdfPermissions.AllowPrint, -3900)]
        [InlineData(PdfPermissions.ReadOnly, -1324)]
        public void PermissionWord_SetsTheReservedBitsAndTheFlags(PdfPermissions permissions, int expected) =>
            Assert.Equal(expected, PdfAes256SecurityHandler.ToPermissionWord(permissions));

        [Fact]
        public void Handler_UserAndOwnerPasswords_UnwrapTheSameFileKey_AndWrongOnesAreRejected()
        {
            var options = new PdfEncryptionOptions { UserPassword = "user", OwnerPassword = "owner" };
            var handler = new PdfAes256SecurityHandler(options);
            var pdf = "x /Encrypt<< " + string.Join(" ", new[] { ("/U", handler.U), ("/UE", handler.UE), ("/O", handler.O), ("/OE", handler.OE) }
                .Select(p => $"{p.Item1} <{Convert.ToHexString(p.Item2)}>")) + ">>";

            Assert.Equal(handler.FileKey, RecoverFileKey(pdf, "user", owner: false));
            Assert.Equal(handler.FileKey, RecoverFileKey(pdf, "owner", owner: true));
            Assert.Null(RecoverFileKey(pdf, "nope", owner: false));
            Assert.Null(RecoverFileKey(pdf, "user", owner: true));
        }

        [Fact]
        public void Handler_Perms_DecryptsToTheWordTheFlagsAndTheAdbMarker()
        {
            var handler = new PdfAes256SecurityHandler(new PdfEncryptionOptions
            {
                UserPassword = "u", OwnerPassword = "o", Permissions = PdfPermissions.ReadOnly, EncryptMetadata = false,
            });

            using var aes = Aes.Create();
            aes.Key = handler.FileKey;
            var block = aes.DecryptEcb(handler.Perms, PaddingMode.None);

            Assert.Equal(PdfAes256SecurityHandler.ToPermissionWord(PdfPermissions.ReadOnly), BitConverter.ToInt32(block, 0));
            Assert.Equal([0xFF, 0xFF, 0xFF, 0xFF], block[4..8]);
            Assert.Equal("Fadb", Encoding.ASCII.GetString(block, 8, 4));
        }

        [Fact]
        public void Handler_FreshKeyPerInstance()
        {
            var options = new PdfEncryptionOptions { UserPassword = "u" };

            Assert.NotEqual(new PdfAes256SecurityHandler(options).FileKey, new PdfAes256SecurityHandler(options).FileKey);
        }

        [Fact]
        public void Handler_WithoutAnOwnerPassword_NothingUnlocksTheOwnerEntries()
        {
            var handler = new PdfAes256SecurityHandler(new PdfEncryptionOptions { UserPassword = "u" });
            var pdf = $"/Encrypt<< /U <{Convert.ToHexString(handler.U)}> /O <{Convert.ToHexString(handler.O)}> /OE <{Convert.ToHexString(handler.OE)}> >>";

            Assert.Null(RecoverFileKey(pdf, "", owner: true));
            Assert.Null(RecoverFileKey(pdf, "u", owner: true));
        }

        [Fact]
        public void PreparePassword_CutsAtExactlyTheByteLimit()
        {
            var longAscii = PdfAes256SecurityHandler.PreparePassword(new string('a', 300));
            // 126 ASCII bytes then a 3-byte character straddle byte 127: a reader keeps exactly 127 bytes, so must we.
            var straddling = PdfAes256SecurityHandler.PreparePassword(new string('a', 126) + "€");

            Assert.Equal(127, longAscii.Length);
            Assert.Equal(127, straddling.Length);
            Assert.Equal(0xE2, straddling[126]);
            Assert.Empty(PdfAes256SecurityHandler.PreparePassword(null));
            Assert.Empty(PdfAes256SecurityHandler.PreparePassword(""));
        }

        [Fact]
        public void PreparePassword_NormalizesCompatibilityForms()
        {
            // U+FB01 (the "fi" ligature) folds to "fi" under NFKC.
            Assert.Equal("fi"u8.ToArray(), PdfAes256SecurityHandler.PreparePassword("ﬁ"));
        }
    }
}
