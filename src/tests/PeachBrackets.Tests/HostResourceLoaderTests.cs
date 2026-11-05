using PeachDrawing;
using PeachDrawing.Core;

namespace PeachBrackets.Tests;

/// <summary>A host that shows documents answers the document's requests for images with <see cref="HostResourceLoader"/>.</summary>
[Collection("HostResourceLoader state")]
public class HostResourceLoaderTests
{
    private const string RedPng = "iVBORw0KGgoAAAANSUhEUgAAAAQAAAAECAIAAAAmkwkpAAAAEUlEQVR4nGP4z8AARwgWXg4ArpMP8aaUSCMAAAAASUVORK5CYII=";

    [Fact]
    public async Task ADataUri_IsLoadedWithItsMimeType()
    {
        var response = await HostResourceLoader.LoadAsync(new RUri("data:image/png;base64," + RedPng));

        Assert.NotNull(response?.ResourceStream);
        Assert.Equal("image/png", response!.ResponseHeaders!["Content-Type"][0]);
        Assert.True(response.ResourceStream!.Length > 20);
    }

    [Fact]
    public async Task AFile_IsLoaded_AndAMissingOneIsNot()
    {
        var path = Path.Combine(Path.GetTempPath(), "host-loader-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(path, "hello", TestContext.Current.CancellationToken);
        try
        {
            using var response = (await HostResourceLoader.LoadAsync(new RUri(new Uri(path))))!.ResourceStream;
            Assert.Equal("hello", await new StreamReader(response!).ReadToEndAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(path);
        }

        Assert.Null(await HostResourceLoader.LoadAsync(new RUri(new Uri(path))));
    }

    [Fact]
    public async Task AWebAddress_IsNotFetchedUnlessTheHostAllowsIt()
    {
        Assert.Null(await HostResourceLoader.LoadAsync(new RUri("https://example.invalid/a.png")));
        Assert.Null(await HostResourceLoader.LoadAsync(new RUri(new Uri("file:///x.png")), allowLocalFiles: false));
    }

    // One request answered with a fixed response, on a port the operating system picks.
    private static (string Address, Task Served, IDisposable Stop) Serve(string status, string contentType, string body)
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        var served = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            var buffer = new byte[4096];
            _ = await stream.ReadAsync(buffer);
            var payload = System.Text.Encoding.UTF8.GetBytes(body);
            var head = $"HTTP/1.1 {status}\r\nContent-Type: {contentType}\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(head));
            await stream.WriteAsync(payload);
        });

