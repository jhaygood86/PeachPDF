using PeachPDF;
using PeachPDF.Adapters;
using PeachPDF.Network;
using PeachPDF.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <see cref="PdfGenerateConfig.AllowLocalFileAccess"/> is the switch a host uses to refuse the local
    /// file system entirely - for untrusted input, or in a browser/WebAssembly app where there is no
    /// meaningful file system to reach. Each denial test has an allowed-variant twin, so a fixture that
    /// silently stopped exercising the feature would fail rather than pass vacuously.
    /// </summary>
    [Collection("CWD-Dependent")]
    public class LocalFileAccessTests
    {
        // A real, loadable 1x1 pixel PNG (same constant used across the image integration tests).
        private const string PngBase64 =
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

        [Fact]
        public void AllowLocalFileAccess_DefaultsToTrue()
        {
            Assert.True(new PdfGenerateConfig().AllowLocalFileAccess);
        }

        // ---- Adapter-level: the two places the flag is read ----------------------------------------

        [Fact]
        public void BaseUri_WhenAllowed_FallsBackToTheWorkingDirectory()
        {
            // The default DataUriNetworkLoader has no BaseUri of its own, so the working directory is what
            // relative references have historically resolved against.
            var adapter = new PdfSharpAdapter { AllowLocalFileAccess = true };

            Assert.NotNull(adapter.BaseUri);
            Assert.Equal("file", adapter.BaseUri!.Scheme);
        }

        [Fact]
        public void BaseUri_WhenDenied_IsNull()
        {
            var adapter = new PdfSharpAdapter { AllowLocalFileAccess = false };

            Assert.Null(adapter.BaseUri);
        }

        [Fact]
        public void BaseUri_WhenDenied_StillUsesTheLoadersOwnBase()
        {
            // The flag suppresses only the working-directory fallback. A loader that supplies its own base -
            // a zip or archive loader in a sandboxed host, say - keeps it, which is what lets such a host
            // resolve relative references without any file system at all.
            var adapter = new PdfSharpAdapter
            {
                AllowLocalFileAccess = false,
                NetworkLoader = new StubLoader(new RUri("https://example.invalid/doc/"))
            };

            Assert.NotNull(adapter.BaseUri);
            Assert.Equal("https", adapter.BaseUri!.Scheme);
        }

        [Fact]
        public async Task GetResourceStream_FileUri_WhenDenied_ReturnsNull()
        {
            var dir = CreateTempDir();

            try
            {
                var path = Path.Combine(dir, "pic.png");
                File.WriteAllBytes(path, Convert.FromBase64String(PngBase64));

                var adapter = new PdfSharpAdapter { AllowLocalFileAccess = false };

                Assert.Null(await adapter.GetResourceStream(new RUri(new Uri(path).AbsoluteUri)));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task GetResourceStream_FileUri_WhenAllowed_ReturnsTheStream()
        {
            var dir = CreateTempDir();

            try
            {
                var path = Path.Combine(dir, "pic.png");
                File.WriteAllBytes(path, Convert.FromBase64String(PngBase64));

                var adapter = new PdfSharpAdapter { AllowLocalFileAccess = true };

                var response = await adapter.GetResourceStream(new RUri(new Uri(path).AbsoluteUri));

                Assert.NotNull(response);
                Assert.NotNull(response!.ResourceStream);

                // The loader hands back an open FileStream; release it before the finally block tries to
                // delete the directory out from under it.
                response.ResourceStream!.Dispose();
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task GetResourceStream_RelativeUri_ReturnsNullWithoutThrowing()
        {
            // Denying local file access is what makes BaseUri nullable, and a null base means a relative
            // reference survives resolution still relative. Every loader has only ever been handed absolute
            // URIs - RUri.Scheme throws on a relative one, and DataUriNetworkLoader reads it unguarded - so
            // the adapter has to answer for this case itself rather than passing it on.
            var adapter = new PdfSharpAdapter { AllowLocalFileAccess = false };

            Assert.Null(await adapter.GetResourceStream(new RUri("logo.png", UriKind.Relative)));
        }

        // ---- End to end ---------------------------------------------------------------------------

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task FileUriImage_IsEmbeddedOnlyWhenAllowed(bool allowLocalFileAccess)
        {
            var dir = CreateTempDir();

            try
            {
                var imagePath = Path.Combine(dir, "pic.png");
                File.WriteAllBytes(imagePath, Convert.FromBase64String(PngBase64));

                var html = $"<!DOCTYPE html><html><body><img src=\"{new Uri(imagePath).AbsoluteUri}\"></body></html>";

                var doc = await new PdfGenerator().GeneratePdf(html, new PdfGenerateConfig
                {
                    PageSize = PageSize.A4,
                    AllowLocalFileAccess = allowLocalFileAccess
                });

                Assert.Equal(allowLocalFileAccess, GetPdfText(doc).Contains("/Subtype /Image"));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task RelativeImage_ResolvedAgainstTheWorkingDirectory_LoadsOnlyWhenAllowed(bool allowLocalFileAccess)
        {
            // With no loader configured a relative reference resolves against the process's working
            // directory - the implicit "load relative paths from disk" behaviour. Denying file access is
            // what stops that, and it stops it at the base-URI stage rather than at the read.
            var dir = CreateTempDir();
            var originalWorkingDirectory = Directory.GetCurrentDirectory();

            try
            {
                File.WriteAllBytes(Path.Combine(dir, "pic.png"), Convert.FromBase64String(PngBase64));
                Directory.SetCurrentDirectory(dir);

                var doc = await new PdfGenerator().GeneratePdf(
                    "<!DOCTYPE html><html><body><img src=\"pic.png\"></body></html>",
                    new PdfGenerateConfig { PageSize = PageSize.A4, AllowLocalFileAccess = allowLocalFileAccess });

                Assert.Equal(allowLocalFileAccess, GetPdfText(doc).Contains("/Subtype /Image"));
            }
            finally
            {
                Directory.SetCurrentDirectory(originalWorkingDirectory);
                Directory.Delete(dir, recursive: true);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task FileUriStylesheet_IsAppliedOnlyWhenAllowed(bool allowLocalFileAccess)
        {
            var dir = CreateTempDir();

            try
            {
                var cssPath = Path.Combine(dir, "sheet.css");
                File.WriteAllText(cssPath, "body { color: red; }");

                var html = "<!DOCTYPE html><html><head>" +
                           $"<link rel=\"stylesheet\" href=\"{new Uri(cssPath).AbsoluteUri}\">" +
                           "</head><body>Hello</body></html>";

                var doc = await new PdfGenerator().GeneratePdf(html, new PdfGenerateConfig
                {
                    PageSize = PageSize.A4,
                    AllowLocalFileAccess = allowLocalFileAccess,
                    // The fill operator lives in the page content stream, which is Flate-compressed by
                    // default and would not be findable in the raw bytes.
                    CompressContentStreams = false
                });

                // Named colors normalize to rgb() form, and red text is emitted as an "rg" fill operator.
                Assert.Equal(allowLocalFileAccess, GetPdfText(doc).Contains("1 0 0 rg"));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task FontFaceFileUrl_IsEmbeddedOnlyWhenAllowed(bool allowLocalFileAccess)
        {
            // @font-face src: url() resolves through the same adapter method as images and stylesheets, so
            // one flag covers it - this is the test that proves that rather than assuming it.
            var dir = CreateTempDir();

            try
            {
                var fontPath = Path.Combine(dir, "font.ttf");
                File.Copy(BundledFonts.Ttf, fontPath);

                var html = "<!DOCTYPE html><html><head><style>" +
                           "@font-face { font-family: 'LocalFileFont'; " +
                           $"src: url('{new Uri(fontPath).AbsoluteUri}') format('truetype'); }}" +
                           "body { font-family: 'LocalFileFont', serif; }" +
                           "</style></head><body>Hello</body></html>";

                var doc = await new PdfGenerator().GeneratePdf(html, new PdfGenerateConfig
                {
                    PageSize = PageSize.A4,
                    AllowLocalFileAccess = allowLocalFileAccess
                });

                // Asserting on this font's own name, not merely on "/FontFile2": the text renders either
                // way, so the fallback family is embedded even when the @font-face fails, and a
                // "/FontFile2" check would pass in both directions.
                // "Source#20Sans#203" is this font's family name as PDF writes it into /BaseFont (spaces
                // escaped). Asserting on the name rather than on "/FontFile2": the text renders either way,
                // so the fallback family is embedded even when the @font-face fails, and a "/FontFile2"
                // check would pass in both directions. The fallback's own name is platform-dependent; this
                // one is not.
                Assert.Equal(allowLocalFileAccess, GetPdfText(doc).Contains("Source#20Sans#203"));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task DenyingAccess_StillResolvesDataUris()
        {
            // Denying local file access makes the adapter's BaseUri null, which is the one thing that could
            // plausibly disturb resolution of references that never needed a base in the first place. A
            // data: URI is the whole content model for the sandboxed case, so it has to keep working.
            var html = $"<!DOCTYPE html><html><body><img src=\"data:image/png;base64,{PngBase64}\"></body></html>";

            var doc = await new PdfGenerator().GeneratePdf(html, new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                AllowLocalFileAccess = false
            });

            Assert.Contains("/Subtype /Image", GetPdfText(doc));
        }

        [Fact]
        public async Task DenyingAccess_BeatsAnExplicitFileUriNetworkLoader()
        {
            // The two settings contradict each other; refusing is the safe reading. The root document is
            // still read, because it is an input the caller named rather than something the document asked
            // for - which is the boundary the property documents.
            var dir = CreateTempDir();

            try
            {
                File.WriteAllBytes(Path.Combine(dir, "pic.png"), Convert.FromBase64String(PngBase64));
                var pagePath = Path.Combine(dir, "page.html");
                File.WriteAllText(pagePath,
                    "<!DOCTYPE html><html><body><p>Rendered anyway</p><img src=\"pic.png\"></body></html>");

                var doc = await new PdfGenerator().GeneratePdf(null, new PdfGenerateConfig
                {
                    NetworkLoader = new FileUriNetworkLoader(pagePath),
                    PageSize = PageSize.A4,
                    AllowLocalFileAccess = false
                });

                Assert.NotNull(doc);
                Assert.True(doc.PageCount >= 1);
                Assert.DoesNotContain("/Subtype /Image", GetPdfText(doc));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        private sealed class StubLoader(RUri? baseUri) : RNetworkLoader
        {
            public override RUri? BaseUri { get; } = baseUri;

            public override Task<string> GetPrimaryContents() => throw new NotSupportedException();

            public override Task<RNetworkResponse?> GetResourceStream(RUri uri) =>
                Task.FromResult<RNetworkResponse?>(null);
        }

        private static string CreateTempDir()
        {
            var dir = Path.Combine(Path.GetTempPath(), "peachpdf-localfile-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string GetPdfText(PeachPdfDocument doc)
        {
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }
    }
}
