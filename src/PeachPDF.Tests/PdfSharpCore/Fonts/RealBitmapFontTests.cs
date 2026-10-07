using PeachDrawing.Text;
using PeachPDF.Tests.TestSupport;
using System.Text;

namespace PeachPDF.Tests.PdfSharpCoreTests.Fonts
{
    /// <summary>
    /// Bitmap colour glyphs from two real, third-party fonts (Simon Cozens' test-fonts, Apache-2.0; see
    /// <c>assets/fonts/SimonCozensTestFonts.LICENSE.txt</c>): <c>CBDT.otf</c> (a CBDT/CBLC format 17 picture) and <c>CFF-and-SBIX.otf</c>
    /// (sbix <c>png </c> pictures at four sizes over CFF outlines). The synthetic fixtures in <see cref="BitmapGlyphTests"/> cover the
    /// table variants; these prove the same code reads fonts a real tool produced.
    /// </summary>
    public class RealBitmapFontTests
    {
        [Fact]
        public void Cbdt_ExposesThePictureOfA_AndNoneForB()
        {
            Typeface face = TypefaceFixtures.Shared(BundledFonts.RealCbdt);
            Assert.True(face.HasBitmapGlyphs);

            Assert.True(face.TryGetBitmap((ushort)face.GlyphOf(new Rune('A')), 32, out var picture));
            Assert.True(picture.Width > 0 && picture.Height > 0);
            Assert.Equal(0x89, picture.Data[0]); // a PNG
            Assert.False(face.TryGetBitmap((ushort)face.GlyphOf(new Rune('B')), 32, out _));
        }

        [Fact]
        public void Sbix_PicksTheSmallestStrikeAtLeastAsLargeAsTheText()
        {
            Typeface face = TypefaceFixtures.Shared(BundledFonts.RealSbix);
            Assert.True(face.HasBitmapGlyphs);
            ushort a = (ushort)face.GlyphOf(new Rune('A'));

            Assert.True(face.TryGetBitmap(a, 100, out var medium));
            Assert.Equal(128, medium.Ppem);
            Assert.True(face.TryGetBitmap(a, 20, out var small));
            Assert.Equal(64, small.Ppem);
            Assert.True(face.TryGetBitmap(a, 1000, out var large));
            Assert.Equal(512, large.Ppem);
            Assert.False(face.TryGetBitmap((ushort)face.GlyphOf(new Rune('B')), 100, out _));
        }

        [Theory]
        [InlineData("cbdt")]
        [InlineData("sbix")]
        public async Task RealBitmapFont_EmbedsItsPictureInAPdf(string which)
        {
            string path = which == "cbdt" ? BundledFonts.RealCbdt : BundledFonts.RealSbix;
            var generator = new PdfGenerator();
            await using (var stream = File.OpenRead(path))
                await generator.AddFontFromStream(stream);

            string family = TypefaceFixtures.FamilyNameOf(path);
            string html = $"<!DOCTYPE html><html><head><style>body {{ font-family: '{family}'; font-size: 80pt; }}</style></head><body>A</body></html>";
            var doc = await generator.GeneratePdf(html, new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false });
            var ms = new MemoryStream();
            doc.Save(ms);

            Assert.Contains("/Subtype /Image", Encoding.Latin1.GetString(ms.ToArray()));
        }
    }
}
