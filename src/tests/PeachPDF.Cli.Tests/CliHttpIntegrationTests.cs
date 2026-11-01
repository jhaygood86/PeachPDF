using System.Net;
using System.Net.Sockets;
using System.Text;
using PeachPDF.Cli;

namespace PeachPDF.Cli.Tests;

public class CliHttpIntegrationTests
{
    [Fact]
    public async Task UrlInput_FetchesDocumentAndResourceOverHttp()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var port = GetFreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        // Serve the document and its referenced stylesheet until the test stops the listener.
        var server = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    var ctx = await listener.GetContextAsync();
                    var path = ctx.Request.Url!.AbsolutePath;
                    var (body, contentType) = path.EndsWith(".css")
                        ? (".x { color: rgb(0, 128, 0); }", "text/css")
                        : ("<html><head><link rel=\"stylesheet\" href=\"style.css\"></head><body><p class=\"x\">Networked</p></body></html>", "text/html");
                    var bytes = Encoding.UTF8.GetBytes(body);
                    ctx.Response.ContentType = contentType;
                    ctx.Response.ContentLength64 = bytes.Length;
                    await ctx.Response.OutputStream.WriteAsync(bytes, cancellation);
                    ctx.Response.Close();
                }
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException or OperationCanceledException)
            {
                // Listener stopped — expected at end of test.
            }
        }, cancellation);

        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "peachpdf-http-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var outPath = Path.Combine(dir, "out.pdf");
            try
            {
                var options = ArgumentParser.Parse([$"http://127.0.0.1:{port}/index.html", "-o", outPath]);
                var exit = await CliRunner.RunAsync(options, TextReader.Null, Stream.Null);

                Assert.Equal(0, exit);
                var bytes = File.ReadAllBytes(outPath);
                Assert.True(bytes.Length > 4 && bytes[0] == '%' && bytes[1] == 'P' && bytes[2] == 'D' && bytes[3] == 'F');
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        finally
        {
            listener.Stop();
            await server;
        }
    }

    private static int GetFreePort()
    {
        var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        tcp.Stop();
        return port;
    }
}
