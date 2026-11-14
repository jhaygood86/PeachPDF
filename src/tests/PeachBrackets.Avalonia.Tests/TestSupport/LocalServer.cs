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
    private readonly Dictionary<string, TaskCompletionSource> _holds = [];
    private readonly List<string> _requests = [];
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private Task? _accepting;

    public LocalServer() => _listener.Start();

    public Uri BaseAddress => new($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");

    /// <summary>Paths requested so far (a copy).</summary>
    public IReadOnlyList<string> Requests
    {
        get
        {
            lock (_gate)
                return [.. _requests];
        }
    }

    public LocalServer Map(string path, string contentType, byte[] body)
    {
        lock (_gate)
            _routes[path] = (contentType, body);

        return this;
    }

    /// <summary>Makes a request for a path wait, once it has arrived, until the returned source is completed.</summary>
    public TaskCompletionSource Hold(string path)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
            _holds[path] = release;

        return release;
    }

    public LocalServer Start()
    {
        _accepting = Task.Run(AcceptAsync);
        return this;
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                _ = Task.Run(() => ServeSafelyAsync(client));
            }
        }
        catch (Exception) when (_stop.IsCancellationRequested || !_listener.Server.IsBound)
        {
            // Stopped.
        }
    }

    private async Task ServeSafelyAsync(TcpClient client)
    {
        try
        {
            await ServeAsync(client);
        }
        catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
        {
            // The client went away (a cancelled load closes its connection).
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream();

            // The request line and the headers, up to the blank line that ends them: one read can hold less than that.
            var buffer = new byte[16384];
            var length = 0;
            while (length < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(length), _stop.Token);
                if (read == 0)
                    break;

                length += read;
                if (Encoding.ASCII.GetString(buffer, 0, length).Contains("\r\n\r\n", StringComparison.Ordinal))
                    break;
            }

            var request = Encoding.ASCII.GetString(buffer, 0, length);
            var path = request.Split(' ')[1];
            TaskCompletionSource? hold;
            (string ContentType, byte[] Body) route;
            bool found;
            lock (_gate)
            {
                _requests.Add(path);
                found = _routes.TryGetValue(path, out route);
                _holds.TryGetValue(path, out hold);
            }

            if (hold is not null)
                await hold.Task.WaitAsync(_stop.Token);

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
        try
        {
            _accepting?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // The accept loop ended with the listener.
        }

        _stop.Dispose();
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
