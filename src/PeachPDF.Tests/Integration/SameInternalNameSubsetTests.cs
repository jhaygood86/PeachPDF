using PeachPDF;
using PeachPDF.PdfSharpCore.Utils;
using PeachPDF.Tests.TestSupport;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Two DIFFERENT font files that share one internal <c>name</c>-table name (the common webfont-subset
    /// pattern - e.g. every "Roboto" subset reports "Roboto") must be treated as distinct resources, the
    /// way a browser identifies a font by its bytes rather than its self-reported name. Before the
    /// content-addressed-identity fix, the second registration overwrote the first's bytes in the resolver
    /// and the two merged into a single embedded font.
    /// </summary>
    public class SameInternalNameSubsetTests
    {
        // A byte-identical clone with a trailing byte appended: the sfnt table directory addresses tables
        // by offset, so the extra byte is ignored by every table reader (the internal name is unchanged),
        // yet the whole-file content checksum differs - exactly "same name, different bytes".
        private static byte[] ByteVariant(byte[] original)
        {
            var variant = new byte[original.Length + 1];
            Array.Copy(original, variant, original.Length);
            variant[^1] = 0x7F;
            return variant;
        }

        [Fact]
        public void Resolver_TwoDifferentBytesUnderOneInternalName_ResolveToTheirOwnBytes()
        {
            var bytesA = File.ReadAllBytes(BundledFonts.Ttf);
            var bytesB = ByteVariant(bytesA);

            var internalName = TtfFontDescription.LoadDescription(new MemoryStream(bytesA)).FontNameInvariantCulture;
            Assert.Equal(internalName, TtfFontDescription.LoadDescription(new MemoryStream(bytesB)).FontNameInvariantCulture);

            var resolver = new FontResolver();
            resolver.AddFont(new MemoryStream(bytesA), "FamA");
            resolver.AddFont(new MemoryStream(bytesB), "FamB");

            var faceA = resolver.ResolveTypeface("FamA", 400, false).FaceName;
            var faceB = resolver.ResolveTypeface("FamB", 400, false).FaceName;

            // The two registrations must not collapse onto one face-name key...
            Assert.NotEqual(faceA, faceB);
            // ...and each must fetch back its own, correct bytes.
            Assert.Equal(bytesA, resolver.GetFont(faceA));
            Assert.Equal(bytesB, resolver.GetFont(faceB));

            // The first-registered face keeps the plain internal name (so every existing test that expects
            // FaceName == the file's internal name is preserved); only the colliding one is disambiguated.
            Assert.Equal(internalName, faceA);
            Assert.NotEqual(internalName, faceB);
        }

        [Fact]
        public async Task Embedding_TwoSameNameSubsetsUnderOneFamily_BothEmbedSeparately()
        {
            var bytesA = File.ReadAllBytes(BundledFonts.Ttf);
            var bytesB = ByteVariant(bytesA);
            var a64 = Convert.ToBase64String(bytesA);
            var b64 = Convert.ToBase64String(bytesB);

            // One CSS family, two same-internal-name faces split by disjoint unicode-range. The text draws
            // from both halves, so both faces are used - and must embed independently rather than merging.
            var html = $@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'Subset'; src: url('data:font/truetype;base64,{a64}') format('truetype'); unicode-range: U+41-5A; }}
@font-face {{ font-family: 'Subset'; src: url('data:font/truetype;base64,{b64}') format('truetype'); unicode-range: U+61-7A; }}
body {{ font-family: 'Subset'; font-size: 20pt; }}
</style></head>
<body>AaBbCc</body>
</html>";

            var generator = new PdfGenerator();
            var doc = await generator.GeneratePdf(html, PageSize.A4);
            var ms = new MemoryStream();
            doc.Save(ms);
            var pdf = Encoding.Latin1.GetString(ms.ToArray());

            // Both distinct-byte faces embed their own glyf program: a merge would leave only one.
            var fontFileCount = Regex.Matches(pdf, "/FontFile2").Count;
            Assert.True(fontFileCount >= 2, $"expected >= 2 embedded fonts, found {fontFileCount}");
        }

        [Fact]
        public async Task PublicApi_AddFontFromStream_WithUnicodeRanges_RestrictsCoverage()
        {
            // The programmatic equivalent of @font-face unicode-range: register a font by stream restricted
            // to uppercase, then a second font (unrestricted) as fallback under the same CSS family list.
            var upperName = TtfFontDescription.LoadDescription(new MemoryStream(File.ReadAllBytes(BundledFonts.Ttf))).FontFamilyInvariantCulture;
            var lowerName = TtfFontDescription.LoadDescription(new MemoryStream(File.ReadAllBytes(BundledFonts.Otf))).FontFamilyInvariantCulture;
            Assert.NotEqual(upperName, lowerName);

            var generator = new PdfGenerator();
            await generator.AddFontFromStream(File.OpenRead(BundledFonts.Ttf), [new RuneRange(new Rune('A'), new Rune('Z'))]);
            await generator.AddFontFromStream(File.OpenRead(BundledFonts.Otf));

            var html = $@"<!DOCTYPE html><html><head><style>
body {{ font-family: '{upperName}', '{lowerName}'; font-size: 20pt; }}
</style></head><body>Aa</body></html>";

            var doc = await generator.GeneratePdf(html, PageSize.A4);
            var ms = new MemoryStream();
            doc.Save(ms);
            var pdf = Encoding.Latin1.GetString(ms.ToArray());

            // The uppercase-restricted TTF embeds (used for 'A'); the OTF fallback embeds (used for 'a',
            // which is outside the first font's declared range).
            Assert.Contains("/FontFile2", pdf);
            Assert.Contains("/FontFile3", pdf);
        }
    }
}