        return ($"http://127.0.0.1:{port}/x.css", served, new Stopper(listener));
    }

    private sealed class Stopper(System.Net.Sockets.TcpListener listener) : IDisposable
    {
        public void Dispose() => listener.Stop();
    }

    [Fact]
    public async Task AWebAddress_IsFetchedWhenTheHostAllowsIt_WithItsContentType()
    {
        var (address, served, stop) = Serve("200 OK", "text/css", "p { color: red }");
        using (stop)
        {
            var response = await HostResourceLoader.LoadAsync(new RUri(address), allowNetwork: true);
            await served;

            Assert.NotNull(response?.ResourceStream);
            Assert.Equal("text/css", response!.ResponseHeaders!["Content-Type"][0]);
            Assert.Equal("p { color: red }", await new StreamReader(response.ResourceStream!).ReadToEndAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task AWebAddressThatAnswersAnError_OrCannotBeReached_IsNotLoaded()
    {
        var (address, served, stop) = Serve("404 Not Found", "text/plain", "nothing here");
        using (stop)
        {
            Assert.Null(await HostResourceLoader.LoadAsync(new RUri(address), allowNetwork: true));
            await served;
        }

        // Nothing listens there now.
        Assert.Null(await HostResourceLoader.LoadAsync(new RUri(address), allowNetwork: true));
    }

    // A server that answers every request on a loopback port with the same response, counting them.
    private sealed class CountingServer : IDisposable
    {
        private readonly System.Net.Sockets.TcpListener _listener = new(System.Net.IPAddress.Loopback, 0);
        private readonly string _head;
        private readonly byte[] _payload;
        private int _requests;

        public CountingServer(string extraHeaders, byte[] payload)
        {
            _payload = payload;
            _head = $"HTTP/1.1 200 OK\r\nContent-Type: text/css\r\nContent-Length: {payload.Length}\r\n{extraHeaders}Connection: close\r\n\r\n";
            _listener.Start();
            _ = Task.Run(Loop);
        }

        public int Requests => Volatile.Read(ref _requests);

        public string Address => $"http://127.0.0.1:{((System.Net.IPEndPoint)_listener.LocalEndpoint).Port}/x.css";

        private async Task Loop()
        {
            try
            {
                while (true)
                {
                    using var client = await _listener.AcceptTcpClientAsync();
                    var stream = client.GetStream();
                    _ = await stream.ReadAsync(new byte[4096]);
                    Interlocked.Increment(ref _requests);
                    await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(_head));
                    await stream.WriteAsync(_payload);
                }
            }
            catch (Exception ex) when (ex is System.Net.Sockets.SocketException or ObjectDisposedException or IOException)
            {
                // Stopped.
            }
        }

        public void Dispose() => _listener.Stop();
    }

    private static async Task<string> BodyAsync(string address)
    {
        var response = await HostResourceLoader.LoadAsync(new RUri(address), allowNetwork: true);
        Assert.NotNull(response?.ResourceStream);
        return await new StreamReader(response!.ResourceStream!).ReadToEndAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AWebAddressFetchedTwice_IsRequestedOnce_AndGivesTheSameContentAndHeaders()
    {
        using var server = new CountingServer("", System.Text.Encoding.UTF8.GetBytes("p { color: red }"));

        var first = await BodyAsync(server.Address);
        var second = await HostResourceLoader.LoadAsync(new RUri(server.Address), allowNetwork: true);

        Assert.Equal("p { color: red }", first);
        Assert.Equal("p { color: red }", await new StreamReader(second!.ResourceStream!).ReadToEndAsync(TestContext.Current.CancellationToken));
        Assert.Equal("text/css", second.ResponseHeaders!["Content-Type"][0]);
        Assert.Equal(1, server.Requests);
    }

    [Theory]
    [InlineData("Cache-Control: no-store\r\n")]
    [InlineData("Cache-Control: private\r\n")]
    public async Task AResponseTheServerKeepsOutOfCaches_IsRequestedEveryTime(string header)
    {
        using var server = new CountingServer(header, System.Text.Encoding.UTF8.GetBytes("a"));

        Assert.Equal("a", await BodyAsync(server.Address));
        Assert.Equal("a", await BodyAsync(server.Address));

        Assert.Equal(2, server.Requests);
    }

    [Fact]
    public async Task AResponseOfMoreThanEightMegabytes_IsNotKept()
    {
        using var server = new CountingServer("", new byte[9 * 1024 * 1024]);

        Assert.Equal(9 * 1024 * 1024, (await BodyAsync(server.Address)).Length);
        Assert.Equal(9 * 1024 * 1024, (await BodyAsync(server.Address)).Length);

        Assert.Equal(2, server.Requests);
    }

    [Fact]
    public async Task ACachedResponse_IsForgottenWhenItsLifetimeHasPassed()
    {
        var lifetime = HostResourceLoader.CacheLifetime;
        try
        {
            HostResourceLoader.CacheLifetime = TimeSpan.FromMilliseconds(50);
            using var server = new CountingServer("", System.Text.Encoding.UTF8.GetBytes("a"));

            await BodyAsync(server.Address);
            await Task.Delay(200, TestContext.Current.CancellationToken);
            await BodyAsync(server.Address);

            Assert.Equal(2, server.Requests);
        }
        finally
        {
            HostResourceLoader.CacheLifetime = lifetime;
        }
    }

    [Fact]
    public async Task ZeroLifetime_KeepsNothing_AndAFreshLifetimeCannotBeNegative()
    {
        var lifetime = HostResourceLoader.CacheLifetime;
        try
        {
            using var server = new CountingServer("", System.Text.Encoding.UTF8.GetBytes("a"));
            await BodyAsync(server.Address);

            HostResourceLoader.CacheLifetime = TimeSpan.Zero;
            await BodyAsync(server.Address);
            await BodyAsync(server.Address);

            Assert.Equal(3, server.Requests);
            Assert.Throws<ArgumentOutOfRangeException>(() => HostResourceLoader.CacheLifetime = TimeSpan.FromSeconds(-1));
        }
        finally
        {
            HostResourceLoader.CacheLifetime = lifetime;
        }
    }

    [Fact]
    public async Task ARelativeAddress_AndABrokenDataUri_AreNotLoaded()
    {
        Assert.Null(await HostResourceLoader.LoadAsync(new RUri("images/a.png", UriKind.Relative)));
        Assert.Null(await HostResourceLoader.LoadAsync(new RUri("data:image/png;base64,@@@@")));
    }

    [Fact]
    public async Task AFileThatCannotBeRead_IsNotLoaded()
    {
        var path = Path.Combine(Path.GetTempPath(), "host-loader-locked-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(path, "x", TestContext.Current.CancellationToken);
        try
        {
            // Another handle that allows no sharing: opening it for reading fails with an IOException, which the loader answers as nothing.
            using var holder = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.Null(await HostResourceLoader.LoadAsync(new RUri(new Uri(path))));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task AnImageInADocument_IsDrawn()
    {
        // The context the way the views make theirs: it loads what the document refers to through the host loader.
        var context = new RasterRenderContext { ResourceLoader = uri => HostResourceLoader.LoadAsync(uri) };
        await using (var font = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "SourceSans3-Regular.ttf")))
            await context.AddFont(font, TestDocument.Family);

        var options = new HtmlDocumentOptions { MediaType = MediaType.Screen, PageWidthPoints = 100, ViewportHeightPoints = 60 };
        options.SetMargins(0);
        using var document = await HtmlDocument.LoadAsync(
            TestDocument.Wrap($"<img src='data:image/png;base64,{RedPng}' style='display:block;width:20pt;height:20pt'>"), context, options);
        using var measuring = context.CreateCanvas(1, 1);
        await document.LayoutAsync(measuring);
        var canvas = await TestDocument.PaintAsync(context, document);

        var (r, g, b, _) = TestDocument.Pixel(canvas, 10, 10);
        Assert.True(r > 200 && g < 60 && b < 60, $"pixel was {r},{g},{b}");
    }
}
