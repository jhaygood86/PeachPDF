using System.Text;
using System.Text.RegularExpressions;
using PeachDrawing.Core;

namespace PeachDrawing.PDF.Tests
{
    /// <summary>A document that names no creator is created by this package, and says so the same way in its Info and its XMP.</summary>
    public partial class PdfCreatorTests
    {
        static readonly DateTimeOffset Created = new(2026, 11, 23, 9, 0, 0, TimeSpan.Zero);

        [GeneratedRegex("<xmp:CreatorTool>([^<]*)</xmp:CreatorTool>")]
        private static partial Regex CreatorToolPattern();

        static byte[] Save(PdfComposerOptions options)
        {
            using var composer = new PdfComposer(new PdfRenderContext(), options);
            var page = composer.AddPage(100, 100);
            page.Canvas.DrawRectangle(page.Canvas.GetSolidBrush(PaintColor.FromArgb(255, 20, 120, 200)), 0, 0, 100, 100);
            using var stream = new MemoryStream();
            composer.Save(stream);
            return stream.ToArray();
        }

        [Fact]
        public void ADocumentThatNamesNoCreator_IsCreatedByThisPackage_NotByAnOldEngine()
        {
            var file = Save(new PdfComposerOptions());

            var metadata = PdfPageSource.Open(file).GetMetadata();

            Assert.Matches(@"^PeachDrawing\.PDF \d+\.\d+\.\d+", metadata.Creator);
            Assert.Equal(metadata.Producer, metadata.Creator);                        // the same package made it, and says so once
            var text = Encoding.Latin1.GetString(file);
            Assert.DoesNotContain("PDFsharp", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("PdfSharpCore", text, StringComparison.OrdinalIgnoreCase);
            // The Info strings may be hexadecimal UTF-16; the old name must not be in that form either.
            Assert.DoesNotContain(Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes("PDFsharp")), text.Replace("\n", "").Replace("\r", ""), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ANamedCreator_IsKept()
        {
            var metadata = PdfPageSource.Open(Save(new PdfComposerOptions { Creator = "My Application 2.0", Producer = "My Producer" })).GetMetadata();

            Assert.Equal("My Application 2.0", metadata.Creator);
            Assert.Equal("My Producer", metadata.Producer);
        }

        [Theory]
        [InlineData(PdfAConformance.PdfA1B)]
        [InlineData(PdfAConformance.PdfA2B)]
        [InlineData(PdfAConformance.PdfA3B)]
        [InlineData(PdfAConformance.PdfA4)]
        public void AnArchivalFileWithNoCreator_HasTheSameCreatorInItsXmp(PdfAConformance level)
        {
            var file = Save(new PdfComposerOptions { Conformance = level, Title = "T", CreationDate = Created });
            var text = Encoding.Latin1.GetString(file);

            // ISO 19005-1 6.7.3: the XMP CreatorTool and the Info /Creator are equivalent. PDF/A-4 has no Info dictionary, so there the
            // XMP is the only place, and it names the package.
            var tool = CreatorToolPattern().Match(text);
            Assert.True(tool.Success, "the XMP names a CreatorTool");
            Assert.Matches(@"^PeachDrawing\.PDF \d+\.\d+\.\d+", tool.Groups[1].Value);
            if (level != PdfAConformance.PdfA4)
                Assert.Equal(PdfPageSource.Open(file).GetMetadata().Creator, tool.Groups[1].Value);
        }

        [Fact]
        public void ANamedCreatorIsTheXmpCreatorToo()
        {
            var file = Save(new PdfComposerOptions { Conformance = PdfAConformance.PdfA1B, Title = "T", CreationDate = Created, Creator = "My Application 2.0" });

            Assert.Equal("My Application 2.0", CreatorToolPattern().Match(Encoding.Latin1.GetString(file)).Groups[1].Value);
            Assert.Equal("My Application 2.0", PdfPageSource.Open(file).GetMetadata().Creator);
        }

        [Fact]
        public void ApplyingOptionsAgain_KeepsTheDefault_UnlessACreatorIsNamed()
        {
            using var composer = new PdfComposer(new PdfRenderContext(), new PdfComposerOptions { Title = "first" });
            composer.AddPage(100, 100);
            composer.Configure(new PdfComposerOptions { Title = "second" });
            using var kept = new MemoryStream();
            composer.Save(kept);
            Assert.Matches(@"^PeachDrawing\.PDF ", PdfPageSource.Open(kept.ToArray()).GetMetadata().Creator);

            composer.Configure(new PdfComposerOptions { Creator = "Named later" });
            using var replaced = new MemoryStream();
            composer.Save(replaced);
            Assert.Equal("Named later", PdfPageSource.Open(replaced.ToArray()).GetMetadata().Creator);
        }
    }
}
