using PeachPDF.Network;

namespace PeachPDF.Tests.Network;

/// <summary>An HttpClient follows redirects itself; the loader says where the body came from so a document can tell a redirect to another origin.</summary>
public class HttpRedirectAddressTests
{
    /// <summary>Answers every request as if it had been redirected to <paramref name="finalAddress"/>, which is what HttpClient's redirect handling leaves in the response.</summary>
    private sealed class RedirectedHandler(Uri? finalAddress) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3]),
                RequestMessage = finalAddress is null ? request : new HttpRequestMessage(HttpMethod.Get, finalAddress),
            });
    }

    [Fact]
    public async Task ARedirectedResource_ReportsTheAddressItEndedAt()
    {
        using var client = new HttpClient(new RedirectedHandler(new Uri("https://other.test/b.png")));
        var loader = new HttpClientNetworkLoader(client, "https://page.test/index.html");

        var response = await loader.GetResourceStream(new Uri("https://page.test/a.png"));

        Assert.NotNull(response);
        Assert.Equal(new Uri("https://other.test/b.png"), response.FinalAddress);
    }

    [Fact]
    public async Task AResourceThatWasNotRedirected_HasNoFinalAddress()
    {
        using var client = new HttpClient(new RedirectedHandler(null));
        var loader = new HttpClientNetworkLoader(client, "https://page.test/index.html");

        var response = await loader.GetResourceStream(new Uri("https://page.test/a.png"));

        Assert.NotNull(response);
        Assert.Null(response.FinalAddress);
    }
}
