using PeachDrawing.Text.Internal.Fonts;
using PeachDrawing.Text.Internal.Fonts.OpenType;
using PeachPDF.Tests.TestSupport;
using System.IO;
using Xunit;

namespace PeachDrawing.Text.Tests.Fonts
{
    /// <summary>
    /// The font engine's own infrastructure - the font-file bytes/identity type, its content hash and the descriptor
    /// cache - which used to live inside the PDF writer's namespaces and is now owned by <c>PeachDrawing.Text.Internal.Fonts</c>.
    /// The simple-font <c>/Widths</c> table, which sits beside the PDF font objects, is tested in PeachPDF.Tests.
    /// </summary>
    public class FontEngineInfrastructureTests
    {
        [Fact]
        public void ContentHash_RejectsNull()
        {
            Assert.Throws<ArgumentNullException>(() => FontContentHash.Compute(null!));
            Assert.Throws<ArgumentNullException>(() => FontFileData.GetOrComputeHash(null!));
        }

        [Fact]
        public void ContentHash_IsTheFirst128BitsOfTheSha256OfTheBytes()
        {
            // SHA-256 of {1,2,3} is 039058c6f2c0cb492c533b0a4d14ef77cc0f78abccced5287d84a1a2011cfb81.
            Assert.Equal("039058c6f2c0cb492c533b0a4d14ef77", FontContentHash.Compute([1, 2, 3]).ToString());
        }

        [Fact]
        public void CompiledFont_ComputesItsKeyFromItsBytesOnDemand()
        {
            byte[] bytes = File.ReadAllBytes(BundledFonts.Ttf);

            var compiled = FontFileData.CreateCompiledFont(bytes);

            Assert.Equal(FontContentHash.Compute(bytes), compiled.Key);
        }

        [Fact]
        public void FontFileData_IdentityIsTheContentKey()
        {
            byte[] bytes = File.ReadAllBytes(BundledFonts.Ttf);
            byte[] copy = (byte[])bytes.Clone();

            var a = FontFileData.GetOrCreateFrom(bytes);
            var sameBytesElsewhere = FontFileData.CreateCompiledFont(copy);
            var other = FontFileData.CreateCompiledFont(File.ReadAllBytes(BundledFonts.Otf));

            Assert.Same(a, FontFileData.GetOrCreateFrom(bytes));
            Assert.True(a.Equals(sameBytesElsewhere));
            Assert.Equal(a.GetHashCode(), sameBytesElsewhere.GetHashCode());
            Assert.False(a.Equals(other));
            Assert.False(a.Equals(null));
            Assert.False(a.Equals("not a font"));
            Assert.Contains("FontFileData", a.DebuggerDisplay);
        }

        [Fact]
        public void OpenTypeFontface_SourceKeyIsTheHashOfItsFileBytes()
        {
            byte[] bytes = File.ReadAllBytes(BundledFonts.Ttf);
            var face = new OpenTypeFontface(FontFileData.CreateCompiledFont(bytes));

            Assert.Equal(FontContentHash.Compute(bytes), face.FontSource.Key);
        }

        [Fact]
        public void ResolveTypeface_WithoutACustomResolver_ResolvesNothing()
        {
            // PeachPDF has no platform resolver of its own: only an installed IFontResolver can resolve a family.
            var info = FontFactory.ResolveTypeface("NoResolverFamily-" + Guid.NewGuid().ToString("N"),
                new FontResolvingOptions(FaceStyle.Regular), "no-resolver-key-" + Guid.NewGuid().ToString("N"), fontResolver: null!);

            Assert.Null(info);
        }

        [Fact]
        public void Typeface_IsCachedPerFamilyAndStyle_AndOwnsExactlyOneDescriptor()
        {
            string family = "TypefaceCacheFamily-" + Guid.NewGuid().ToString("N");
            var resolver = new FontResolver();
            using (var stream = File.OpenRead(BundledFonts.Ttf))
                resolver.AddFont(stream, family);

            var first = LoadedTypeface.GetOrCreateFrom(family, new FontResolvingOptions(FaceStyle.Regular), resolver);
            var second = LoadedTypeface.GetOrCreateFrom(family, new FontResolvingOptions(FaceStyle.Regular), resolver);

            Assert.Same(first, second);
            Assert.Same(first.Descriptor, second.Descriptor);
            Assert.True(first.Descriptor.UnitsPerEm > 0);
            Assert.Equal(first.FamilyName, first.Descriptor.FontName);
        }

        [Fact]
        public void Typeface_OfACustomFamily_IsNotSharedWithAnotherResolverInstance()
        {
            string family = "TypefaceIsolationFamily-" + Guid.NewGuid().ToString("N");
            var a = new FontResolver();
            var b = new FontResolver();
            foreach (var (resolver, path) in new[] { (a, BundledFonts.Ttf), (b, BundledFonts.Otf) })
            {
                using var stream = File.OpenRead(path);
                resolver.AddFont(stream, family);
            }

            var fromA = LoadedTypeface.GetOrCreateFrom(family, new FontResolvingOptions(FaceStyle.Regular), a);
            var fromB = LoadedTypeface.GetOrCreateFrom(family, new FontResolvingOptions(FaceStyle.Regular), b);

            Assert.NotSame(fromA, fromB);
            Assert.NotSame(fromA.Descriptor, fromB.Descriptor);
            Assert.NotEqual(fromA.FontSource.Key, fromB.FontSource.Key);
        }
    }
}
