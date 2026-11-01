using PeachDrawing.Text.Internal.Fonts;
using PeachPDF.Tests.TestSupport;
using System;
using System.IO;
using Xunit;

namespace PeachDrawing.Text.Tests.Fonts
{
    /// <summary>
    /// Two DIFFERENT font files that share one internal <c>name</c>-table name (the common webfont-subset
    /// pattern - e.g. every "Roboto" subset reports "Roboto") must be treated as distinct resources by the
    /// font resolver, the way a browser identifies a font by its bytes rather than its self-reported name. Before
    /// the content-addressed-identity fix, the second registration overwrote the first's bytes in the resolver
    /// and the two merged into a single embedded font. (The end-to-end embedding of two such faces is covered by
    /// <c>SameInternalNameSubsetTests</c> in PeachPDF.Tests.)
    /// </summary>
    public class SameInternalNameResolverTests
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
    }
}
