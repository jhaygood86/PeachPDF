using PeachDrawing;
using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Import;

namespace PeachDrawing.PDF.Tests.Import
{
    /// <summary>
    /// Encrypted files read with the empty user password. The fixtures were written by MuPDF (an independent implementation), one for each
    /// cipher the Standard security handler uses, so a key derivation or per-object key mistake here would not match what real files do.
    /// </summary>
    public class PdfDecryptionTests
    {
        private static class Fixtures
        {
        public const string Rc4_40 = "JVBERi0xLjcKJcK1wrYKJSBXcml0dGVuIGJ5IE11UERGIDEuMjkuMAoKMSAwIG9iago8PC9UeXBlL0NhdGFsb2cvUGFnZXMgMiAwIFIvSW5mbzw8L1Byb2R1Y2VyPDAyQkZGNDYwN0YyMURDNkExNThDQzAyRj4+Pj4+CmVuZG9iagoKMiAwIG9iago8PC9UeXBlL1BhZ2VzL0NvdW50IDEvS2lkc1s0IDAgUl0+PgplbmRvYmoKCjMgMCBvYmoKPDw+PgplbmRvYmoKCjQgMCBvYmoKPDwvVHlwZS9QYWdlL01lZGlhQm94WzAgMCAxMDAgMTAwXS9Sb3RhdGUgMC9SZXNvdXJjZXMgMyAwIFIvUGFyZW50IDIgMCBSL0NvbnRlbnRzWzUgMCBSXT4+CmVuZG9iagoKNSAwIG9iago8PC9MZW5ndGggMzE+PgpzdHJlYW0KDggjtBoJCJYL2awNJZbZUnaPYfNnrYyaA1T/qVrYfgplbmRzdHJlYW0KZW5kb2JqCgo2IDAgb2JqCjw8L1RpdGxlPDA2OUMyREFBQjEzQzYxQTdDRDRGOURGQT4+PgplbmRvYmoKCnhyZWYKMCA3CjAwMDAwMDAwMDAgNjU1MzUgZiAKMDAwMDAwMDA0MiAwMDAwMCBuIAowMDAwMDAwMTMyIDAwMDAwIG4gCjAwMDAwMDAxODQgMDAwMDAgbiAKMDAwMDAwMDIwNSAwMDAwMCBuIAowMDAwMDAwMzEyIDAwMDAwIG4gCjAwMDAwMDAzOTIgMDAwMDAgbiAKCnRyYWlsZXIKPDwvU2l6ZSA3L0luZm8gNiAwIFIvUm9vdCAxIDAgUi9JRFs8QzJCREMyOUFDMkE2QzM4NkMyOThDM0E0M0RDMkJDN0E+PEIzQkE0NEFBRjkwQkRERUJEQTY3QzY5QTdBMkIyRUJDPl0vRW5jcnlwdDw8L0ZpbHRlci9TdGFuZGFyZC9SIDIvViAxL0xlbmd0aCA0MC9QIC00L0VuY3J5cHRNZXRhZGF0YSB0cnVlL088QzkyNDIyNjg3RkFDRUU2ODZFMzczRjEwQjVDN0QwNDczODA1MzE1MkY3RTJFRTMwRTExQzY5RUM0NDI1NzZBQj4vVTwyQTEwQzVBRjM1OTdGQTUxQTY5MDkxNjU0M0Q5QjhCOTdDM0M5RkJBNUQyRjk1NjIwRkY5Q0Y4NkRCRDc1NzcwPj4+Pj4Kc3RhcnR4cmVmCjQ0NQolJUVPRgo=";
        public const string Rc4_128 = "JVBERi0xLjcKJcK1wrYKJSBXcml0dGVuIGJ5IE11UERGIDEuMjkuMAoKMSAwIG9iago8PC9UeXBlL0NhdGFsb2cvUGFnZXMgMiAwIFIvSW5mbzw8L1Byb2R1Y2VyPDVFMTgwODhDNDc5Q0JDMzIxN0RCNDM5Qz4+Pj4+CmVuZG9iagoKMiAwIG9iago8PC9UeXBlL1BhZ2VzL0NvdW50IDEvS2lkc1s0IDAgUl0+PgplbmRvYmoKCjMgMCBvYmoKPDw+PgplbmRvYmoKCjQgMCBvYmoKPDwvVHlwZS9QYWdlL01lZGlhQm94WzAgMCAxMDAgMTAwXS9Sb3RhdGUgMC9SZXNvdXJjZXMgMyAwIFIvUGFyZW50IDIgMCBSL0NvbnRlbnRzWzUgMCBSXT4+CmVuZG9iagoKNSAwIG9iago8PC9MZW5ndGggMzE+PgpzdHJlYW0K9BnKLFJBvlZ4H1BhxlePZLwTHprNpwRIhiw8rlCk8wplbmRzdHJlYW0KZW5kb2JqCgo2IDAgb2JqCjw8L1RpdGxlPEQ2NTJGQzE3MkEyMDA2OEMzQTlFOEI0NT4+PgplbmRvYmoKCnhyZWYKMCA3CjAwMDAwMDAwMDAgNjU1MzUgZiAKMDAwMDAwMDA0MiAwMDAwMCBuIAowMDAwMDAwMTMyIDAwMDAwIG4gCjAwMDAwMDAxODQgMDAwMDAgbiAKMDAwMDAwMDIwNSAwMDAwMCBuIAowMDAwMDAwMzEyIDAwMDAwIG4gCjAwMDAwMDAzOTIgMDAwMDAgbiAKCnRyYWlsZXIKPDwvU2l6ZSA3L0luZm8gNiAwIFIvUm9vdCAxIDAgUi9JRFs8QzM4QkMyQkFDMkEyMUFDMjhGQzJCRTIwNTEwQkMyOEQ+PDBBNUQ2QUFFNDg4Q0Q5OEY2Mzg2ODkxMEVGMTE1RDQ4Pl0vRW5jcnlwdDw8L0ZpbHRlci9TdGFuZGFyZC9SIDMvViAyL0xlbmd0aCAxMjgvUCAtNC9FbmNyeXB0TWV0YWRhdGEgdHJ1ZS9PPDU2NkZBODczRUUzM0M3OTdDRDNCOTA0RkRBREY4MTRBRkEzNERGOUEzOEY2RUQ0MUI5ODRFMkM2REEyQUE2RjU+L1U8ODkzMjU5OTIzNTkxRThBMjU5MjFDRENEMTE1QjM0RTIyOEJGNEU1RTRFNzU4QTQxNjQwMDRFNTZGRkZBMDEwOD4+Pj4+CnN0YXJ0eHJlZgo0NDUKJSVFT0YK";
        public const string Aes128 = "JVBERi0xLjcKJcK1wrYKJSBXcml0dGVuIGJ5IE11UERGIDEuMjkuMAoKMSAwIG9iago8PC9UeXBlL0NhdGFsb2cvUGFnZXMgMiAwIFIvSW5mbzw8L1Byb2R1Y2VyPDVEQTg0QTE5RjRFQjYyN0ExQjg3NjY0REI2RDA3OUIyMkI5QTg5RUU0NjhFODgxNDc5NkI2M0VBODhFQTNEQTE+Pj4+PgplbmRvYmoKCjIgMCBvYmoKPDwvVHlwZS9QYWdlcy9Db3VudCAxL0tpZHNbNCAwIFJdPj4KZW5kb2JqCgozIDAgb2JqCjw8Pj4KZW5kb2JqCgo0IDAgb2JqCjw8L1R5cGUvUGFnZS9NZWRpYUJveFswIDAgMTAwIDEwMF0vUm90YXRlIDAvUmVzb3VyY2VzIDMgMCBSL1BhcmVudCAyIDAgUi9Db250ZW50c1s1IDAgUl0+PgplbmRvYmoKCjUgMCBvYmoKPDwvTGVuZ3RoIDQ4Pj4Kc3RyZWFtCv8G5LQKHKzb8pTM3qWFFHV+jQPcpRsClCILxMrzSGjz0sWIiZUM7aeyNlUlHZPEaAplbmRzdHJlYW0KZW5kb2JqCgo2IDAgb2JqCjw8L1RpdGxlPDQ5QkU5RTRENEEwNUU4MTUwMjc4OTNCNTAzMTVCNzlGQ0NEMEQzMDdBMTIxMTQ4MUM4MTJDRERFODMwQUE5MEU+Pj4KZW5kb2JqCgp4cmVmCjAgNwowMDAwMDAwMDAwIDY1NTM1IGYgCjAwMDAwMDAwNDIgMDAwMDAgbiAKMDAwMDAwMDE3MiAwMDAwMCBuIAowMDAwMDAwMjI0IDAwMDAwIG4gCjAwMDAwMDAyNDUgMDAwMDAgbiAKMDAwMDAwMDM1MiAwMDAwMCBuIAowMDAwMDAwNDQ5IDAwMDAwIG4gCgp0cmFpbGVyCjw8L1NpemUgNy9JbmZvIDYgMCBSL1Jvb3QgMSAwIFIvSURbPDE5QzI4OEMzQTcxRkMyQjIxRjdCNjVDMzlBNDVDM0FFPjwzRDQwMDM0NUZDNDUxN0IzMDVERUM0M0JEN0I4MjM4ND5dL0VuY3J5cHQ8PC9GaWx0ZXIvU3RhbmRhcmQvUiA0L1YgNC9MZW5ndGggMTI4L1AgLTQvRW5jcnlwdE1ldGFkYXRhIHRydWUvU3RtRi9TdGRDRi9TdHJGL1N0ZENGL0NGPDwvU3RkQ0Y8PC9BdXRoRXZlbnQvRG9jT3Blbi9DRk0vQUVTVjIvTGVuZ3RoIDE2Pj4+Pi9PPDU2NkZBODczRUUzM0M3OTdDRDNCOTA0RkRBREY4MTRBRkEzNERGOUEzOEY2RUQ0MUI5ODRFMkM2REEyQUE2RjU+L1U8Qjc4MTI5MDVBMjY5OUJBRDVBQzhFQ0NBMTg5OTNBRTIyOEJGNEU1RTRFNzU4QTQxNjQwMDRFNTZGRkZBMDEwOD4+Pj4+CnN0YXJ0eHJlZgo1NDIKJSVFT0YK";
        public const string Aes256 = "JVBERi0xLjcKJcK1wrYKJSBXcml0dGVuIGJ5IE11UERGIDEuMjkuMAoKMSAwIG9iago8PC9UeXBlL0NhdGFsb2cvUGFnZXMgMiAwIFIvSW5mbzw8L1Byb2R1Y2VyPEQxNjJFOEVGQjNEQjc4NkNGMTAwMTU5QUE4ODNFREE4MUJCOTkzNEJDQTBBMzI4NkU2RUM4NzU2QjVCOUYxQ0Y+Pj4+PgplbmRvYmoKCjIgMCBvYmoKPDwvVHlwZS9QYWdlcy9Db3VudCAxL0tpZHNbNCAwIFJdPj4KZW5kb2JqCgozIDAgb2JqCjw8Pj4KZW5kb2JqCgo0IDAgb2JqCjw8L1R5cGUvUGFnZS9NZWRpYUJveFswIDAgMTAwIDEwMF0vUm90YXRlIDAvUmVzb3VyY2VzIDMgMCBSL1BhcmVudCAyIDAgUi9Db250ZW50c1s1IDAgUl0+PgplbmRvYmoKCjUgMCBvYmoKPDwvTGVuZ3RoIDQ4Pj4Kc3RyZWFtCndO0wss5PquDuVQR8ewdMWFVTNSTELfP6zESFYid0Q/qJcBfu2GBiUF6gg4Sn4GgQplbmRzdHJlYW0KZW5kb2JqCgo2IDAgb2JqCjw8L1RpdGxlPEVCMjM1NkU1Mjk3RTZBRTQwRDY1ODk0RjZGMDQ2NEY2NkI1RjhGN0ZFOTUxREQzODIwNEVERkYxQThCM0ZFODg+Pj4KZW5kb2JqCgp4cmVmCjAgNwowMDAwMDAwMDAwIDY1NTM1IGYgCjAwMDAwMDAwNDIgMDAwMDAgbiAKMDAwMDAwMDE3MiAwMDAwMCBuIAowMDAwMDAwMjI0IDAwMDAwIG4gCjAwMDAwMDAyNDUgMDAwMDAgbiAKMDAwMDAwMDM1MiAwMDAwMCBuIAowMDAwMDAwNDQ5IDAwMDAwIG4gCgp0cmFpbGVyCjw8L1NpemUgNy9JbmZvIDYgMCBSL1Jvb3QgMSAwIFIvSURbPDA0NTVDM0ExM0Q2NUMzQjJDMkFBQzI4QTYwQzM4QUMzPjw5MzZGQjdGRUE4NTEyM0JBQzMzRENCRkNBNTU2M0UwNz5dL0VuY3J5cHQ8PC9GaWx0ZXIvU3RhbmRhcmQvUiA2L1YgNS9MZW5ndGggMjU2L1AgLTQvRW5jcnlwdE1ldGFkYXRhIHRydWUvU3RtRi9TdGRDRi9TdHJGL1N0ZENGL0NGPDwvU3RkQ0Y8PC9BdXRoRXZlbnQvRG9jT3Blbi9DRk0vQUVTVjMvTGVuZ3RoIDMyPj4+Pi9PPDcxMzEyMUUyNThBOEY4NDJBMzJEN0VGNjg4MDhEMUE5QjI3QUZDRUExQTQ5QzM4NEE5MkFFMDMyRDNEMkZCQkQwRjJEOEJFNzRCMTIwNDMxQTVDRkFCMzVCNzVBNzY1Nz4vVTwwRTdBNDUxNTJGQkE4QjgxMkI0N0Y3REY2MDlENDk2MTA0M0Y4RUU1NkY1QTQyRUVGRkJBQjk0Mjk5NTZGMjAxQTcxQjdDQjNENTMyMzQ5MDhDNEFGNzQwMDBCNUIxQUU+L09FPDdGRUM0NUIxRjVBRDIwOUQwRTgzMTkxOEFFMkE0NEJBRDIzNTQ5QzQ0OEFFNThGOTgyOEMxQTZGQUM2MzgzNjc+L1VFPDcxMDU0RTUxRjAzMUEwODQ1QkEzNEIzODFEQkFENDE4NDU5OUZENDlDN0VFNzBFQ0RFRTg5MDFGMzQwNkMxMkQ+L1Blcm1zPDFGRDdCM0E1RjYyODM0OTBFQTFBNDNCMjlCOTYwMTM0Pj4+Pj4Kc3RhcnR4cmVmCjU0MgolJUVPRgo=";
        public const string NeedsPassword = "JVBERi0xLjcKJcK1wrYKJSBXcml0dGVuIGJ5IE11UERGIDEuMjkuMAoKMSAwIG9iago8PC9UeXBlL0NhdGFsb2cvUGFnZXMgMiAwIFIvSW5mbzw8L1Byb2R1Y2VyPDRDRjI4RjkxNkY1Q0ZDMzFBMDAzNjFDQzAzMzY2Qjk1M0EwQUY5OTg0NzE4RDk2Q0ZGNEFGMzY0MTExMjkxMDE+Pj4+PgplbmRvYmoKCjIgMCBvYmoKPDwvVHlwZS9QYWdlcy9Db3VudCAxL0tpZHNbNCAwIFJdPj4KZW5kb2JqCgozIDAgb2JqCjw8Pj4KZW5kb2JqCgo0IDAgb2JqCjw8L1R5cGUvUGFnZS9NZWRpYUJveFswIDAgMTAwIDEwMF0vUm90YXRlIDAvUmVzb3VyY2VzIDMgMCBSL1BhcmVudCAyIDAgUi9Db250ZW50c1s1IDAgUl0+PgplbmRvYmoKCjUgMCBvYmoKPDwvTGVuZ3RoIDQ4Pj4Kc3RyZWFtCkjmr6rWhu/KlAvsJFQT40pw2jm1aLdNLdhj1hgsUD9rAzv3v9SzvUElQkKHXBmkjAplbmRzdHJlYW0KZW5kb2JqCgo2IDAgb2JqCjw8L1RpdGxlPERDMjJBNTQ0OTEwNzU2QzFDMjVEQzI2OThFMkQ1NzRERDk3MkJFODJDRDhBREZCRDEyQ0I3MTRDNDFBNDlFRUM+Pj4KZW5kb2JqCgp4cmVmCjAgNwowMDAwMDAwMDAwIDY1NTM1IGYgCjAwMDAwMDAwNDIgMDAwMDAgbiAKMDAwMDAwMDE3MiAwMDAwMCBuIAowMDAwMDAwMjI0IDAwMDAwIG4gCjAwMDAwMDAyNDUgMDAwMDAgbiAKMDAwMDAwMDM1MiAwMDAwMCBuIAowMDAwMDAwNDQ5IDAwMDAwIG4gCgp0cmFpbGVyCjw8L1NpemUgNy9JbmZvIDYgMCBSL1Jvb3QgMSAwIFIvSURbPDdDNERDMkE1QzNCRjVFNUNDMkE0MTZDMjhCQzNCQ0MzPjwxNzU4OTJGODI0NTUyMTZEQzc0NzlGRDExNzUyQzQxNT5dL0VuY3J5cHQ8PC9GaWx0ZXIvU3RhbmRhcmQvUiA0L1YgNC9MZW5ndGggMTI4L1AgLTQvRW5jcnlwdE1ldGFkYXRhIHRydWUvU3RtRi9TdGRDRi9TdHJGL1N0ZENGL0NGPDwvU3RkQ0Y8PC9BdXRoRXZlbnQvRG9jT3Blbi9DRk0vQUVTVjIvTGVuZ3RoIDE2Pj4+Pi9PPDBEQjU4NTVGQzUzMjY1NjlFNzY1OTA2Q0FGNjRFNDQyOUE0QzIwRDZFOTk2RkRFRjk2M0U5QjUwODBGOUUwODM+L1U8MzI1QjYzNkI5MEYwNzY3N0M4REZDNTBGMTQ3N0MzRjkyOEJGNEU1RTRFNzU4QTQxNjQwMDRFNTZGRkZBMDEwOD4+Pj4+CnN0YXJ0eHJlZgo1NDIKJSVFT0YK";
        }

