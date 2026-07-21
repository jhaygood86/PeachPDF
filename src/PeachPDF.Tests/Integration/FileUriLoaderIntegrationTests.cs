using PeachPDF;
using PeachPDF.Network;
using PeachPDF.Tests.TestSupport;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end tests for the unified file-system loading path: a local HTML file and the resources it
    /// references (stylesheets, nested relative fonts, images) are served through
    /// <see cref="FileUriNetworkLoader"/> exactly like network resources, with no separate file-system
    /// code path. Also covers the two ways <c>file:</c> resources resolve without an explicit
    /// <see cref="FileUriNetworkLoader"/>: a <c>&lt;base href="file:///..."&gt;</c> element under the
    /// default loader, and the current-working-directory base default.
    /// </summary>
    public class FileUriLoaderIntegrationTests
    {
        // A real, loadable 1x1 pixel PNG (same constant used across the image integration tests).
        private const string PngBase64 =
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

        [Fact]
        public async Task PrimaryFile_WithRelativeImage_LoadsImageFromDisk()
        {
            var dir = CreateTempDir();

            try
            {
                File.WriteAllBytes(Path.Combine(dir, "pic.png"), Convert.FromBase64String(PngBase64));
                var pagePath = Path.Combine(dir, "page.html");
                File.WriteAllText(pagePath, "<!DOCTYPE html><html><body><img src=\"pic.png\"></body></html>");

                var config = new PdfGenerateConfig
                {
                    NetworkLoader = new FileUriNetworkLoader(pagePath),
                    PageSize = PageSize.A4
                };

                var doc = await new PdfGenerator().GeneratePdf(null, config);

                Assert.NotNull(doc);
                Assert.True(doc.PageCount >= 1);
                Assert.Contains("/Subtype /Image", GetPdfText(doc));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task PrimaryFile_WithLinkedStylesheetAndNestedRelativeFont_LoadsBothFromDisk()
        {
            var dir = CreateTempDir();

            try
            {
                // css/sheet.css references sub/font.ttf relative to its OWN location, so this proves the
                // linked stylesheet loaded from disk (text/css Content-Type gate) AND that a relative
                // reference inside it resolves against the stylesheet's file: location, not the document's.
                Directory.CreateDirectory(Path.Combine(dir, "css", "sub"));
                File.Copy(BundledFonts.Ttf, Path.Combine(dir, "css", "sub", "font.ttf"));
                File.WriteAllText(Path.Combine(dir, "css", "sheet.css"),
                    "@font-face { font-family: 'FileLoaderFont'; src: url('sub/font.ttf') format('truetype'); }\n" +
                    "body { font-family: 'FileLoaderFont', serif; }");

                var pagePath = Path.Combine(dir, "page.html");
                File.WriteAllText(pagePath,
                    "<!DOCTYPE html><html><head><link rel=\"stylesheet\" href=\"css/sheet.css\"></head><body>Hello File</body></html>");

                var config = new PdfGenerateConfig
                {
                    NetworkLoader = new FileUriNetworkLoader(pagePath),
                    PageSize = PageSize.A4
                };

                var doc = await new PdfGenerator().GeneratePdf(null, config);

                Assert.NotNull(doc);
                Assert.Contains("/FontFile2", GetPdfText(doc));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task BaseHrefFileUri_UnderDefaultLoader_RoutesFileResourcesThroughAdapter()
        {
            // No FileUriNetworkLoader is configured (the default DataUriNetworkLoader is used); the file:
            // image still loads because PdfSharpAdapter.GetResourceStream routes file: URIs internally,
            // the same way it always has for data: URIs.
            var dir = CreateTempDir();

            try
            {
                File.WriteAllBytes(Path.Combine(dir, "pic.png"), Convert.FromBase64String(PngBase64));
                var baseHref = new Uri(EnsureTrailingSeparator(dir)).AbsoluteUri;

                var html = $"<!DOCTYPE html><html><head><base href=\"{baseHref}\"></head>" +
                           "<body><img src=\"pic.png\"></body></html>";

                var config = new PdfGenerateConfig { PageSize = PageSize.A4 };

                var doc = await new PdfGenerator().GeneratePdf(html, config);

                Assert.NotNull(doc);
                Assert.Contains("/Subtype /Image", GetPdfText(doc));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task RelativeImage_WithNoLoader_LoadsFromCurrentWorkingDirectory()
        {
            // With no loader and no <base>, relative references resolve against the current working
            // directory as a file: URI (the historical implicit "load relative paths from disk"). Place
            // the asset under the real CWD instead of mutating Directory.SetCurrentDirectory, which would
            // race other tests running in parallel.
            var relDir = "peachpdf-cwd-" + Guid.NewGuid().ToString("N");
            var absDir = Path.Combine(Directory.GetCurrentDirectory(), relDir);
            Directory.CreateDirectory(absDir);

            try
            {
                File.WriteAllBytes(Path.Combine(absDir, "pic.png"), Convert.FromBase64String(PngBase64));

                var html = $"<!DOCTYPE html><html><body><img src=\"{relDir}/pic.png\"></body></html>";

                var config = new PdfGenerateConfig { PageSize = PageSize.A4 };

                var doc = await new PdfGenerator().GeneratePdf(html, config);

                Assert.NotNull(doc);
                Assert.Contains("/Subtype /Image", GetPdfText(doc));
            }
            finally
            {
                Directory.Delete(absDir, recursive: true);
            }
        }

        [Fact]
        public async Task Image_WithMalformedSrc_DoesNotThrow_AndSkipsImage()
        {
            // An invalid port makes URI resolution throw UriFormatException; the image source is reported
            // as invalid and skipped, and the rest of the document still renders (no /Subtype /Image).
            const string html = "<!DOCTYPE html><html><body><img src=\"http://host:notaport/\"></body></html>";

            var doc = await new PdfGenerator().GeneratePdf(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.NotNull(doc);
            Assert.True(doc.PageCount >= 1);
            Assert.DoesNotContain("/Subtype /Image", GetPdfText(doc));
        }

        [Fact]
        public async Task LinkedStylesheet_WithMalformedHref_DoesNotThrow_AndStillRenders()
        {
            // Same unresolvable reference on a <link> stylesheet: the handler reports it and returns empty
            // rather than throwing, so the document still renders.
            const string html = "<!DOCTYPE html><html><head>" +
                                 "<link rel=\"stylesheet\" href=\"http://host:notaport/\">" +
                                 "</head><body>Hello Malformed</body></html>";

            var doc = await new PdfGenerator().GeneratePdf(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.NotNull(doc);
            Assert.True(doc.PageCount >= 1);
        }

        private static string CreateTempDir()
        {
            var dir = Path.Combine(Path.GetTempPath(), "peachpdf-fileuri-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string EnsureTrailingSeparator(string directory) =>
            directory.EndsWith(Path.DirectorySeparatorChar) || directory.EndsWith(Path.AltDirectorySeparatorChar)
                ? directory
                : directory + Path.DirectorySeparatorChar;

        private static string GetPdfText(PeachPdfDocument doc)
        {
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }
    }
}
