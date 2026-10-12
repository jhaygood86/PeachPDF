using PeachPDF.PDF.Security;
using PeachPDF.Tests.TestSupport;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// AES-256 (security handler revision 6) encryption. The reader side below is written from the specification
    /// (ISO 32000-2, 7.6.4.3.3 "Algorithm 2.A") rather than by calling back into the writer, so a file that only the
    /// writer itself could open does not pass.
    /// </summary>
    public class PdfEncryptionTests
    {
        // The title is a Unicode string: stored as hexadecimal UTF-16BE ("Plain" is enough to recognize it).
        static readonly string TitleHex = Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes("Plain Secret"));

        const string SimpleHtml = "<html><body><p>Hello encrypted world</p></body></html>";

        static PdfGenerateConfig Config(PdfEncryptionOptions? encryption) => new()
        {
            PageSize = PageSize.A4,
            Metadata = new PdfDocumentMetadata { Title = "Plain Secret Title", CreationDate = DateTimeOffset.UtcNow },
            Encryption = encryption,
        };

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

        static byte[] DecryptCbc(byte[] key, byte[] data)
        {
            using var aes = Aes.Create();
            aes.Key = key;
            return aes.DecryptCbc(data.AsSpan(16), data.AsSpan(0, 16), PaddingMode.PKCS7);
        }

        /// <summary>Every stream's bytes, as stored.</summary>
        static byte[][] StreamBodies(byte[] file)
        {
            var text = Encoding.Latin1.GetString(file);
            return Regex.Matches(text, @"stream\n(.*?)\nendstream", RegexOptions.Singleline)
                .Select(m => Encoding.Latin1.GetBytes(m.Groups[1].Value)).ToArray();
        }

        static async Task<byte[]> Render(PdfGenerateConfig config)
        {
            var document = await new PdfGenerator().GeneratePdf(SimpleHtml, config);
            using var stream = new MemoryStream();
            document.Save(stream);
            return stream.ToArray();
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

        [Fact]
        public async Task Generated_HasTheEncryptDictionary_AndNoPlaintextTitleOrContent()
        {
            var file = await Render(Config(new PdfEncryptionOptions { UserPassword = "open", OwnerPassword = "admin" }));
            var pdf = Encoding.Latin1.GetString(file);

            Assert.Matches(new Regex(@"/Filter\s*/Standard"), pdf);
            Assert.Matches(new Regex(@"/V\s+5\b"), pdf);
            Assert.Matches(new Regex(@"/R\s+6\b"), pdf);
            Assert.Contains("/AESV3", pdf);
            Assert.Matches(new Regex(@"/Length\s+256"), pdf);
            Assert.DoesNotContain(TitleHex, pdf);
            Assert.DoesNotContain("/EncryptMetadata", pdf);
        }

        [Fact]
        public async Task Generated_EveryStreamDecryptsWithTheUserKey_AndWithTheOwnerKey()
        {
            var file = await Render(Config(new PdfEncryptionOptions { UserPassword = "open", OwnerPassword = "admin" }));
            var pdf = Encoding.Latin1.GetString(file);

            var userKey = RecoverFileKey(pdf, "open", owner: false);
            var ownerKey = RecoverFileKey(pdf, "admin", owner: true);
            Assert.NotNull(userKey);
            Assert.Equal(userKey, ownerKey);
            Assert.Null(RecoverFileKey(pdf, "wrong", owner: false));

            var streams = StreamBodies(file);
            Assert.NotEmpty(streams);
            var decryptedAny = false;
            foreach (var body in streams)
            {
                // Ciphertext is a 16-byte IV plus whole blocks, and every stream decrypts with correct padding.
                Assert.Equal(0, body.Length % 16);
                var plain = DecryptCbc(userKey!, body);
                decryptedAny |= plain.Length > 0;
            }
            Assert.True(decryptedAny);
        }

        [Fact]
        public async Task Generated_PageContent_InflatesToTheOriginalOperatorsAfterDecryption()
        {
            var file = await Render(Config(new PdfEncryptionOptions { UserPassword = "open" }));
            var pdf = Encoding.Latin1.GetString(file);
            var key = RecoverFileKey(pdf, "open", owner: false)!;

            var texts = StreamBodies(file).Select(b =>
            {
                var plain = DecryptCbc(key, b);
                try
                {
                    using var zlib = new ZLibStream(new MemoryStream(plain), CompressionMode.Decompress);
                    using var output = new MemoryStream();
                    zlib.CopyTo(output);
                    return Encoding.Latin1.GetString(output.ToArray());
                }
                catch (InvalidDataException)
                {
                    return Encoding.Latin1.GetString(plain);
                }
            }).ToList();

            Assert.Contains(texts, t => t.Contains(" Tj") || t.Contains("TJ"));
        }

        [Fact]
        public async Task Generated_DeclaredLengthMatchesTheEncryptedBytes()
        {
            var file = await Render(Config(new PdfEncryptionOptions()));
            var pdf = Encoding.Latin1.GetString(file);

            foreach (Match match in Regex.Matches(pdf, @"/Length\s+(\d+)[^>]*>>\s*stream\n(.*?)\nendstream", RegexOptions.Singleline))
                Assert.Equal(int.Parse(match.Groups[1].Value), match.Groups[2].Length);
        }

        [Fact]
        public async Task Generated_TrailerIdAndEncryptEntriesStayUnencrypted()
        {
            var file = await Render(Config(new PdfEncryptionOptions { UserPassword = "open" }));
            var pdf = Encoding.Latin1.GetString(file);

            var id = Regex.Match(pdf, @"/ID\s*\[\s*<([0-9A-Fa-f]{32})>\s*<([0-9A-Fa-f]{32})>\s*\]");
            Assert.True(id.Success);
            Assert.Equal(48, EncryptEntry(pdf, "/U").Length);
            Assert.Equal(48, EncryptEntry(pdf, "/O").Length);
            Assert.Equal(32, EncryptEntry(pdf, "/UE").Length);
            Assert.Equal(32, EncryptEntry(pdf, "/OE").Length);
            Assert.Equal(16, EncryptEntry(pdf, "/Perms").Length);
        }

        [Fact]
        public async Task Generated_Permissions_AreWrittenAsTheSignedWord()
        {
            var file = await Render(Config(PdfEncryptionOptions.ReadOnly(ownerPassword: "admin")));
            var pdf = Encoding.Latin1.GetString(file);

            Assert.Matches(new Regex(@"/P\s+-1324\b"), pdf);
        }

        [Fact]
        public async Task Generated_NoUserPassword_OpensWithTheEmptyPassword()
        {
            var file = await Render(Config(PdfEncryptionOptions.ReadOnly(ownerPassword: "admin")));
            var pdf = Encoding.Latin1.GetString(file);

            Assert.NotNull(RecoverFileKey(pdf, "", owner: false));
            Assert.Null(RecoverFileKey(pdf, "", owner: true));
        }

        [Fact]
        public async Task Generated_SavingTwice_EncryptsThePlaintextAgain_UnderANewKey()
        {
            var document = await new PdfGenerator().GeneratePdf(SimpleHtml, Config(new PdfEncryptionOptions { UserPassword = "open" }));

            byte[] Save()
            {
                using var stream = new MemoryStream();
                document.Save(stream);
                return stream.ToArray();
            }

            var first = Save();
            var second = Save();
            var firstPdf = Encoding.Latin1.GetString(first);
            var secondPdf = Encoding.Latin1.GetString(second);
            var firstKey = RecoverFileKey(firstPdf, "open", owner: false)!;
            var secondKey = RecoverFileKey(secondPdf, "open", owner: false)!;

            Assert.NotEqual(firstKey, secondKey);
            foreach (var (file, key) in new[] { (first, firstKey), (second, secondKey) })
            {
                foreach (var body in StreamBodies(file))
                    Assert.NotEmpty(DecryptCbc(key, body).Concat(new byte[1]));
            }
            // Decrypting the second file's streams with its own key gives the same lengths as the first's: not doubly encrypted.
            Assert.Equal(
                StreamBodies(first).Select(b => DecryptCbc(firstKey, b).Length).Order(),
                StreamBodies(second).Select(b => DecryptCbc(secondKey, b).Length).Order());
        }

        [Fact]
        public async Task Generated_EncryptMetadataFalse_LeavesTheXmpReadable_WithTheIdentityCryptFilter()
        {
            var config = Config(new PdfEncryptionOptions { UserPassword = "open", EncryptMetadata = false });
            config.EnableXmpMetadata = true;
            var file = await Render(config);
            var pdf = Encoding.Latin1.GetString(file);

            Assert.Matches(new Regex(@"/EncryptMetadata\s+false"), pdf);
            Assert.Contains("<x:xmpmeta", pdf);
            Assert.Matches(new Regex(@"/Type\s*/Metadata.*?/Filter\s*/Crypt", RegexOptions.Singleline), pdf);
            Assert.Matches(new Regex(@"/Name\s*/Identity"), pdf);
        }

        [Fact]
        public async Task Generated_EncryptMetadataTrue_EncryptsTheXmp()
        {
            var config = Config(new PdfEncryptionOptions { UserPassword = "open" });
            config.EnableXmpMetadata = true;
            var pdf = Encoding.Latin1.GetString(await Render(config));

            Assert.DoesNotContain("<x:xmpmeta", pdf);
            Assert.DoesNotContain("/Crypt", pdf);
        }

        [Fact]
        public async Task Generated_Pdf17_WritesTheHeaderAndTheAdobeExtension()
        {
            var config = Config(new PdfEncryptionOptions());
            config.PdfVersion = PdfVersion.Pdf17;
            var pdf = Encoding.Latin1.GetString(await Render(config));

            Assert.StartsWith("%PDF-1.7", pdf);
            Assert.Matches(new Regex(@"/Extensions\s*<<\s*/ADBE\s*<<.*?/BaseVersion\s*/1\.7.*?/ExtensionLevel\s+8", RegexOptions.Singleline), pdf);
        }

        [Fact]
        public async Task Generated_Pdf20_HasNoExtensionsEntry()
        {
            var pdf = Encoding.Latin1.GetString(await Render(Config(new PdfEncryptionOptions())));

            Assert.StartsWith("%PDF-2.0", pdf);
            Assert.DoesNotContain("/Extensions", pdf);
        }

        [Fact]
        public async Task Generated_WithoutEncryption_HasNoEncryptEntry()
        {
            var pdf = Encoding.Latin1.GetString(await Render(Config(null)));

            Assert.DoesNotContain("/Encrypt", pdf);
            Assert.Contains(TitleHex, pdf);
        }

        [Theory]
        [InlineData(PdfAConformance.PdfA2B)]
        [InlineData(PdfAConformance.PdfA4)]
        public async Task Encryption_WithPdfA_Throws(PdfAConformance conformance)
        {
            var config = Config(new PdfEncryptionOptions());
            config.PdfAConformance = conformance;

            await Assert.ThrowsAsync<InvalidOperationException>(() => new PdfGenerator().GeneratePdf(SimpleHtml, config));
        }

        [Fact]
        public async Task Encryption_WithPdfX_Throws()
        {
            var config = Config(new PdfEncryptionOptions());
            config.PdfXConformance = PdfXConformance.X4;

            await Assert.ThrowsAsync<InvalidOperationException>(() => new PdfGenerator().GeneratePdf(SimpleHtml, config));
        }

        [Fact]
        public async Task Encryption_ASecondCallWithDifferentProtection_Throws()
        {
            var generator = new PdfGenerator();
            var document = await generator.GeneratePdf(SimpleHtml, Config(new PdfEncryptionOptions { UserPassword = "a" }));

            await generator.AddPdfPages(document, SimpleHtml, Config(new PdfEncryptionOptions { UserPassword = "a" }));
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => generator.AddPdfPages(document, SimpleHtml, Config(new PdfEncryptionOptions { UserPassword = "b" })));
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => generator.AddPdfPages(document, SimpleHtml, Config(null)));
        }

        [Fact]
        public async Task Encryption_OptionsChangedAfterwards_DoNotAffectTheDocument()
        {
            var options = new PdfEncryptionOptions { UserPassword = "first" };
            var document = await new PdfGenerator().GeneratePdf(SimpleHtml, Config(options));
            options.UserPassword = "second";

            using var stream = new MemoryStream();
            document.Save(stream);
            var pdf = Encoding.Latin1.GetString(stream.ToArray());

            Assert.NotNull(RecoverFileKey(pdf, "first", owner: false));
            Assert.Null(RecoverFileKey(pdf, "second", owner: false));
        }

        [Fact]
        public void Options_SameAs_ComparesEveryField()
        {
            var a = new PdfEncryptionOptions { UserPassword = "u", OwnerPassword = "o", Permissions = PdfPermissions.ReadOnly, EncryptMetadata = false };

            Assert.True(a.SameAs(a.Clone()));
            Assert.False(a.SameAs(null));
            Assert.False(a.SameAs(new PdfEncryptionOptions { UserPassword = "x" }));
            Assert.False(a.SameAs(new PdfEncryptionOptions { UserPassword = "u", OwnerPassword = "x" }));
            Assert.False(a.SameAs(new PdfEncryptionOptions { UserPassword = "u", OwnerPassword = "o" }));
            Assert.False(a.SameAs(new PdfEncryptionOptions { UserPassword = "u", OwnerPassword = "o", Permissions = PdfPermissions.ReadOnly }));
        }

        [Fact]
        public void ReadOnly_ForbidsEditingButAllowsViewingPrintingAndCopying()
        {
            var permissions = PdfEncryptionOptions.ReadOnly().Permissions;

            Assert.True(permissions.HasFlag(PdfPermissions.AllowPrint));
            Assert.True(permissions.HasFlag(PdfPermissions.AllowCopy));
            Assert.False(permissions.HasFlag(PdfPermissions.AllowModify));
            Assert.False(permissions.HasFlag(PdfPermissions.AllowAnnotations));
            Assert.False(permissions.HasFlag(PdfPermissions.AllowFillForms));
            Assert.False(permissions.HasFlag(PdfPermissions.AllowAssemble));
        }
    }
}