        private static byte[] Bytes(string base64) => Convert.FromBase64String(base64);

        [Theory]
        [InlineData(Fixtures.Rc4_40)]
        [InlineData(Fixtures.Rc4_128)]
        [InlineData(Fixtures.Aes128)]
        [InlineData(Fixtures.Aes256)]
        public void EncryptedFile_IsReadWithTheEmptyUserPassword(string base64)
        {
            var source = PdfPageSource.Open(Bytes(base64));
            using var canvas = new RasterRenderContext().CreateCanvas(100, 100);

            var unsupported = source.Paint(0, canvas);

            Assert.Empty(unsupported);
            Assert.Equal(1, source.PageCount);
            var pixels = canvas.ToPixelBuffer().PremultipliedRgba.Span;
            int Index(int x, int y) => (y * 100 + x) * 4;
            Assert.True(pixels[Index(20, 80)] > 200 && pixels[Index(20, 80) + 1] < 60 && pixels[Index(20, 80) + 3] == 255, "the red square decrypted and drew");
            Assert.Equal(0, pixels[Index(80, 20) + 3]);
        }

        [Theory]
        [InlineData(Fixtures.Rc4_40)]
        [InlineData(Fixtures.Rc4_128)]
        [InlineData(Fixtures.Aes128)]
        [InlineData(Fixtures.Aes256)]
        public void EncryptedStrings_AreDecrypted(string base64)
        {
            var doc = PdfSourceDocument.Open(Bytes(base64));
            var info = doc.DictOf(doc.Trailer, "Info");

            Assert.NotNull(info);
            Assert.Equal("Secret title", ((PdfString)doc.Resolve(info!.GetRaw("Title"))!).Value);
        }

