using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PeachDrawing.Core;
using PeachDrawing.PDF.Tests.Import;

namespace PeachDrawing.PDF.Tests.Operations
{
    /// <summary>
    /// <see cref="PdfComposerOptions.Linearize"/>: the file has the layout of ISO 32000-1 Annex F, checked here from the bytes alone (the offsets the
    /// parameter dictionary promises) and by reading the file back. qpdf's own check was also run over these outputs when the writer was written.
    /// </summary>
    public class PdfLinearizeTests
    {
        private static string Text(byte[] pdf) => Encoding.Latin1.GetString(pdf);

        private static byte[] Compose(int pages, bool linearize, PdfEncryptionOptions? encryption = null)
        {
            using var composer = new PdfComposer(new PdfRenderContext(), new PdfComposerOptions
            {
                Linearize = linearize,
                CompressContentStreams = false,
                Encryption = encryption,
                Title = "Linearized",
            });
            for (var i = 0; i < pages; i++)
            {
                var page = composer.AddPage(100, 100);
                page.Canvas.DrawRectangle(page.Canvas.GetSolidBrush(PaintColor.FromArgb(255, i * 50 + 20, 0, 0)), 0, 0, 100, 100);
                page.AddLink(new Rect(10, 10, 20, 20), "https://example.com/" + i);
            }

            composer.AddBookmarks([new PdfBookmark(1, "Last", true) { PageIndex = pages - 1 }]);
            using var stream = new MemoryStream();
            composer.Save(stream);
            return stream.ToArray();
        }

        /// <summary>A file of <paramref name="pages"/> pages that all draw one form, which is therefore an object the pages share.</summary>
        private static byte[] WithSharedForm(int pages, bool linearize)
        {
            var form = TestPdf.Stream("/Type /XObject /Subtype /Form /BBox [0 0 100 100]", "0 0 1 rg 0 0 100 100 re f");
            var source = TestPdf.Build("/F Do", "/XObject << /F 5 0 R >>", extraObjects: form);
            var operation = PdfOperation.LoadBytes(source).TakePages(string.Join(',', Enumerable.Repeat("1", pages))).WithCopyMode(PdfCopyMode.ObjectCopy);
            return (linearize ? operation.Linearize() : operation).ToArray();
        }

        private static long Number(string text, string key) =>
            long.Parse(Regex.Match(text, key + @" (\d+)").Groups[1].Value, CultureInfo.InvariantCulture);

        private static (byte R, byte G, byte B) At(byte[] pdf, int page, string? password = null)
        {
            var source = PdfPageSource.Open(pdf, password);
            using var canvas = new RasterRenderContext().CreateCanvas(100, 100);
            source.Paint(page, canvas);
            var span = canvas.ToPixelBuffer().PremultipliedRgba.Span;
            var i = (50 * 100 + 50) * 4;
            return (span[i], span[i + 1], span[i + 2]);
        }

        [Fact]
        public void NotLinearized_ByDefault() => Assert.DoesNotContain("/Linearized", Text(Compose(2, linearize: false)));

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(5)]
        public void ParameterDictionary_DescribesTheFile(int pages)
        {
            var bytes = Compose(pages, linearize: true);
            var text = Text(bytes);

            // The first object after the header is the parameter dictionary.
            Assert.Matches(@"^%PDF-\d\.\d\n%[^\n]*\n\d+ 0 obj\n<< /Linearized 1 ", text);
            Assert.Equal(bytes.Length, Number(text, "/L"));
            Assert.Equal(pages, Number(text, "/N"));

            // /H: the hint stream's offset and length are exactly one object, whose dictionary says where its second table starts.
            var hint = Regex.Match(text, @"/H \[ (\d+) (\d+) \]");
            var hintOffset = int.Parse(hint.Groups[1].Value, CultureInfo.InvariantCulture);
            var hintLength = int.Parse(hint.Groups[2].Value, CultureInfo.InvariantCulture);
            Assert.Matches(@"^\d+ 0 obj\n<< /S \d+( /O \d+)? /Length \d+ >>\nstream\n", text[hintOffset..]);
            Assert.EndsWith("endstream\nendobj\n", text[hintOffset..(hintOffset + hintLength)]);

            // /E ends the first page's section, where the next object begins, and /O is the first page's object, which is in that section.
            var end = (int)Number(text, "/E");
            Assert.True(end > hintOffset + hintLength);
            if (pages > 1)
                Assert.Matches(@"^\d+ 0 obj\n", text[end..]);
            Assert.Matches(@"(?m)^" + Number(text, "/O") + @" 0 obj\n<<\n/Type /Page\b", text);

            // /T: the whitespace before the first entry of the main table, which starts at the free entry of object 0.
            var firstEntry = (int)Number(text, "/T");
            Assert.StartsWith("\n0000000000 65535 f", text[firstEntry..]);

            // The file's last startxref finds the first-page table, which follows the parameter dictionary and has its own trailer.
            var startxref = int.Parse(Regex.Match(text, @"startxref\n(\d+)\n%%EOF\n$").Groups[1].Value, CultureInfo.InvariantCulture);
            Assert.StartsWith("xref\n", text[startxref..]);
            Assert.Matches(@"/Prev \d{10}\b", text);
            Assert.Contains("startxref\n0\n%%EOF\n", text);
        }

