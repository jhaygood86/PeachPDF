using PeachPDF.Cli;

namespace PeachPDF.Cli.Tests;

public class CliRunnerIntegrationTests
{
    private static bool IsPdf(byte[] bytes) =>
        bytes.Length > 4 && bytes[0] == '%' && bytes[1] == 'P' && bytes[2] == 'D' && bytes[3] == 'F';

    [Fact]
    public async Task FileInput_WritesPdfToOutputFile()
    {
        var dir = CreateTempDir();
        try
        {
            var htmlPath = Path.Combine(dir, "doc.html");
            var outPath = Path.Combine(dir, "doc.pdf");
            File.WriteAllText(htmlPath, "<html><body><h1>Hello</h1></body></html>");

            var options = ArgumentParser.Parse([htmlPath, "-o", outPath]);
            var exit = await CliRunner.RunAsync(options, TextReader.Null, Stream.Null);

            Assert.Equal(0, exit);
            Assert.True(File.Exists(outPath));
            Assert.True(IsPdf(File.ReadAllBytes(outPath)));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task FileInput_DefaultOutput_DerivesPdfName()
    {
        var dir = CreateTempDir();
        try
        {
            var htmlPath = Path.Combine(dir, "report.html");
            File.WriteAllText(htmlPath, "<html><body><p>Body</p></body></html>");

            var options = ArgumentParser.Parse([htmlPath]);
            var exit = await CliRunner.RunAsync(options, TextReader.Null, Stream.Null);

            Assert.Equal(0, exit);
            Assert.True(File.Exists(Path.Combine(dir, "report.pdf")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task StdinInput_StdoutOutput_WritesPdfBytes()
    {
        var options = ArgumentParser.Parse(["-", "-o", "-"]);
        using var stdout = new MemoryStream();

        var exit = await CliRunner.RunAsync(
            options, new StringReader("<html><body><h1>From stdin</h1></body></html>"), stdout);

        Assert.Equal(0, exit);
        Assert.True(IsPdf(stdout.ToArray()));
    }

    [Fact]
    public async Task MultipleInputs_CombineIntoOnePdf()
    {
        var dir = CreateTempDir();
        try
        {
            var a = Path.Combine(dir, "a.html");
            var b = Path.Combine(dir, "b.html");
            var outPath = Path.Combine(dir, "combined.pdf");
            File.WriteAllText(a, "<html><body><h1>Doc A</h1></body></html>");
            File.WriteAllText(b, "<html><body><h1>Doc B</h1></body></html>");

            var options = ArgumentParser.Parse([a, b, "-o", outPath]);
            var exit = await CliRunner.RunAsync(options, TextReader.Null, Stream.Null);

            Assert.Equal(0, exit);
            Assert.True(IsPdf(File.ReadAllBytes(outPath)));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task UserStyleSheets_AreAcceptedAndRender()
    {
        var dir = CreateTempDir();
        try
        {
            var htmlPath = Path.Combine(dir, "doc.html");
            var cssA = Path.Combine(dir, "a.css");
            var cssB = Path.Combine(dir, "b.css");
            var outPath = Path.Combine(dir, "doc.pdf");
            File.WriteAllText(htmlPath, "<html><body><p class='x'>Styled</p></body></html>");
            File.WriteAllText(cssA, ".x { color: red; }");
            File.WriteAllText(cssB, ".x { color: green; }");

            var options = ArgumentParser.Parse([htmlPath, "-s", cssA, "-s", cssB, "-o", outPath]);
            var exit = await CliRunner.RunAsync(options, TextReader.Null, Stream.Null);

            Assert.Equal(0, exit);
            Assert.True(IsPdf(File.ReadAllBytes(outPath)));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task MhtmlInput_RendersAndDisposesStream()
    {
        var dir = CreateTempDir();
        try
        {
            var mhtPath = Path.Combine(dir, "page.mht");
            var outPath = Path.Combine(dir, "page.pdf");
            File.WriteAllText(mhtPath,
                "Content-Type: multipart/related; boundary=\"B\"\r\n\r\n" +
                "--B\r\nContent-Type: text/html\r\nContent-Location: http://x/index.html\r\n\r\n" +
                "<html><body><h1>MHTML input</h1></body></html>\r\n--B--\r\n");

            var options = ArgumentParser.Parse([mhtPath, "-o", outPath]);
            var exit = await CliRunner.RunAsync(options, TextReader.Null, Stream.Null);

            Assert.Equal(0, exit);
            Assert.True(IsPdf(File.ReadAllBytes(outPath)));
            // The source stream must have been released once rendering finished.
            File.Delete(mhtPath);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task NoLocalFiles_BlocksResourcesButStillRenders()
    {
        var dir = CreateTempDir();
        try
        {
            var htmlPath = Path.Combine(dir, "doc.html");
            var outPath = Path.Combine(dir, "doc.pdf");
            File.WriteAllText(htmlPath, "<html><body><img src='missing.png'><p>Text</p></body></html>");

            var options = ArgumentParser.Parse([htmlPath, "--no-local-files", "-o", outPath]);
            var exit = await CliRunner.RunAsync(options, TextReader.Null, Stream.Null);

            Assert.Equal(0, exit);
            Assert.True(IsPdf(File.ReadAllBytes(outPath)));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Stdin_WithHttpBaseUrl_Renders()
    {
        var options = ArgumentParser.Parse(["-", "--baseurl", "https://example.com/", "-o", "-"]);
        using var stdout = new MemoryStream();

        var exit = await CliRunner.RunAsync(
            options, new StringReader("<html><body><p>Based</p></body></html>"), stdout);

        Assert.Equal(0, exit);
        Assert.True(IsPdf(stdout.ToArray()));
    }

    [Fact]
    public async Task MissingFile_ReturnsNonZeroExit()
    {
        var options = ArgumentParser.Parse(["/no/such/file.html", "-o", Path.Combine(CreateTempDir(), "x.pdf")]);
        var exit = await CliRunner.RunAsync(options, TextReader.Null, Stream.Null);
        Assert.NotEqual(0, exit);
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "peachpdf-cli-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
