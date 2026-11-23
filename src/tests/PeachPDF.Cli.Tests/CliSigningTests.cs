using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using PeachDrawing.PDF;
using PeachPDF.Cli;

namespace PeachPDF.Cli.Tests;

/// <summary>The <c>--sign-*</c> and <c>--timestamp-url</c> options.</summary>
public class CliSigningTests
{
    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "peachpdf-cli-sign-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string WritePfx(string dir, string password)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=CLI Test Signer", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        var path = Path.Combine(dir, "signer.pfx");
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));
        return path;
    }

    [Fact]
    public void NoSigningOptions_LeaveTheConfigUnsigned()
    {
        var options = ArgumentParser.Parse(["doc.html"]);

        Assert.False(options.WantsSigning);
        Assert.Null(CliRunner.BuildConfig(options).Signing);
    }

    [Fact]
    public void SigningDetailsWithoutACertificate_AreAnError()
    {
        var options = ArgumentParser.Parse(["--sign-reason=Approved", "doc.html"]);

        Assert.Contains("--sign-cert", Assert.Single(options.Errors));
    }

    [Theory]
    [InlineData("--sign-format=rot13")]
    [InlineData("--timestamp-url=ftp://example.com/tsa")]
    [InlineData("--timestamp-url=not a url")]
    [InlineData("--sign-box=1,2,3")]
    [InlineData("--sign-box=0,10,10,100,50")]
    [InlineData("--sign-box=1,10,10,-5,50")]
    [InlineData("--sign-box=1,10,x,100,50")]
    public void InvalidSigningValues_AreErrors(string argument)
    {
        var options = ArgumentParser.Parse(["--sign-cert=a.pfx", argument, "doc.html"]);

        Assert.NotEmpty(options.Errors);
    }

    [Fact]
    public void AVisibleBoxWithPdfA_IsAnError()
    {
        var options = ArgumentParser.Parse(["--sign-cert=a.pfx", "--sign-box=1,10,10,100,50", "--pdfa=2b", "doc.html"]);

        Assert.Contains("PDF/A", Assert.Single(options.Errors));
    }

    [Theory]
    [InlineData("pkcs7", PeachPDF.PdfSignatureFormat.Pkcs7Detached)]
    [InlineData("cades", PeachPDF.PdfSignatureFormat.CadesDetached)]
    [InlineData("PAdES", PeachPDF.PdfSignatureFormat.CadesDetached)]
    public void TheFormat_IsParsed(string value, PeachPDF.PdfSignatureFormat expected)
    {
        var options = ArgumentParser.Parse(["--sign-cert=a.pfx", $"--sign-format={value}", "doc.html"]);

        Assert.Empty(options.Errors);
        Assert.Equal(expected, options.SignFormat);
    }

    [Fact]
    public void TheBoxAndTheDetails_AreParsed()
    {
        var options = ArgumentParser.Parse(["--sign-cert=a.pfx", "--sign-box=2,10.5,20,150,40", "--sign-name=N", "--sign-reason=R", "--sign-location=L", "--sign-contact=C", "--timestamp-url=http://tsa.example/", "doc.html"]);

        Assert.Empty(options.Errors);
        var box = Assert.IsType<PeachPDF.PdfSignatureBox>(options.SignBox);
        Assert.Equal(2, box.PageNumber);
        Assert.Equal(10.5, box.X);
        Assert.Equal(20, box.Y);
        Assert.Equal(150, box.Width);
        Assert.Equal(40, box.Height);
        Assert.Equal(("N", "R", "L", "C"), (options.SignName, options.SignReason, options.SignLocation, options.SignContact));
        Assert.Equal("http://tsa.example/", options.TimestampUrl!.ToString());
    }

    [Fact]
    public void ThePasswordCanComeFromAFileOrTheEnvironment()
    {
        var dir = CreateTempDir();
        try
        {
            var file = Path.Combine(dir, "pw.txt");
            File.WriteAllText(file, "from-file\n");
            Assert.Equal("from-file", ArgumentParser.Parse(["--sign-cert=a.pfx", $"--sign-password-file={file}", "doc.html"]).SignPassword);

            Environment.SetEnvironmentVariable("PEACHPDF_TEST_SIGN_PW", "from-env");
            try
            {
                Assert.Equal("from-env", ArgumentParser.Parse(["--sign-cert=a.pfx", "--sign-password-env=PEACHPDF_TEST_SIGN_PW", "doc.html"]).SignPassword);
            }
            finally
            {
                Environment.SetEnvironmentVariable("PEACHPDF_TEST_SIGN_PW", null);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ASignedRun_WritesAPdfWhoseSignatureIsIntact()
    {
        var dir = CreateTempDir();
        try
        {
            var html = Path.Combine(dir, "doc.html");
            var output = Path.Combine(dir, "doc.pdf");
            File.WriteAllText(html, "<html><body><p>Signed by the command line</p></body></html>");
            var pfx = WritePfx(dir, "secret");

            var options = ArgumentParser.Parse([html, "-o", output, $"--sign-cert={pfx}", "--sign-password=secret", "--sign-reason=CLI test", "--sign-box=1,50,700,200,60"]);
            var exit = await CliRunner.RunAsync(options, TextReader.Null, Stream.Null);

            Assert.Equal(0, exit);
            var signature = Assert.Single(PdfPageSource.Open(File.ReadAllBytes(output)).GetSignatures());
            Assert.True(signature.IsIntact, signature.Problem);
            Assert.True(signature.CoversWholeFile);
            Assert.Equal("CLI test", signature.Reason);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task AWrongCertificatePassword_FailsWithAMessageNotACrash()
    {
        var dir = CreateTempDir();
        try
        {
            var html = Path.Combine(dir, "doc.html");
            File.WriteAllText(html, "<html><body><p>x</p></body></html>");
            var pfx = WritePfx(dir, "secret");

            var options = ArgumentParser.Parse([html, "-o", Path.Combine(dir, "out.pdf"), $"--sign-cert={pfx}", "--sign-password=wrong"]);
            var exit = await CliRunner.RunAsync(options, TextReader.Null, Stream.Null);

            Assert.Equal(1, exit);
            Assert.False(File.Exists(Path.Combine(dir, "out.pdf")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
