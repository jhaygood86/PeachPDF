using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PeachPDF.Tests.PdfEngineTests.Encodings
{
    /// <summary>A long run of text is shown as one hexadecimal glyph literal of the right length.</summary>
    public class GlyphRunLiteralIntegrationTests
    {
        [Theory]
        [InlineData(20)]
        [InlineData(300)]
        public async Task ARunOfAnyLengthIsShownAsOneLiteralOfItsGlyphs(int letters)
        {
            var generator = new PdfGenerator();
            var document = await generator.GeneratePdf(
                $"<html><body style='margin:0'><p style='white-space: nowrap; font-size: 6pt'>{new string('m', letters)}</p></body></html>",
                new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false });

            var content = new StringBuilder();
            content.Append(document.Inspect().ContentText(document.Inspect().Pages[0]));

            var literals = Regex.Matches(content.ToString(), @"<([0-9A-F\n]+)>\s*Tj")
                .Select(match => match.Groups[1].Value.Replace("\n", string.Empty).Length / 4)
                .ToList();

            Assert.Equal(letters, literals.Max());
        }
    }
}
