using PeachPDF.Cli;

namespace PeachPDF.Cli.Tests;

public class LicenseInfoTests
{
    [Fact]
    public void License_ContainsBsdText()
    {
        var text = LicenseInfo.License;
        Assert.Contains("Redistribution and use", text);
        Assert.Contains("THIS SOFTWARE IS PROVIDED", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Credits_ContainsLicenseAndThirdPartyAcknowledgements()
    {
        var text = LicenseInfo.Credits;
        Assert.Contains("Redistribution and use", text);
        Assert.Contains("Third-Party Licenses", text);
    }

    [Fact]
    public async Task ShowLicense_PrintsLicenseAndExitsZero()
    {
        var (exit, output) = await RunCapturingStdout(["--show-license"]);
        Assert.Equal(0, exit);
        Assert.Contains("Redistribution and use", output);
    }

    [Fact]
    public async Task Credits_PrintsAcknowledgementsAndExitsZero()
    {
        var (exit, output) = await RunCapturingStdout(["--credits"]);
        Assert.Equal(0, exit);
        Assert.Contains("Third-Party Licenses", output);
    }

    [Fact]
    public async Task Version_PrintsVersionAndExitsZero()
    {
        var (exit, output) = await RunCapturingStdout(["--version"]);
        Assert.Equal(0, exit);
        Assert.Contains("PeachPDF", output);
    }

    private static async Task<(int Exit, string Output)> RunCapturingStdout(string[] args)
    {
        var original = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            var exit = await CliProgram.MainAsync(args);
            return (exit, writer.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }
}
