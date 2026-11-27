using PeachBrackets;

namespace PeachBrackets.Tests;

public class BlobUrlStoreTests
{
    private sealed class Owner;

    [Fact]
    public async Task AnAddressResolvesToItsContentUntilItIsRevoked_AndTheHostLoaderAnswersIt()
    {
        var owner = new Owner();
        var url = "blob:null/" + Guid.NewGuid();
        BlobUrlStore.Register(url, owner, static (_, _) => new BlobContent([1, 2, 3], "image/png"));

        var response = await HostResourceLoader.LoadAsync(new Uri(url));
        Assert.NotNull(response);
        using var copy = new MemoryStream();
        await response!.ResourceStream!.CopyToAsync(copy, TestContext.Current.CancellationToken);
        Assert.Equal([1, 2, 3], copy.ToArray());
        Assert.Equal("image/png", response.ResponseHeaders!["Content-Type"][0]);

        BlobUrlStore.Revoke(url);
        Assert.Null(await HostResourceLoader.LoadAsync(new Uri(url)));
        GC.KeepAlive(owner);
    }

    [Fact]
    public void AnUnknownAddress_ResolvesToNothing()
    {
        Assert.Null(BlobUrlStore.TryRead("blob:null/" + Guid.NewGuid()));
        Assert.Null(BlobUrlStore.Load(new Uri("https://example.test/a.png")));
    }
}
