using System;
using System.IO;
using System.Threading.Tasks;
using PeachPDF.Network;

namespace PeachPDF.Tests.Network
{
    public class FileUriNetworkLoaderTests
    {
        [Fact]
        public void DefaultConstructor_BaseUri_IsCurrentDirectoryWithTrailingSlash()
        {
            var loader = new FileUriNetworkLoader();

            var baseUri = loader.BaseUri;

            Assert.NotNull(baseUri);
            Assert.Equal("file", baseUri!.Scheme);
            Assert.True(baseUri.IsAbsoluteUri);
            // A directory base must end in '/' so a relative reference resolves inside it.
            Assert.EndsWith("/", baseUri.AbsoluteUri);

            var expected = new Uri(EnsureTrailingSeparator(Directory.GetCurrentDirectory()));
            Assert.Equal(expected.AbsoluteUri, baseUri.AbsoluteUri);
        }

        [Fact]
        public void PrimaryFileConstructor_BaseUri_IsTheFilesOwnUri()
        {
            var path = CreateTempFile("page.html", "<html></html>");

            try
            {
                var loader = new FileUriNetworkLoader(path);

                Assert.NotNull(loader.BaseUri);
                Assert.Equal(new Uri(path).AbsoluteUri, loader.BaseUri!.AbsoluteUri);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task PrimaryFileConstructor_GetPrimaryContents_ReturnsFileText()
        {
            const string html = "<html><body>Hello File</body></html>";
            var path = CreateTempFile("page.html", html);

            try
            {
                var loader = new FileUriNetworkLoader(path);

                var contents = await loader.GetPrimaryContents();

                Assert.Equal(html, contents);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task DefaultConstructor_GetPrimaryContents_Throws()
        {
            var loader = new FileUriNetworkLoader();

            await Assert.ThrowsAsync<InvalidOperationException>(() => loader.GetPrimaryContents());
        }

        [Fact]
        public async Task GetResourceStream_ExistingFile_ReturnsStreamAndContentType()
        {
            var path = CreateTempFile("styles.css", "body { color: red; }");

            try
            {
                var loader = new FileUriNetworkLoader();
                var response = await loader.GetResourceStream(new RUri(new Uri(path)));

                Assert.NotNull(response);
                Assert.NotNull(response!.ResourceStream);

                Assert.NotNull(response.ResponseHeaders);
                Assert.True(response.ResponseHeaders!.TryGetValue("Content-Type", out var contentType));
                Assert.Equal("text/css", contentType![0]);

                using var reader = new StreamReader(response.ResourceStream!);
                Assert.Equal("body { color: red; }", await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task GetResourceStream_FileOpenFails_ReturnsNull()
        {
            var path = CreateTempFile("locked.css", "body { }");

            try
            {
                // Hold an exclusive handle (.NET enforces FileShare on every OS it targets) so the loader's
                // own open throws IOException - it must fail soft to null rather than propagate.
                using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    var loader = new FileUriNetworkLoader();
                    var response = await loader.GetResourceStream(new RUri(new Uri(path)));

                    Assert.Null(response);
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task GetResourceStream_MissingFile_ReturnsNull()
        {
            var path = Path.Combine(Path.GetTempPath(), "peachpdf-does-not-exist-" + Guid.NewGuid().ToString("N") + ".png");

            var loader = new FileUriNetworkLoader();
            var response = await loader.GetResourceStream(new RUri(new Uri(path)));

            Assert.Null(response);
        }

        [Fact]
        public async Task GetResourceStream_Directory_ReturnsNull()
        {
            var dir = EnsureTrailingSeparator(Path.GetTempPath());

            var loader = new FileUriNetworkLoader();
            var response = await loader.GetResourceStream(new RUri(new Uri(dir)));

            Assert.Null(response);
        }

        [Fact]
        public async Task GetResourceStream_NonFileScheme_ReturnsNull()
        {
            var loader = new FileUriNetworkLoader();

            var response = await loader.GetResourceStream(new RUri("https://example.com/photo.png"));

            Assert.Null(response);
        }

        [Fact]
        public async Task GetResourceStream_RelativeUri_ReturnsNull()
        {
            var loader = new FileUriNetworkLoader();

            var response = await loader.GetResourceStream(new RUri("photo.png", UriKind.Relative));

            Assert.Null(response);
        }

        private static string CreateTempFile(string name, string contents)
        {
            var dir = Path.Combine(Path.GetTempPath(), "peachpdf-fileloader-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, name);
            File.WriteAllText(path, contents);
            return path;
        }

        private static string EnsureTrailingSeparator(string directory) =>
            directory.EndsWith(Path.DirectorySeparatorChar) || directory.EndsWith(Path.AltDirectorySeparatorChar)
                ? directory
                : directory + Path.DirectorySeparatorChar;
    }
}
