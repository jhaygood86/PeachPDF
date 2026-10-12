using PeachPDF;
using PeachPDF.Cli;

namespace PeachPDF.Cli.Tests;

/// <summary>
/// The <c>--user-password</c>, <c>--owner-password</c>, <c>--permissions</c> and <c>--no-encrypt-metadata</c> options:
/// parsing, the translation into <see cref="PdfGenerateConfig"/>, and one real run.
/// </summary>
public class CliEncryptionTests
{
    private const string Html = "<html><body><p>Secret</p></body></html>";

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "peachpdf-cli-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void NoEncryptionOptions_LeaveTheConfigUnencrypted()
    {
        var options = ArgumentParser.Parse(["doc.html"]);

        Assert.False(options.WantsEncryption);
        Assert.Null(CliRunner.BuildConfig(options).Encryption);
    }

    [Fact]
    public void Passwords_AreMappedToTheEncryptionOptions()
    {
        var config = CliRunner.BuildConfig(ArgumentParser.Parse(["--user-password=open", "--owner-password", "admin", "doc.html"]));

        var encryption = Assert.IsType<PdfEncryptionOptions>(config.Encryption);
        Assert.Equal("open", encryption.UserPassword);
        Assert.Equal("admin", encryption.OwnerPassword);
        Assert.Equal(PdfPermissions.All, encryption.Permissions);
        Assert.True(encryption.EncryptMetadata);
    }

    [Theory]
    [InlineData("read-only", PdfPermissions.ReadOnly)]
    [InlineData("none", PdfPermissions.None)]
    [InlineData("all", PdfPermissions.All)]
    [InlineData("print", PdfPermissions.AllowPrint | PdfPermissions.AllowHighQualityPrint)]
    [InlineData("print-low,copy", PdfPermissions.AllowPrint | PdfPermissions.AllowCopy)]
    [InlineData("Copy, Accessibility", PdfPermissions.AllowCopy | PdfPermissions.AllowAccessibilityExtract)]
    [InlineData("modify,annotate,fill-forms,assemble", PdfPermissions.AllowModify | PdfPermissions.AllowAnnotations | PdfPermissions.AllowFillForms | PdfPermissions.AllowAssemble)]
    public void Permissions_AreParsedAsAList(string value, PdfPermissions expected)
    {
        var options = ArgumentParser.Parse([$"--permissions={value}", "doc.html"]);

        Assert.Empty(options.Errors);
        Assert.Equal(expected, options.Permissions);
        Assert.Equal(expected, CliRunner.BuildConfig(options).Encryption!.Permissions);
    }

    [Fact]
    public void Permissions_UnknownName_IsError()
    {
        var options = ArgumentParser.Parse(["--permissions=print,fly", "doc.html"]);

        Assert.Contains("fly", Assert.Single(options.Errors));
    }

    [Fact]
    public void NoEncryptMetadata_TurnsOnEncryptionAndKeepsTheXmpReadable()
    {
        var config = CliRunner.BuildConfig(ArgumentParser.Parse(["--no-encrypt-metadata", "doc.html"]));

        Assert.False(config.Encryption!.EncryptMetadata);
    }

    [Fact]
    public void PasswordFile_IsReadWithoutItsTrailingNewline()
    {
        var dir = CreateTempDir();
        try
        {
            var user = Path.Combine(dir, "user.txt");
            var owner = Path.Combine(dir, "owner.txt");
            File.WriteAllText(user, "from file\r\n");
            File.WriteAllText(owner, "owner file\n");

            var options = ArgumentParser.Parse([$"--user-password-file={user}", $"--owner-password-file={owner}", "doc.html"]);

            Assert.Empty(options.Errors);
            Assert.Equal("from file", options.UserPassword);
            Assert.Equal("owner file", options.OwnerPassword);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void PasswordFile_Missing_IsError()
    {
        var options = ArgumentParser.Parse(["--user-password-file=/no/such/file.txt", "doc.html"]);

        Assert.Contains("--user-password-file", Assert.Single(options.Errors));
    }

    [Fact]
    public void PasswordEnvironment_IsReadFromTheNamedVariable()
    {
        var name = "PEACHPDF_TEST_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(name, "from env");
        try
        {
            var options = ArgumentParser.Parse([$"--user-password-env={name}", $"--owner-password-env={name}", "doc.html"]);

            Assert.Empty(options.Errors);
            Assert.Equal("from env", options.UserPassword);
            Assert.Equal("from env", options.OwnerPassword);
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public void PasswordEnvironment_Unset_IsError()
    {
        var options = ArgumentParser.Parse(["--owner-password-env=PEACHPDF_SURELY_NOT_SET_9F3A", "doc.html"]);

        Assert.Contains("PEACHPDF_SURELY_NOT_SET_9F3A", Assert.Single(options.Errors));
    }

    [Fact]
    public void Encryption_WithPdfA_IsError()
    {
        var options = ArgumentParser.Parse(["--user-password=x", "--pdfa=2b", "doc.html"]);

        Assert.Contains("--pdfa", Assert.Single(options.Errors));
    }

    [Fact]
    public async Task RealRun_WritesAnEncryptedPdf()
    {
        var dir = CreateTempDir();
        try
        {
            var html = Path.Combine(dir, "doc.html");
            var pdf = Path.Combine(dir, "doc.pdf");
            File.WriteAllText(html, Html);

            var exit = await CliRunner.RunAsync(
                ArgumentParser.Parse([html, "--user-password=open", "--permissions=read-only", "-o", pdf]),
                TextReader.Null, Stream.Null);

            Assert.Equal(0, exit);
            var text = System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(pdf));
            Assert.Contains("/Encrypt", text);
            Assert.Contains("/AESV3", text);
            Assert.DoesNotContain("Secret", text);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