        [Fact]
        public void FileThatNeedsAPassword_IsRefused()
        {
            var e = Assert.Throws<NotSupportedException>(() => PdfSourceDocument.Open(Bytes(Fixtures.NeedsPassword)));

            Assert.Contains("password", e.Message);
        }

        [Fact]
        public void FileWritten_ByThisLibrary_WithItsOwnEncryption_IsReadBack()
        {
            using var composer = new PdfComposer(new PdfRenderContext(), new PdfComposerOptions { Encryption = PdfEncryptionOptions.ReadOnly(null, "owner") });
            var canvas = composer.AddPage(100, 100).Canvas;
            canvas.DrawRectangle(canvas.GetSolidBrush(PeachDrawing.Core.PaintColor.FromArgb(255, 0, 0, 255)), 0, 0, 50, 50);
            using var ms = new MemoryStream();
            composer.Save(ms);

            var source = PdfPageSource.Open(ms.ToArray());
            using var raster = new RasterRenderContext().CreateCanvas(100, 100);
            source.Paint(0, raster);

            var span = raster.ToPixelBuffer().PremultipliedRgba.Span;
            Assert.True(span[(25 * 100 + 25) * 4 + 2] > 200);
        }

        [Fact]
        public void FileWritten_ByThisLibrary_WithAUserPassword_IsRefused()
        {
            using var composer = new PdfComposer(new PdfRenderContext(), new PdfComposerOptions { Encryption = PdfEncryptionOptions.ReadOnly("user", "owner") });
            composer.AddPage(100, 100);
            using var ms = new MemoryStream();
            composer.Save(ms);

            Assert.Throws<NotSupportedException>(() => PdfPageSource.Open(ms.ToArray()));
        }

