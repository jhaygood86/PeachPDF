using System.Net;
using System.Net.Sockets;
using System.Text;
using SkiaSharp;

namespace PeachBrackets.Avalonia.Tests.TestSupport;

/// <summary>A one-purpose HTTP server on the loopback interface, so the network paths of the view run against a real socket.</summary>
internal sealed class LocalServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly Dictionary<string, (string ContentType, byte[] Body)> _routes = [];
    private readonly CancellationTokenSource _stop = new();

    public LocalServer() => _listener.Start();

    public Uri BaseAddress => new($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");

    /// <summary>Paths requested so far.</summary>
    public List<string> Requests { get; } = [];

    public LocalServer Map(string path, string contentType, byte[] body)
    {
        _routes[path] = (contentType, body);
        return this;
    }

    public LocalServer Start()
    {
        _ = Task.Run(AcceptAsync);
        return this;
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                _ = Task.Run(() => ServeAsync(client));
            }
        }
        catch (Exception)
        {
            // Stopped.
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream();
            var buffer = new byte[4096];
            var read = await stream.ReadAsync(buffer);
            var request = Encoding.ASCII.GetString(buffer, 0, read);
            var path = request.Split(' ')[1];
            lock (Requests)
                Requests.Add(path);

            var found = _routes.TryGetValue(path, out var route);
            var body = found ? route.Body : "not found"u8.ToArray();
            var head = $"HTTP/1.1 {(found ? "200 OK" : "404 Not Found")}\r\nContent-Type: {(found ? route.ContentType : "text/plain")}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
            await stream.WriteAsync(body);
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
    }
}

internal static class Images
{
    /// <summary>A PNG of one color.</summary>
    public static byte[] Png(SKColor color, int width = 4, int height = 4)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public static string DataUri(SKColor color, int width = 4, int height = 4) => "data:image/png;base64," + Convert.ToBase64String(Png(color, width, height));
}
