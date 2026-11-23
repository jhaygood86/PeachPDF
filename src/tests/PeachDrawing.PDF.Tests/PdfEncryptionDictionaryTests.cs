using System.Text;
using System.Text.RegularExpressions;
using PeachDrawing.Core;

namespace PeachDrawing.PDF.Tests
{
    /// <summary>The encryption dictionary is an indirect object the trailer refers to, stored with its strings in the clear.</summary>
    public class PdfEncryptionDictionaryTests
    {
        static PdfComposerOptions Encrypted(bool linearize = false) => new()
        {
            Linearize = linearize,
            Encryption = new PdfEncryptionOptions { UserPassword = "user", OwnerPassword = "owner" },
        };

        static byte[] Save(PdfComposer composer)
        {
            using var stream = new MemoryStream();
            composer.Save(stream);
            return stream.ToArray();
        }

        static PdfComposer Make(PdfComposerOptions options, int pages = 2)
        {
            var composer = new PdfComposer(new PdfRenderContext(), options);
            for (var i = 0; i < pages; i++)
            {
                var page = composer.AddPage(100, 100);
                page.Canvas.DrawRectangle(page.Canvas.GetSolidBrush(PaintColor.FromArgb(255, 200, 120, 40)), 0, 0, 100, 100);
            }

            return composer;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TheTrailerReferstoAnIndirectEncryptionDictionary(bool linearize)
        {
            using var composer = Make(Encrypted(linearize), pages: 3);
            var text = Encoding.Latin1.GetString(Save(composer));

            var reference = Regex.Matches(text, @"/Encrypt (\d+) 0 R")[^1];
            Assert.DoesNotMatch(@"/Encrypt\s*<<", text);                                       // never a direct dictionary any more
            var body = Regex.Match(text, $@"(?<=\n){reference.Groups[1].Value} 0 obj\n(.*?)\nendobj", RegexOptions.Singleline).Groups[1].Value;
            Assert.Contains("/Filter /Standard", body);
            Assert.Contains("/CFM /AESV3", body);
        }

        [Fact]
        public void ItsStringsAreStoredInTheClear_AtTheirOwnLengths()
        {
            using var composer = Make(Encrypted());
            var text = Encoding.Latin1.GetString(Save(composer));
            var body = Regex.Match(text, @"(?<=\n)\d+ 0 obj\n(<<\n/Filter /Standard.*?)\nendobj", RegexOptions.Singleline).Groups[1].Value;

            // AES-256 (revision 6): /U and /O are 48 bytes, /UE and /OE 32, /Perms 16. Encrypted as a string of an indirect object they would
            // each have a 16-byte IV and padding added, and no reader could read the key material back.
            int Hex(string key) => Regex.Match(body, $@"/{key}\s*<([0-9A-Fa-f]+)>").Groups[1].Length / 2;
            Assert.Equal(48, Hex("U"));
            Assert.Equal(48, Hex("O"));
            Assert.Equal(32, Hex("UE"));
            Assert.Equal(32, Hex("OE"));
            Assert.Equal(16, Hex("Perms"));
        }

        [Fact]
        public void TheFileStillOpensWithItsPasswords()
        {
            using var composer = Make(Encrypted());
            var file = Save(composer);

            Assert.Equal(2, PdfPageSource.Open(file, "user").PageCount);
            Assert.Equal(2, PdfPageSource.Open(file, "owner").PageCount);
            Assert.Throws<PdfPasswordException>(() => PdfPageSource.Open(file, "wrong"));
        }

        [Fact]
        public void SavingTwice_WritesOneEncryptionDictionaryEach_UnderANewKey()
        {
            using var composer = Make(Encrypted());
            var first = Save(composer);
            var second = Save(composer);

            // The first save's dictionary is not carried into the second file as a stray object.
            Assert.Single(Regex.Matches(Encoding.Latin1.GetString(first), "/Filter /Standard"));
            Assert.Single(Regex.Matches(Encoding.Latin1.GetString(second), "/Filter /Standard"));
            Assert.Equal(2, PdfPageSource.Open(second, "user").PageCount);

            // A fresh key and salts each save: the two files do not share their key material.
            string U(byte[] file) => Regex.Match(Encoding.Latin1.GetString(file), @"/U\s*<([0-9A-Fa-f]+)>").Groups[1].Value;
            Assert.NotEqual(U(first), U(second));
        }

        [Fact]
        public void ALinearizedFile_PutsItWithTheCatalog_AheadOfTheFirstPage()
        {
            using var composer = Make(Encrypted(linearize: true), pages: 3);
            var file = Save(composer);
            var text = Encoding.Latin1.GetString(file);

            var encrypt = text.IndexOf("/Filter /Standard", StringComparison.Ordinal);
            var catalog = text.IndexOf("/Type /Catalog", StringComparison.Ordinal);
            var firstPage = text.IndexOf("/Type /Page", StringComparison.Ordinal);

            Assert.True(catalog >= 0 && encrypt > catalog, "the encryption dictionary follows the catalog");
            Assert.True(encrypt < firstPage, "and comes before the first page");
            Assert.Equal(3, PdfPageSource.Open(file, "user").PageCount);
        }

        [Fact]
        public void AnUnencryptedFile_HasNoEncryptionDictionary()
        {
            using var composer = Make(new PdfComposerOptions());
            var text = Encoding.Latin1.GetString(Save(composer));

            Assert.DoesNotContain("/Encrypt", text);
            Assert.DoesNotContain("/Filter /Standard", text);
        }
    }
}
