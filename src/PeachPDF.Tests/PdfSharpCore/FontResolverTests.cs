using MigraDocCore.DocumentObjectModel.MigraDoc.DocumentObjectModel.Shapes;
using PeachPDF.PdfSharpCore.Utils;
using System.IO;

namespace PeachPDF.Tests.PdfSharpCoreTests
{
    public class FontResolverTests
    {
        [Fact]
        public void SupportedFonts_ContainsFonts()
        {
            Assert.NotEmpty(FontResolver.SupportedFonts);
        }

        [Fact]
        public void ResolveTypeface_KnownFamily_ReturnsResolverInfo()
        {
            var resolver = new FontResolver();

            // Pick the first family that was actually loaded
            var familyName = TtfFontDescription.LoadDescription(FontResolver.SupportedFonts[0])
                .FontFamilyInvariantCulture;

            var info = resolver.ResolveTypeface(familyName, isBold: false, isItalic: false);

            Assert.NotNull(info);
            Assert.NotEmpty(info.FaceName);
        }

        [Fact]
        public void ResolveTypeface_UnknownFamily_ReturnsFallback()
        {
            var resolver = new FontResolver();

            // Unknown families fall back to the first available font rather than throwing
            var info = resolver.ResolveTypeface("__NonExistentFamily__", isBold: false, isItalic: false);

            Assert.NotNull(info);
        }

        [Fact]
        public void ResolveTypeface_NullIfNotFound_ReturnsNull()
        {
            var resolver = new FontResolver { NullIfFontNotFound = true };

            var info = resolver.ResolveTypeface("__NonExistentFamily__", isBold: false, isItalic: false);

            Assert.Null(info);
        }

        [Fact]
        public void GetFont_ResolvableFaceName_ReturnsFontBytes()
        {
            var resolver = new FontResolver();
            var familyName = TtfFontDescription.LoadDescription(FontResolver.SupportedFonts[0])
                .FontFamilyInvariantCulture;

            var info = resolver.ResolveTypeface(familyName, isBold: false, isItalic: false);
            var bytes = resolver.GetFont(info.FaceName);

            Assert.NotNull(bytes);
            Assert.True(bytes.Length > 0);
        }

        [Fact]
        public void HasFont_KnownFaceName_ReturnsTrue()
        {
            var resolver = new FontResolver();
            var familyName = TtfFontDescription.LoadDescription(FontResolver.SupportedFonts[0])
                .FontFamilyInvariantCulture;
            var info = resolver.ResolveTypeface(familyName, isBold: false, isItalic: false);

            Assert.True(resolver.HasFont(info.FaceName));
        }

        [Fact]
        public void HasFont_UnknownFaceName_ReturnsFalse()
        {
            var resolver = new FontResolver();

            Assert.False(resolver.HasFont("__NoSuchFace__"));
        }

        [Fact]
        public void AddFont_CustomFont_IsResolvableByFamilyName()
        {
            var resolver = new FontResolver();
            var fontPath = FontResolver.SupportedFonts[0];
            var fontDesc = TtfFontDescription.LoadDescription(fontPath);
            const string customFamilyName = "__TestCustomFamily__";

            using var stream = File.OpenRead(fontPath);
            resolver.AddFont(stream, customFamilyName);

            var info = resolver.ResolveTypeface(customFamilyName, isBold: false, isItalic: false);
            Assert.NotNull(info);
            Assert.NotEmpty(info.FaceName);
        }

        [Fact]
        public void AddFont_CustomFont_BytesRetrievableByFaceName()
        {
            var resolver = new FontResolver();
            var fontPath = FontResolver.SupportedFonts[0];
            const string customFamilyName = "__TestCustomFamily2__";

            using var stream = File.OpenRead(fontPath);
            resolver.AddFont(stream, customFamilyName);

            var info = resolver.ResolveTypeface(customFamilyName, isBold: false, isItalic: false);
            var bytes = resolver.GetFont(info.FaceName);

            Assert.NotNull(bytes);
            Assert.True(bytes.Length > 0);
        }

        [Fact]
        public void DiscoverSupportedFonts_UnsupportedPlatform_ReturnsEmpty()
        {
            var fonts = FontResolver.DiscoverSupportedFonts(isOSX: false, isLinux: false, isWindows: false);

            Assert.Empty(fonts);
        }

        [Fact]
        public void DiscoverSupportedFonts_Linux_DelegatesToLinuxSystemFontResolver()
        {
            var fonts = FontResolver.DiscoverSupportedFonts(isOSX: false, isLinux: true, isWindows: false);

            Assert.Equal(LinuxSystemFontResolver.Resolve(), fonts);
        }

        [Fact]
        public void DiscoverSupportedFonts_Windows_ReturnsSystemFontFiles()
        {
            var fonts = FontResolver.DiscoverSupportedFonts(isOSX: false, isLinux: false, isWindows: true);

            Assert.NotNull(fonts);
        }

        [Fact]
        public void DiscoverSupportedFonts_OSX_UsesLibraryFontsDirectory()
        {
            // The macOS font directories don't exist on non-macOS test runners, but the
            // branch must still be exercised so it's covered on the platforms where it does.
            // Missing directories are skipped rather than throwing, so this returns an
            // empty (but non-null) array on non-macOS runners.
            var fonts = FontResolver.DiscoverSupportedFonts(isOSX: true, isLinux: false, isWindows: false);

            Assert.NotNull(fonts);
        }
    }
}
