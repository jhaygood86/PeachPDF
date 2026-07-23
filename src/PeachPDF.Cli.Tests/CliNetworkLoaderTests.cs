using PeachPDF.Cli;
using PeachPDF.Network;

namespace PeachPDF.Cli.Tests;

public class CliNetworkLoaderTests
{
    [Fact]
    public async Task FileResource_ReadWhenLocalFilesAllowed()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "body { color: red; }");
            var loader = new CliNetworkLoader(new RUri(new Uri(path)), httpClient: null, allowLocalFiles: true);

            var response = await loader.GetResourceStream(new RUri(new Uri(path)));

            Assert.NotNull(response);
            Assert.NotNull(response!.ResourceStream);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task FileResource_RefusedWhenLocalFilesDisabled()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "body { color: red; }");
            var loader = new CliNetworkLoader(new RUri(new Uri(path)), httpClient: null, allowLocalFiles: false);

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
        var loader = new CliNetworkLoader(baseUri: null, httpClient: null, allowLocalFiles: true);
        var response = await loader.GetResourceStream(new RUri(new Uri("https://example.com/style.css")));
        Assert.Null(response);
    }

    [Fact]
    public async Task DataUri_IsNotHandledByLoader()
    {
        // data: URIs are resolved by the adapter itself, so the loader returns null for them.
        var loader = new CliNetworkLoader(baseUri: null, httpClient: null, allowLocalFiles: true);
        var response = await loader.GetResourceStream(new RUri("data:text/css,body{}"));
        Assert.Null(response);
    }

    [Fact]
    public void BaseUri_ReflectsProvidedValue()
    {
        var baseUri = new RUri(new Uri("https://example.com/docs/"));
        var loader = new CliNetworkLoader(baseUri, httpClient: null, allowLocalFiles: true);
        Assert.Equal("https://example.com/docs/", loader.BaseUri!.AbsoluteUri);
    }
}
