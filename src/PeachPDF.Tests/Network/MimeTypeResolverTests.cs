using System;
using PeachPDF.Network;

namespace PeachPDF.Tests.Network
{
    public class MimeTypeResolverTests
    {
        [Theory]
        [InlineData("page.html", "text/html")]
        [InlineData("PAGE.HTM", "text/html")]
        [InlineData("styles.css", "text/css")]
        [InlineData("icon.svg", "image/svg+xml")]
        [InlineData("photo.png", "image/png")]
        [InlineData("photo.jpg", "image/jpeg")]
        [InlineData("photo.jpeg", "image/jpeg")]
        [InlineData("photo.bmp", "image/bmp")]
        [InlineData("anim.gif", "image/gif")]
        [InlineData("tex.tga", "image/x-tga")]
        [InlineData("art.psd", "image/vnd.adobe.photoshop")]
        [InlineData("scene.hdr", "image/vnd.radiance")]
        [InlineData("font.ttf", "font/ttf")]
        [InlineData("font.otf", "font/otf")]
        [InlineData("font.woff", "font/woff")]
        [InlineData("font.woff2", "font/woff2")]
        public void StaticMap_ResolvesKnownExtensions(string extension, string expected)
        {
            var extensionNoDot = extension.Contains('.') ? extension[(extension.LastIndexOf('.') + 1)..].ToLowerInvariant() : extension;

            Assert.Equal(expected, MimeTypeResolver.TryGetFromStaticMap(extensionNoDot));
        }

        [Fact]
        public void StaticMap_UnknownExtension_ReturnsNull()
        {
            Assert.Null(MimeTypeResolver.TryGetFromStaticMap("xyz"));
        }

        [Fact]
        public void GetMimeType_NoExtension_ReturnsOctetStream()
        {
            Assert.Equal("application/octet-stream", MimeTypeResolver.GetMimeType("README"));
        }

        [Fact]
        public void GetMimeType_UnknownExtension_ReturnsOctetStream()
        {
            // A random extension no OS registers, so both the OS lookup and the static map miss.
            Assert.Equal("application/octet-stream", MimeTypeResolver.GetMimeType("data.peachpdfnope"));
        }

        [Theory]
        [InlineData("/some/dir/photo.png", "image/png")]
        [InlineData("styles.CSS", "text/css")]
        public void GetMimeType_KnownFormats_ResolveRegardlessOfOsRegistration(string path, string expected)
        {
            // These formats are guaranteed by the static fallback even if the OS lookup returns nothing.
            Assert.Equal(expected, MimeTypeResolver.GetMimeType(path));
        }

        [Theory]
        [InlineData("image/png", true)]
        [InlineData("text/css", true)]
        [InlineData("font/woff2", true)]
        // A Windows shell property descriptor, returned by AssocQueryString when no Content Type is
        // registered - must be rejected so it never becomes a Content-Type header.
        [InlineData("prop:System.ItemTypeText;System.Size;System.DateModified", false)]
        [InlineData("PROP:whatever", false)]
        [InlineData("image", false)]
        [InlineData("/leadingslash", false)]
        [InlineData("", false)]
        [InlineData("   ", false)]
        [InlineData(null, false)]
        public void LooksLikeMimeType_AcceptsRealMimeTypesRejectsJunk(string? value, bool expected)
        {
            Assert.Equal(expected, MimeTypeResolver.LooksLikeMimeType(value));
        }

        // ---- Platform-native lookups. Each runs only on its own OS (skipped elsewhere) and asserts a
        //      handful of MIME types that platform resolves. These verify the real OS integration and are
        //      not counted on for cross-platform coverage. On macOS/Linux the CI runner already resolves
        //      these types; on Windows a bare Server image registers no Content Type, so the CI workflow's
        //      "Ensure MIME content-type associations (Windows)" step registers exactly this asserted set. ----

        [Theory]
        [InlineData(".html", "text/html")]
        [InlineData(".css", "text/css")]
        [InlineData(".png", "image/png")]
        [InlineData(".jpg", "image/jpeg")]
        public void Windows_AssociationLookup_ResolvesCommonTypes(string extensionWithDot, string expected)
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Equal(expected, MimeTypeResolver.TryGetFromWindows(extensionWithDot));
            }
            else
            {
                Assert.Skip("Windows-only: exercises the AssocQueryString association API.");
            }
        }

        [Theory]
        [InlineData("html", "text/html")]
        [InlineData("css", "text/css")]
        [InlineData("png", "image/png")]
        [InlineData("jpg", "image/jpeg")]
        [InlineData("svg", "image/svg+xml")]
        public void Apple_UniformTypeIdentifierLookup_ResolvesCommonTypes(string extensionNoDot, string expected)
        {
            if (OperatingSystem.IsMacOS() || OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst() || OperatingSystem.IsTvOS())
            {
                Assert.Equal(expected, MimeTypeResolver.TryGetFromApple(extensionNoDot));
            }
            else
            {
                Assert.Skip("Apple-only: exercises the CoreServices Uniform Type Identifiers C API.");
            }
        }

        [Theory]
        [InlineData("html", "text/html")]
        [InlineData("css", "text/css")]
        [InlineData("png", "image/png")]
        [InlineData("svg", "image/svg+xml")]
        public void Linux_MimeTypesDatabaseLookup_ResolvesCommonTypes(string extensionNoDot, string expected)
        {
            if (!OperatingSystem.IsLinux())
            {
                Assert.Skip("Linux-only: exercises the /etc/mime.types database lookup.");
            }
            else if (!System.IO.File.Exists("/etc/mime.types"))
            {
                Assert.Skip("This Linux host has no /etc/mime.types database installed.");
            }
            else
            {
                Assert.Equal(expected, MimeTypeResolver.TryGetFromLinux(extensionNoDot));
            }
        }
    }
}
