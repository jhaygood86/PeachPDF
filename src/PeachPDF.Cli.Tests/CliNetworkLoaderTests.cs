using PeachPDF.Cli;
using PeachPDF.Network;

namespace PeachPDF.Cli.Tests;

/// <summary>
/// The loader only handles <c>http(s):</c>. <c>file:</c> and <c>data:</c> are resolved by PeachPDF's own
/// adapter, and <c>--no-local-files</c> is enforced there via
/// <see cref="PdfGenerateConfig.AllowLocalFileAccess"/> — see <c>CliRunnerIntegrationTests</c> for that.
/// </summary>
public class CliNetworkLoaderTests
{
    [Fact]
    public async Task FileResource_IsNotHandledByLoader()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "body { color: red; }");
            var loader = new CliNetworkLoader(new RUri(new Uri(path)), httpClient: null);

            var response = await loader.GetResourceStream(new RUri(new Uri(path)));

            Assert.Null(response);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task HttpResource_NullWhenNetworkDisabled()
    {
        var loader = new CliNetworkLoader(baseUri: null, httpClient: null);
        var response = await loader.GetResourceStream(new RUri(new Uri("https://example.com/style.css")));
        Assert.Null(response);
    }

    [Fact]
    public async Task DataUri_IsNotHandledByLoader()
    {
        // data: URIs are resolved by the adapter itself, so the loader returns null for them.
        var loader = new CliNetworkLoader(baseUri: null, httpClient: null);
        var response = await loader.GetResourceStream(new RUri("data:text/css,body{}"));
        Assert.Null(response);
    }

    [Fact]
    public void BaseUri_ReflectsProvidedValue()
    {
        var baseUri = new RUri(new Uri("https://example.com/docs/"));
        var loader = new CliNetworkLoader(baseUri, httpClient: null);
        Assert.Equal("https://example.com/docs/", loader.BaseUri!.AbsoluteUri);
    }

    [Fact]
    public async Task GetPrimaryContents_IsNotSupported()
    {
        // The CLI reads the root document itself, so this loader is only ever asked for referenced
        // resources - a call here would mean a caller had wired it up as the document source by mistake.
        var loader = new CliNetworkLoader(baseUri: null, httpClient: null);

        await Assert.ThrowsAsync<NotSupportedException>(() => loader.GetPrimaryContents());
    }
}
