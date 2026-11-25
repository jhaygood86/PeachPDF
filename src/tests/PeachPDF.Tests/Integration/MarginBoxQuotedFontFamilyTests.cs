using System.Text;

using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A quoted and an unquoted spelling of one <c>font-family</c> name select the same face in a page-margin box, as they do in the body.
    /// The body here deliberately uses the default font and only the margin box names the registered family, so the registered
    /// face's name appears in the PDF only when the margin box resolved it.
    /// </summary>
    public class MarginBoxQuotedFontFamilyTests
    {
        private const string RegisteredName = "Source#20Sans#203";

        [Theory]
        [InlineData("\"{F}\"", true)]
        [InlineData("'{F}'", true)]
        [InlineData("\"Source \\53 ans 3\"", true)]
        [InlineData("\"Missing, Family\", \"{F}\"", true)]
        [InlineData("\"Missing Family\"", false)]
        public async Task MarginBoxFontFamily_ResolvesTheRegisteredFace(string familyValue, bool expectRegistered)
        {
            var generator = new PdfGenerator();
            await using (var stream = File.OpenRead(BundledFonts.Ttf))
                await generator.AddFontFromStream(stream);

            familyValue = familyValue.Replace("{F}", TypefaceFixtures.FamilyNameOf(BundledFonts.Ttf));

            var html = "<!DOCTYPE html><html><head><style>" +
                       "@page { size: A4; margin: 2cm; @bottom-right { content: \"Footer \" counter(page); font-family: " + familyValue + "; } }" +
                       "</style></head><body><p>Body</p></body></html>";

            var doc = await generator.GeneratePdf(html, PageSize.A4);
            using var ms = new MemoryStream();
            doc.Save(ms);

            Assert.Equal(expectRegistered, Encoding.Latin1.GetString(ms.ToArray()).Contains(RegisteredName));
        }
    }
}
