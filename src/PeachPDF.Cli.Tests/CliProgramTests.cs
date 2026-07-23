using PeachPDF.Cli;

namespace PeachPDF.Cli.Tests;

public class CliProgramTests
{
    [Fact]
    public async Task UnknownOption_ExitsNonZero_WithStderrMessage()
    {
        var (exit, stderr) = await RunCapturingStderr(["--encrypt", "doc.html"]);
        Assert.Equal(1, exit);
        Assert.Contains("unknown option", stderr);
    }

    [Fact]
    public async Task NoInput_ExitsNonZero()
    {
        var (exit, _) = await RunCapturingStderr(["--verbose"]);
        Assert.Equal(1, exit);
    }

    [Fact]
    public async Task Help_ExitsZero()
    {
        var original = Console.Out;
        Console.SetOut(new StringWriter());
        try
        {
            Assert.Equal(0, await CliProgram.MainAsync(["--help"]));
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Fact]
    public async Task ValidRender_ExitsZero_AndWritesPdf()
    {
        var dir = Path.Combine(Path.GetTempPath(), "peachpdf-prog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var htmlPath = Path.Combine(dir, "doc.html");
            var outPath = Path.Combine(dir, "doc.pdf");
            File.WriteAllText(htmlPath, "<html><body><h1>Program test</h1></body></html>");

            var exit = await CliProgram.MainAsync([htmlPath, "-o", outPath]);

            Assert.Equal(0, exit);
            Assert.True(File.Exists(outPath));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static async Task<(int Exit, string Stderr)> RunCapturingStderr(string[] args)
    {
        var original = Console.Error;
        var writer = new StringWriter();
        Console.SetError(writer);
        try
        {
            var exit = await CliProgram.MainAsync(args);
            return (exit, writer.ToString());
        }
        finally
        {
            Console.SetError(original);
        }
    }
}