        [Theory]
        [InlineData("<< /Filter /Other /V 1 /R 2 >>", "security handler other than the Standard")]
        [InlineData("<< /Filter /Standard /V 9 /R 2 >>", "is not supported")]
        [InlineData("<< /Filter /Standard /V 1 /R 7 >>", "is not supported")]
        public void UnknownHandlers_AreRefused(string encrypt, string message)
        {
            var pdf = TestPdf.Build("", extraObjects: [encrypt]);
            var text = System.Text.Encoding.Latin1.GetString(pdf).Replace("/Root 1 0 R", "/Root 1 0 R /Encrypt 5 0 R");

            var e = Assert.Throws<NotSupportedException>(() => PdfSourceDocument.Open(System.Text.Encoding.Latin1.GetBytes(text)));

            Assert.Contains(message, e.Message);
        }

        [Fact]
        public void Rc4_IsItsOwnInverse_AndMatchesTheKnownVector()
        {
            // RFC 6229 style check: key "Key", plaintext "Plaintext" gives BBF316E8D940AF0AD3.
            var cipher = PdfDecryptor.Rc4(System.Text.Encoding.ASCII.GetBytes("Key"), System.Text.Encoding.ASCII.GetBytes("Plaintext"));

            Assert.Equal("BBF316E8D940AF0AD3", Convert.ToHexString(cipher));
            Assert.Equal("Plaintext", System.Text.Encoding.ASCII.GetString(PdfDecryptor.Rc4(System.Text.Encoding.ASCII.GetBytes("Key"), cipher)));
        }
    }
}
