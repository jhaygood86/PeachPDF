using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Utils;
using System.IO;

namespace PeachPDF.Tests.PdfSharpCoreTests
{
    public class TtfFontDescriptionTests
    {
        private static string FirstSystemFont => FontResolver.SupportedFonts[0];

        [Fact]
        public void LoadFromFile_ReturnsNonEmptyFamilyAndFullName()
        {
            var desc = TtfFontDescription.LoadDescription(FirstSystemFont);

            Assert.NotEmpty(desc.FontFamilyInvariantCulture);
            Assert.NotEmpty(desc.FontNameInvariantCulture);
        }

        [Fact]
        public void LoadFromStream_MatchesLoadFromFile()
        {
            var path = FirstSystemFont;
            var fromFile = TtfFontDescription.LoadDescription(path);

            using var stream = File.OpenRead(path);
            var fromStream = TtfFontDescription.LoadDescription(stream);

            Assert.Equal(fromFile.FontFamilyInvariantCulture, fromStream.FontFamilyInvariantCulture);
            Assert.Equal(fromFile.FontNameInvariantCulture, fromStream.FontNameInvariantCulture);
            Assert.Equal(fromFile.Style, fromStream.Style);
        }

        [Fact]
        public void LoadFromFile_StyleIsValidEnum()
        {
            var desc = TtfFontDescription.LoadDescription(FirstSystemFont);

            Assert.True(
                desc.Style == XFontStyle.Regular ||
                desc.Style == XFontStyle.Bold ||
                desc.Style == XFontStyle.Italic ||
                desc.Style == XFontStyle.BoldItalic,
                $"Unexpected style value: {desc.Style}");
        }

        [Fact]
        public void LoadFromStream_InvalidData_ThrowsException()
        {
            var garbage = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 };
            using var stream = new MemoryStream(garbage);

            Assert.ThrowsAny<Exception>(() => TtfFontDescription.LoadDescription(stream));
        }

        [Fact]
        public void LoadFromFile_EmptyStream_ThrowsException()
        {
            using var stream = new MemoryStream([]);

            Assert.ThrowsAny<Exception>(() => TtfFontDescription.LoadDescription(stream));
        }

        [Fact]
        public void LoadFromFile_AllSupportedFonts_ParseWithoutError()
        {
            var failures = new List<(string path, string error)>();

            foreach (var path in FontResolver.SupportedFonts)
            {
                try
                {
                    var desc = TtfFontDescription.LoadDescription(path);
                    // Each parsed font must have non-empty names
                    Assert.NotEmpty(desc.FontFamilyInvariantCulture);
                }
                catch (Exception ex)
                {
                    failures.Add((path, ex.Message));
                }
            }

            Assert.Empty(failures);
        }
    }
}
