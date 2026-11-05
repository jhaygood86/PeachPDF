using PeachDrawing.Core;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PeachBrackets.Tests;

/// <summary>
/// What the host resource loader keeps for the next request of the same address, and how it asks for it: against a real server on the loopback
/// address, because the behaviour is in the wire (chunked bodies, compressed bodies, headers that say not to keep a response).
/// </summary>
[Collection("HostResourceLoader state")]
public class HostResourceLoaderCachingTests
{
    // A server that answers every request with one canned response and counts the requests and what they asked for.
    private sealed class TinyServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private int _requests;

        public TinyServer(Func<byte[]> response)
        {
            _listener.Start();
            Task.Run(async () =>
            {
                while (!_stop.IsCancellationRequested)
                {
                    TcpClient client;
                    try
                    {
                        client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    }
                    catch (Exception)
                    {
                        return;
                    }

                    _ = Task.Run(() => Serve(client, response()));
                }
            });
        }

        public int Requests => Volatile.Read(ref _requests);

        public string LastRequest { get; private set; } = string.Empty;

        public string Url(string path = "/resource") => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}{path}";

        private async Task Serve(TcpClient client, byte[] response)
        {
            using (client)
            {
                var stream = client.GetStream();
                var request = new StringBuilder();
                var buffer = new byte[4096];
                while (!request.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                {
                    var read = await stream.ReadAsync(buffer);
                    if (read == 0)
                        return;

                    request.Append(Encoding.ASCII.GetString(buffer, 0, read));
                }

                LastRequest = request.ToString();
                Interlocked.Increment(ref _requests);
                try
                {
                    await stream.WriteAsync(response);
                }
                catch (IOException)
                {
                    // The client stopped reading (it had what it needed).
                }
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
        }
    }

    private static byte[] Chunked(byte[] body, string extraHeaders = "")
    {
        using var message = new MemoryStream();
        message.Write(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/css\r\nTransfer-Encoding: chunked\r\nConnection: close\r\n{extraHeaders}\r\n"));
        for (var offset = 0; offset < body.Length; offset += 16384)
        {
            var size = Math.Min(16384, body.Length - offset);
            message.Write(Encoding.ASCII.GetBytes($"{size:x}\r\n"));
            message.Write(body, offset, size);
            message.Write("\r\n"u8);
        }

        message.Write("0\r\n\r\n"u8);
        return message.ToArray();
    }

    private static async Task<byte[]> LoadAsync(string url)
    {
        using var response = (await HostResourceLoader.LoadAsync(new RUri(url), allowNetwork: true))?.ResourceStream;
        Assert.NotNull(response);
        using var copy = new MemoryStream();
        await response!.CopyToAsync(copy, TestContext.Current.CancellationToken);
        return copy.ToArray();
    }

    [Fact]
    public async Task AResponseWithNoDeclaredLength_IsKeptForTheNextRequest()
    {
        var body = Encoding.UTF8.GetBytes(new string('a', 5000));
        using var server = new TinyServer(() => Chunked(body));

        Assert.Equal(body, await LoadAsync(server.Url()));
        Assert.Equal(body, await LoadAsync(server.Url()));

        Assert.Equal(1, server.Requests);
    }

    [Fact]
    public async Task PrefetchingAddresses_FetchesEachOnce_AndTheLaterLoadsComeFromTheCache()
    {
        var body = Encoding.UTF8.GetBytes(new string('p', 3000));
        using var server = new TinyServer(() => Chunked(body));
        var addresses = new[] { server.Url("/a"), server.Url("/b"), server.Url("/a"), server.Url("/c") }.Select(a => new RUri(a)).ToList();

        await HostResourceLoader.PrefetchAsync(addresses, allowNetwork: true, concurrency: 2);

        Assert.Equal(3, server.Requests);
        Assert.Equal(body, await LoadAsync(server.Url("/b")));
        Assert.Equal(3, server.Requests);
    }

    [Fact]
    public async Task PrefetchingWithTheNetworkOff_FetchesNothing()
    {
        using var server = new TinyServer(() => Chunked([1, 2, 3]));

        await HostResourceLoader.PrefetchAsync([new RUri(server.Url("/a"))], allowNetwork: false);
        await HostResourceLoader.PrefetchAsync([new RUri("data:text/plain,hi"), new RUri("file:///nowhere")], allowNetwork: true);

        Assert.Equal(0, server.Requests);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await HostResourceLoader.PrefetchAsync([], true, 0));
    }

    [Fact]
    public async Task APrefetchOfAnAddressThatFails_IsNotAnError()
    {

        await HostResourceLoader.PrefetchAsync([new RUri("http://127.0.0.1:1/unreachable")], allowNetwork: true);
    }

    [Fact]
    public async Task ACompressedResponse_IsAskedForDecodedAndKept()
    {
        var body = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("body { color: red } ", 400)));
        byte[] gzip;
        using (var compressed = new MemoryStream())
        {
            using (var zip = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
                zip.Write(body);

            gzip = compressed.ToArray();
        }

        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/css\r\nContent-Encoding: gzip\r\nContent-Length: {gzip.Length}\r\nConnection: close\r\n\r\n");
        using var server = new TinyServer(() => [.. head, .. gzip]);

        Assert.Equal(body, await LoadAsync(server.Url()));
        Assert.Contains("Accept-Encoding:", server.LastRequest, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("gzip", server.LastRequest, StringComparison.OrdinalIgnoreCase);

        // Decoded, a response has no declared length any more; it is kept all the same.
        Assert.Equal(body, await LoadAsync(server.Url()));
        Assert.Equal(1, server.Requests);
    }

    [Theory]
    [InlineData("Cache-Control: no-store\r\n")]
    [InlineData("Cache-Control: private\r\n")]
    public async Task AResponseThatSaysNotToKeepIt_IsFetchedAgain(string header)
    {
        var body = Encoding.UTF8.GetBytes("hello");
        using var server = new TinyServer(() => Chunked(body, header));

        Assert.Equal(body, await LoadAsync(server.Url()));
        Assert.Equal(body, await LoadAsync(server.Url()));

        Assert.Equal(2, server.Requests);
    }

    [Fact]
    public async Task AWebRequest_IsReportedAsASpanWithItsUrlAndWhetherItWasKept()
    {
        var stopped = new ConcurrentBag<Activity>();
        using var listener = EngineTelemetryTests.Listen(stopped);
        ActivitySource.AddActivityListener(listener);

        var body = Encoding.UTF8.GetBytes("hello");
        using var server = new TinyServer(() => Chunked(body));
        var url = server.Url("/telemetry");

        await LoadAsync(url);
        await LoadAsync(url);

        var fetches = stopped.Where(a => a.OperationName == "resource.fetch" && Equals(a.GetTagItem("url"), url)).OrderBy(a => a.StartTimeUtc).ToList();
        Assert.Equal(2, fetches.Count);
        Assert.Equal("miss", fetches[0].GetTagItem("cache"));
        Assert.Equal(200, fetches[0].GetTagItem("status"));
        Assert.Equal(body.Length, fetches[0].GetTagItem("bytes"));
        Assert.Equal("hit", fetches[1].GetTagItem("cache"));
    }

    [Fact]
    public async Task AResponseOverTheLimitWithNoDeclaredLength_ComesBackWholeAndIsNotKept()
    {
        // 9 MB: past the 8 MB limit, so what was read ahead is handed back followed by the rest of the stream.
        var body = new byte[9 * 1024 * 1024];
        new Random(7).NextBytes(body);
        using var server = new TinyServer(() => Chunked(body));

        var first = await LoadAsync(server.Url());
        Assert.Equal(body.Length, first.Length);
        Assert.True(first.AsSpan().SequenceEqual(body));

        await LoadAsync(server.Url());
        Assert.Equal(2, server.Requests);
    }
}
