using System.Net.Http;
using PeachPDF.Cli;

namespace PeachPDF.Cli.Tests;

public class CliRunnerInternalsTests
{
    [Fact]
    public void BuildHttpClient_NullWhenNoNetwork()
    {
        Assert.Null(CliRunner.BuildHttpClient(ArgumentParser.Parse(["--no-network", "doc.html"])));
    }

    [Fact]
    public void BuildHttpClient_AppliesTimeoutHeadersAndAuth()
    {
        var options = ArgumentParser.Parse([
            "--http-timeout", "42",
            "--user-agent", "peachpdf-test",
            "--http-header", "X-Test: yes",
            "--auth-user", "alice",
            "--auth-password", "secret",
            "doc.html",
        ]);

        using var client = CliRunner.BuildHttpClient(options);

        Assert.NotNull(client);
        Assert.Equal(TimeSpan.FromSeconds(42), client!.Timeout);
        Assert.Contains(client.DefaultRequestHeaders.GetValues("User-Agent"), v => v.Contains("peachpdf-test"));
        Assert.True(client.DefaultRequestHeaders.Contains("X-Test"));
        Assert.Equal("Basic", client.DefaultRequestHeaders.Authorization!.Scheme);
    }

    [Fact]
    public void BuildHttpClient_WithProxyAndInsecure_ReturnsClient()
    {
        var options = ArgumentParser.Parse(["--http-proxy", "http://proxy.local:8080", "--insecure", "doc.html"]);
        using var client = CliRunner.BuildHttpClient(options);
        Assert.NotNull(client);
    }

    [Fact]
    public void DefaultBaseFor_FileInput_UsesFileUri()
    {
        var baseUri = CliRunner.DefaultBaseFor(new CliInput(CliInputKind.File, "sub/doc.html"));
        Assert.Equal("file", baseUri.Scheme);
        Assert.EndsWith("doc.html", baseUri.AbsoluteUri);
    }

    [Fact]
    public void DefaultBaseFor_UrlInput_UsesUrl()
    {
        var baseUri = CliRunner.DefaultBaseFor(new CliInput(CliInputKind.Url, "https://example.com/a/b"));
        Assert.Equal("https://example.com/a/b", baseUri.AbsoluteUri);
    }

    [Fact]
    public void DefaultBaseFor_Stdin_UsesCurrentDirectory()
    {
        var baseUri = CliRunner.DefaultBaseFor(new CliInput(CliInputKind.Stdin, "-"));
        Assert.Equal("file", baseUri.Scheme);
        Assert.EndsWith("/", baseUri.AbsoluteUri);
    }

    [Fact]
    public void ResolveBaseUri_HttpUrl_IsUsedDirectly()
    {
        var baseUri = CliRunner.ResolveBaseUri("https://example.com/base/");
        Assert.Equal("https://example.com/base/", baseUri.AbsoluteUri);
    }

    [Fact]
    public void ResolveBaseUri_LocalDirectory_GetsTrailingSeparator()
    {
        var dir = Path.Combine(Path.GetTempPath(), "peachpdf-base-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var baseUri = CliRunner.ResolveBaseUri(dir);
            Assert.Equal("file", baseUri.Scheme);
            Assert.EndsWith("/", baseUri.AbsoluteUri);
        }
        finally
        {
            Directory.Delete(dir);
        }
    }
}