        [Fact]
        public void EveryObjectIsWhereTheTablesSayItIs()
        {
            var bytes = Compose(4, linearize: true);
            var text = Text(bytes);

            var tables = Regex.Matches(text, @"xref\n(\d+) (\d+)\n((?:\d{10} \d{5} [nf] \n)+)");
            Assert.Equal(2, tables.Count);
            foreach (Match table in tables)
            {
                var number = int.Parse(table.Groups[1].Value, CultureInfo.InvariantCulture);
                foreach (var line in table.Groups[3].Value.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.EndsWith(" f ", StringComparison.Ordinal))
                    {
                        number++;
                        continue;
                    }

                    var offset = int.Parse(line[..10], CultureInfo.InvariantCulture);
                    Assert.StartsWith($"{number} 0 obj", text[offset..]);
                    number++;
                }
            }
        }

        [Fact]
        public void LinearizedFile_ReadsBackLikeTheOrdinaryOne()
        {
            var plain = Compose(4, linearize: false);
            var linear = Compose(4, linearize: true);

            var source = PdfPageSource.Open(linear);
            Assert.Equal(4, source.PageCount);
            for (var i = 0; i < 4; i++)
            {
                Assert.Equal(At(plain, i), At(linear, i));
                Assert.Equal("https://example.com/" + i, Assert.Single(source.GetLinks(i)).Uri);
            }

            Assert.Equal(3, Assert.Single(source.GetOutline()).PageIndex);
            Assert.Equal("Linearized", source.GetMetadata().Title);
        }

        [Fact]
        public void ObjectsSharedBetweenPages_AreKeptOnce_AndEveryPageStillDrawsThem()
        {
            var plain = WithSharedForm(3, linearize: false);
            var linear = WithSharedForm(3, linearize: true);

            Assert.Equal(3, PdfPageSource.Open(linear).PageCount);
            Assert.Equal(Count(Text(plain), "0 0 1 rg"), Count(Text(linear), "0 0 1 rg"));
            for (var i = 0; i < 3; i++)
            {
                var (r, _, b) = At(linear, i);
                Assert.True(b > 200 && r < 50);
            }
        }

        [Fact]
        public void SavingAgain_GivesAFileThatIsStillLinearizedAndReadable()
        {
            using var composer = new PdfComposer(new PdfRenderContext(), new PdfComposerOptions { Linearize = true, CompressContentStreams = false });
            composer.AddPage(100, 100);
            composer.AddPage(100, 100);

            using var first = new MemoryStream();
            composer.Save(first);
            using var second = new MemoryStream();
            composer.Save(second);

            Assert.Equal(2, PdfPageSource.Open(first.ToArray()).PageCount);
            Assert.Equal(2, PdfPageSource.Open(second.ToArray()).PageCount);
            Assert.Contains("/Linearized 1", Text(second.ToArray()));
        }

        [Fact]
        public void Encrypted_LinearizedFile_OpensWithItsPassword()
        {
            var bytes = Compose(3, linearize: true, new PdfEncryptionOptions { UserPassword = "pw", OwnerPassword = "ow" });

            Assert.Throws<PdfPasswordException>(() => PdfPageSource.Open(bytes));
            var source = PdfPageSource.Open(bytes, "pw");
            Assert.Equal(3, source.PageCount);
            Assert.Single(source.GetLinks(2));
            Assert.Equal(At(Compose(3, linearize: false), 1), At(bytes, 1, "pw"));
        }

        [Fact]
        public void Operation_Linearize_WritesALinearizedFile()
        {
            var result = PdfOperation.LoadBytes(Compose(2, linearize: false)).MergeBytes(Compose(2, linearize: false)).Linearize().ToArray();

            Assert.Contains("/Linearized 1", Text(result));
            Assert.Equal(4, PdfPageSource.Open(result).PageCount);
        }

        private static int Count(string text, string needle)
        {
            var count = 0;
            for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + 1, StringComparison.Ordinal))
                count++;
            return count;
        }
    }
}
