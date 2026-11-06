using PeachDrawing.Core;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace PeachBrackets.Tests;

/// <summary>The <c>User-Agent</c> the host resource loader sends: what Chromium sends on the platform, then <c>PeachBrackets/&lt;version&gt;</c>.</summary>
[Collection("HostResourceLoader state")]
public partial class HostUserAgentTests
{
    [GeneratedRegex(@"^Mozilla/5\.0 \((?<platform>[^)]+)\) AppleWebKit/537\.36 \(KHTML, like Gecko\) Chrome/156\.0\.0\.0 Safari/537\.36 PeachBrackets/(?<version>\S+)$")]
    private static partial Regex Shape();

    [Fact]
    public void TheDefault_IsTheChromiumStringOfThePlatformFollowedByThePeachBracketsToken()
    {
        var match = Shape().Match(HostResourceLoader.UserAgent);

        Assert.True(match.Success, HostResourceLoader.UserAgent);
        var platform = match.Groups["platform"].Value;
        if (OperatingSystem.IsAndroid())
            Assert.Equal("Linux; Android 10; K", platform);
        else if (OperatingSystem.IsWindows())
            Assert.StartsWith("Windows NT 10.0", platform);
        else if (OperatingSystem.IsMacOS())
            Assert.Equal("Macintosh; Intel Mac OS X 10_15_7", platform);
        else
            Assert.StartsWith("X11; Linux ", platform);

        // The version is the package's, not a placeholder.
        Assert.Matches(@"^\d+\.\d+\.\d+", match.Groups["version"].Value);
    }

    [Fact]
    public void ASetUserAgent_IsKept_AndNullRestoresTheDefault()
    {
        var original = HostResourceLoader.UserAgent;
        try
        {
            HostResourceLoader.UserAgent = "  MyHost/2  ";
            Assert.Equal("MyHost/2", HostResourceLoader.UserAgent);

            HostResourceLoader.UserAgent = " ";
            Assert.Equal(original, HostResourceLoader.UserAgent);
        }
        finally
        {
            HostResourceLoader.UserAgent = null!;
        }
    }

    [Fact]
    public async Task AWebRequest_CarriesTheUserAgent()
    {
        if (OperatingSystem.IsBrowser())
            return;

        // A one-shot server on the loopback address that records the request line and headers it is sent.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var served = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            var buffer = new byte[8192];
            var request = new StringBuilder();
            while (!request.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                var read = await stream.ReadAsync(buffer);
                if (read == 0)
                    break;

                request.Append(Encoding.ASCII.GetString(buffer, 0, read));
            }

            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"));
            return request.ToString();
        }, TestContext.Current.CancellationToken);

        var before = HostResourceLoader.CacheLifetime;
        HostResourceLoader.CacheLifetime = TimeSpan.Zero;
        try
        {
            using var response = (await HostResourceLoader.LoadAsync(new Uri($"http://127.0.0.1:{port}/page"), allowNetwork: true))?.ResourceStream;
            Assert.NotNull(response);
        }
        finally
        {
            HostResourceLoader.CacheLifetime = before;
        }

        var sent = await served.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Contains("User-Agent: " + HostResourceLoader.UserAgent + "\r\n", sent);
    }
}
