using System.Net;
using System.Text;
using PeachPDF.Network;

namespace PeachPDF.Tests.Network
{
    public class UriNetworkLoaderTests
    {
        private sealed class StubHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<p>" + request.RequestUri!.AbsolutePath + "</p>") });
        }

        [Fact]
        public async Task HttpClientNetworkLoader_ExposesItsBaseUri_AndFetchesByUri()
        {
            using var client = new HttpClient(new StubHandler());
            var loader = new HttpClientNetworkLoader(client, "https://example.test/doc.html");

            Assert.Equal("https://example.test/doc.html", loader.BaseUri!.AbsoluteUri);
            Assert.Equal("<p>/doc.html</p>", await loader.GetPrimaryContents());
            var response = await loader.GetResourceStream(new Uri("https://example.test/a.css"));
            Assert.NotNull(response?.ResourceStream);
        }

        [Fact]
        public async Task MimeKitNetworkLoader_FindsAPartByItsContentLocation()
        {
            const string mhtml = "MIME-Version: 1.0\r\nContent-Type: multipart/related; boundary=\"b\"; type=\"text/html\"\r\n\r\n" +
                "--b\r\nContent-Type: text/html\r\nContent-Location: https://example.test/\r\n\r\n<p>hi</p>\r\n" +
                "--b\r\nContent-Type: text/css\r\nContent-Location: https://example.test/a.css\r\n\r\np{color:red}\r\n--b--\r\n";
            using var stream = new MemoryStream(Encoding.ASCII.GetBytes(mhtml));
            var loader = new MimeKitNetworkLoader(stream);

            var response = await loader.GetResourceStream(new Uri("https://example.test/a.css"));

            Assert.NotNull(response?.ResourceStream);
            using var reader = new StreamReader(response!.ResourceStream!);
            Assert.Contains("color:red", await reader.ReadToEndAsync());
        }
    }
}
