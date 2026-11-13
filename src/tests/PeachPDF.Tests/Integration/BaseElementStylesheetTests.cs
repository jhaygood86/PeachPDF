using PeachPDF.Network;
using PeachDrawing.Core;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A <c>&lt;base href&gt;</c> is the base for the document's stylesheets as well as its images and links, resolved against the host's own base
    /// address; one that is malformed is ignored rather than failing the render.
    /// </summary>
    public class BaseElementStylesheetTests
    {
        private sealed class RecordingLoader(RUri baseUri) : RNetworkLoader
        {
            public List<string> Requested { get; } = [];

            public override RUri? BaseUri { get; } = baseUri;

            public override Task<string> GetPrimaryContents() => throw new NotSupportedException();

            public override Task<RNetworkResponse?> GetResourceStream(RUri uri)
            {
                Requested.Add(uri.AbsoluteUri);
                return Task.FromResult<RNetworkResponse?>(null);
            }
        }

        private static async Task<RecordingLoader> RenderAsync(string baseElement)
        {
            var loader = new RecordingLoader(new RUri("http://host.test/doc/"));
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, NetworkLoader = loader };

            var pdf = await new PdfGenerator().GeneratePdf("<!doctype html><html><head>" + baseElement + "<link rel=stylesheet href='s.css'></head><body><p>x</p></body></html>", config);

            Assert.NotNull(pdf);
            return loader;
        }

        [Fact]
        public async Task ARelativeBaseElement_IsResolvedAgainstTheHostBaseAddressForStylesheets()
        {
            var loader = await RenderAsync("<base href='css/'>");

            Assert.Contains("http://host.test/doc/css/s.css", loader.Requested);
        }

        [Fact]
        public async Task AMalformedBaseElement_IsIgnoredAndTheStylesheetUsesTheHostBaseAddress()
        {
            var loader = await RenderAsync("<base href='http://[bad'>");

            Assert.Contains("http://host.test/doc/s.css", loader.Requested);
        }
    }
}
