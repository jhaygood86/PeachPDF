using PeachPDF;
using PeachPDF.Cli;

namespace PeachPDF.Cli.Tests;

/// <summary>
/// The <c>--pdfa</c>, <c>--pdf-creation-date</c>, <c>--attach</c> and <c>--facturx-*</c> options: parsing, the
/// translation into <see cref="PdfGenerateConfig"/>, and one real run of each.
/// </summary>
public class CliPdfAAndEmbeddingTests
{
    private const string Html = "<html><body><p>Invoice</p></body></html>";

    private const string CiiEn16931 =
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <rsm:CrossIndustryInvoice xmlns:rsm="urn:un:unece:uncefact:data:standard:CrossIndustryInvoice:100"
                                  xmlns:ram="urn:un:unece:uncefact:data:standard:ReusableAggregateBusinessInformationEntity:100">
          <rsm:ExchangedDocumentContext>
            <ram:GuidelineSpecifiedDocumentContextParameter><ram:ID>urn:cen.eu:en16931:2017</ram:ID></ram:GuidelineSpecifiedDocumentContextParameter>
          </rsm:ExchangedDocumentContext>
        </rsm:CrossIndustryInvoice>
        """;

    private static string PdfText(byte[] pdf) => System.Text.Encoding.Latin1.GetString(pdf);

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "peachpdf-cli-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    // --- --pdfa ---

    [Theory]
    [InlineData("1a", PdfAConformance.PdfA1A)]
    [InlineData("1b", PdfAConformance.PdfA1B)]
    [InlineData("2a", PdfAConformance.PdfA2A)]
    [InlineData("2b", PdfAConformance.PdfA2B)]
    [InlineData("2u", PdfAConformance.PdfA2U)]
    [InlineData("3a", PdfAConformance.PdfA3A)]
    [InlineData("3b", PdfAConformance.PdfA3B)]
    [InlineData("3U", PdfAConformance.PdfA3U)]
    public void PdfA_LevelIsParsed(string value, PdfAConformance expected)
    {
        var options = ArgumentParser.Parse([$"--pdfa={value}", "doc.html"]);

        Assert.Empty(options.Errors);
        Assert.Equal(expected, options.PdfA);
    }

    [Theory]
    [InlineData("4b")]
    [InlineData("3")]
    [InlineData("PdfA3B")]
    public void PdfA_UnknownLevel_IsError(string value)
    {
        var options = ArgumentParser.Parse(["--pdfa", value, "doc.html"]);

        var error = Assert.Single(options.Errors);
        Assert.Contains("--pdfa", error);
        Assert.Null(options.PdfA);
    }

    [Fact]
    public void PdfA_MapsToConformance_WithoutInventingACreationDate()
    {
        var config = CliRunner.BuildConfig(ArgumentParser.Parse(["--pdfa=3b", "doc.html"]));

        Assert.Equal(PdfAConformance.PdfA3B, config.PdfAConformance);
        // No date is made up: the document's own date is used, and the library throws when there is none.
        Assert.Null(config.Metadata);
    }

    [Fact]
    public void WithoutPdfA_NoConformanceAndNoInventedCreationDate()
    {
        var config = CliRunner.BuildConfig(ArgumentParser.Parse(["doc.html"]));

        Assert.Equal(PdfAConformance.None, config.PdfAConformance);
        Assert.Null(config.Metadata);
    }

    // --- --pdf-creation-date ---

    [Fact]
    public void CreationDate_WithAnOffset_IsKept()
    {
        var options = ArgumentParser.Parse(["--pdf-creation-date=2026-09-21T10:30:00+02:00", "doc.html"]);

        Assert.Empty(options.Errors);
        Assert.Equal(new DateTimeOffset(2026, 9, 21, 10, 30, 0, TimeSpan.FromHours(2)), options.PdfCreationDate);
    }

    [Fact]
    public void CreationDate_WithoutAnOffset_IsUtc_SoTheSameCommandGivesTheSameFileEverywhere()
    {
        var options = ArgumentParser.Parse(["--pdf-creation-date", "2026-09-21", "doc.html"]);

        Assert.Equal(new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero), options.PdfCreationDate);
    }

    [Fact]
    public void CreationDate_Invalid_IsError()
    {
        var options = ArgumentParser.Parse(["--pdf-creation-date=yesterday", "doc.html"]);

        Assert.Contains("--pdf-creation-date", Assert.Single(options.Errors));
    }

    [Fact]
    public void CreationDate_MapsToMetadata()
    {
        var config = CliRunner.BuildConfig(ArgumentParser.Parse(
            ["--pdfa=2b", "--pdf-creation-date=2020-01-02T03:04:05Z", "doc.html"]));

        Assert.Equal(new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero), config.Metadata!.CreationDate);
    }

    [Fact]
    public void CreationDate_WithoutPdfA_IsStillApplied()
    {
        var config = CliRunner.BuildConfig(ArgumentParser.Parse(["--pdf-creation-date=2020-01-02", "doc.html"]));

        Assert.Equal(new DateTimeOffset(2020, 1, 2, 0, 0, 0, TimeSpan.Zero), config.Metadata!.CreationDate);
    }

    // --- --attach ---

    [Fact]
    public void Attach_FileOnly_HasNoExplicitMimeOrRelationship()
    {
        var options = ArgumentParser.Parse(["--attach=data.csv", "doc.html"]);

        Assert.Empty(options.Errors);
        var attachment = Assert.Single(options.Attachments);
        Assert.Equal("data.csv", attachment.Path);
        Assert.Null(attachment.MimeType);
        Assert.Null(attachment.Relationship);
    }

    [Fact]
    public void Attach_WithSettings_ParsesMimeAndRelationship_AnyCase()
    {
        var options = ArgumentParser.Parse(["--attach=data.bin; MIME=application/x-thing ; Rel=Source", "doc.html"]);

        Assert.Empty(options.Errors);
        var attachment = Assert.Single(options.Attachments);
        Assert.Equal("data.bin", attachment.Path);
        Assert.Equal("application/x-thing", attachment.MimeType);
        Assert.Equal(PdfAttachmentRelationship.Source, attachment.Relationship);
    }

    [Fact]
    public void Attach_IsRepeatable_InOrder()
    {
        var options = ArgumentParser.Parse(["--attach=a.txt", "--attach", "b.txt;rel=supplement", "doc.html"]);

        Assert.Equal(["a.txt", "b.txt"], options.Attachments.Select(a => a.Path));
        Assert.Equal(PdfAttachmentRelationship.Supplement, options.Attachments[1].Relationship);
    }

    [Theory]
    [InlineData(";mime=text/plain")]
    [InlineData("a.txt;mime=plain")]
    [InlineData("a.txt;rel=cousin")]
    [InlineData("a.txt;colour=red")]
    [InlineData("a.txt;mime")]
    public void Attach_BadValue_IsError(string value)
    {
        var options = ArgumentParser.Parse(["--attach", value, "doc.html"]);

        Assert.Contains("--attach", Assert.Single(options.Errors));
        Assert.Empty(options.Attachments);
    }

    [Fact]
    public void Attach_MapsToConfigAttachments_ReadingTheFile()
    {
        var dir = CreateTempDir();
        try
        {
            var csv = Path.Combine(dir, "sales.csv");
            File.WriteAllText(csv, "a,b\n1,2\n");

            var config = CliRunner.BuildConfig(ArgumentParser.Parse(
                [$"--attach={csv};rel=data", "--pdfa=3b", "doc.html"]));

            var attachment = Assert.Single(config.Attachments);
            Assert.Equal("sales.csv", attachment.FileName);
            Assert.Equal("text/csv", attachment.MimeType);
            Assert.Equal(PdfAttachmentRelationship.Data, attachment.Relationship);
            Assert.Equal("a,b\n1,2\n", System.Text.Encoding.UTF8.GetString(attachment.Data));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Attach_ExplicitMimeAndDefaultRelationship_AreUsed()
    {
        var dir = CreateTempDir();
        try
        {
            var file = Path.Combine(dir, "blob.dat");
            File.WriteAllBytes(file, [1, 2, 3]);

            var config = CliRunner.BuildConfig(ArgumentParser.Parse([$"--attach={file};mime=application/x-blob", "doc.html"]));

            var attachment = Assert.Single(config.Attachments);
            Assert.Equal("application/x-blob", attachment.MimeType);
            Assert.Equal(PdfAttachmentRelationship.Unspecified, attachment.Relationship);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Attach_MissingFile_ThrowsAnIOException_WhichTheRunnerReportsAsAnError()
    {
        var options = ArgumentParser.Parse(["--attach=/definitely/not/here.txt", "doc.html"]);

        Assert.ThrowsAny<IOException>(() => CliRunner.BuildConfig(options));
    }

    [Theory]
    [InlineData("a.txt", "text/plain")]
    [InlineData("A.CSV", "text/csv")]
    [InlineData("a.xml", "text/xml")]
    [InlineData("a.json", "application/json")]
    [InlineData("a.html", "text/html")]
    [InlineData("a.htm", "text/html")]
    [InlineData("a.pdf", "application/pdf")]
    [InlineData("a.png", "image/png")]
    [InlineData("a.jpg", "image/jpeg")]
    [InlineData("a.jpeg", "image/jpeg")]
    [InlineData("a.gif", "image/gif")]
    [InlineData("a.tif", "image/tiff")]
    [InlineData("a.tiff", "image/tiff")]
    [InlineData("a.xls", "application/vnd.ms-excel")]
    [InlineData("a.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData("a.ods", "application/vnd.oasis.opendocument.spreadsheet")]
    [InlineData("a.unknown", "application/octet-stream")]
    [InlineData("noextension", "application/octet-stream")]
    public void GuessMimeType_FollowsTheExtension(string path, string expected)
    {
        Assert.Equal(expected, CliRunner.GuessMimeType(path));
    }

    // --- --facturx-xml / --facturx-profile ---

    [Theory]
    [InlineData("minimum", FacturXProfile.Minimum)]
    [InlineData("basic-wl", FacturXProfile.BasicWl)]
    [InlineData("BASICWL", FacturXProfile.BasicWl)]
    [InlineData("basic_wl", FacturXProfile.BasicWl)]
    [InlineData("basic", FacturXProfile.Basic)]
    [InlineData("en16931", FacturXProfile.En16931)]
    [InlineData("EN-16931", FacturXProfile.En16931)]
    [InlineData("EN 16931", FacturXProfile.En16931)] // the specification's own spelling
    [InlineData("BASIC WL", FacturXProfile.BasicWl)]
    [InlineData("extended", FacturXProfile.Extended)]
    [InlineData("xrechnung", FacturXProfile.XRechnung)]
    public void FacturXProfile_IsParsed(string value, FacturXProfile expected)
    {
        var options = ArgumentParser.Parse([$"--facturx-profile={value}", "--facturx-xml=inv.xml", "--pdfa=3b", "doc.html"]);

        Assert.Empty(options.Errors);
        Assert.Equal(expected, options.FacturXProfile);
    }

    [Fact]
    public void FacturXProfile_Unknown_IsError()
    {
        var options = ArgumentParser.Parse(["--facturx-profile=comfort", "--facturx-xml=inv.xml", "--pdfa=3b", "doc.html"]);

        Assert.Contains("--facturx-profile", Assert.Single(options.Errors));
    }

    [Fact]
    public void FacturXProfile_WithoutXml_IsError()
    {
        var options = ArgumentParser.Parse(["--facturx-profile=basic", "--pdfa=3b", "doc.html"]);

        Assert.Contains("without --facturx-xml", Assert.Single(options.Errors));
    }

    [Fact]
    public void FacturXXml_WithoutAnyPdfA_IsError()
    {
        var options = ArgumentParser.Parse(["--facturx-xml=inv.xml", "doc.html"]);

        Assert.Contains("PDF/A-3", Assert.Single(options.Errors));
    }

    [Theory]
    [InlineData("--pdfa=2b")]
    [InlineData("--pdfa=1a")]
    public void FacturXXml_WithAPdfALevelBelow3_IsError(string pdfaOption)
    {
        var options = ArgumentParser.Parse([pdfaOption, "--facturx-xml=inv.xml", "doc.html"]);

        Assert.Contains("PDF/A-3", Assert.Single(options.Errors));
    }

    [Theory]
    [InlineData("3a")]
    [InlineData("3b")]
    [InlineData("3u")]
    public void FacturXXml_WithAPdfA3Level_IsAccepted(string level)
    {
        var options = ArgumentParser.Parse([$"--pdfa={level}", "--facturx-xml=inv.xml", "doc.html"]);

        Assert.Empty(options.Errors);
        Assert.Equal("inv.xml", options.FacturXXmlPath);
    }

    [Fact]
    public void FacturX_MapsToConfig_ReadingTheXml()
    {
        var dir = CreateTempDir();
        try
        {
            var xml = Path.Combine(dir, "invoice.xml");
            File.WriteAllText(xml, CiiEn16931);

            var config = CliRunner.BuildConfig(ArgumentParser.Parse(
                ["--pdfa=3a", $"--facturx-xml={xml}", "--facturx-profile=en16931", "doc.html"]));

            Assert.NotNull(config.FacturX);
            Assert.Equal(FacturXProfile.En16931, config.FacturX!.Profile);
            Assert.Equal(CiiEn16931, System.Text.Encoding.UTF8.GetString(config.FacturX.Xml));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void FacturX_WithoutTheOption_LeavesTheConfigUntouched()
    {
        var config = CliRunner.BuildConfig(ArgumentParser.Parse(["doc.html"]));

        Assert.Null(config.FacturX);
        Assert.Empty(config.Attachments);
    }

    // --- Real runs ---

    [Fact]
    public async Task Run_WithPdfA_UsesTheDateTheDocumentCarries()
    {
        var dir = CreateTempDir();
        try
        {
            var html = Path.Combine(dir, "dated.html");
            var pdf = Path.Combine(dir, "dated.pdf");
            File.WriteAllText(html, "<html><head><meta name=\"date\" content=\"2024-03-15\"></head><body><p>Hi</p></body></html>");

            var exit = await CliRunner.RunAsync(ArgumentParser.Parse([html, "--pdfa=2b", "-o", pdf]), TextReader.Null, Stream.Null);

            Assert.Equal(0, exit);
            Assert.Contains("<xmp:CreateDate>2024-03-15", PdfText(File.ReadAllBytes(pdf)));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Run_WithPdfA_AndADatelessDocument_FailsUntilADateIsGiven()
    {
        var dir = CreateTempDir();
        try
        {
            var html = Path.Combine(dir, "undated.html");
            var pdf = Path.Combine(dir, "undated.pdf");
            File.WriteAllText(html, Html);

            // Nothing is invented: PDF/A needs a creation date, and neither the document nor the command line gave one.
            var failed = await CliRunner.RunAsync(ArgumentParser.Parse([html, "--pdfa=2b", "-o", pdf]), TextReader.Null, Stream.Null);
            Assert.Equal(1, failed);
            Assert.False(File.Exists(pdf));

            var succeeded = await CliRunner.RunAsync(
                ArgumentParser.Parse([html, "--pdfa=2b", "--pdf-creation-date=2026-09-21", "-o", pdf]), TextReader.Null, Stream.Null);
            Assert.Equal(0, succeeded);
            Assert.Contains("<xmp:CreateDate>2026-09-21", PdfText(File.ReadAllBytes(pdf)));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Run_WithAttach_EmbedsTheFile()
    {
        var dir = CreateTempDir();
        try
        {
            var html = Path.Combine(dir, "report.html");
            var csv = Path.Combine(dir, "sales.csv");
            var pdf = Path.Combine(dir, "report.pdf");
            File.WriteAllText(html, Html);
            File.WriteAllText(csv, "a,b\n1,2\n");

            var options = ArgumentParser.Parse([html, "--pdfa=3b", "--pdf-creation-date=2026-09-21", $"--attach={csv};rel=data", "-o", pdf]);
            var exit = await CliRunner.RunAsync(options, TextReader.Null, Stream.Null);

            Assert.Equal(0, exit);
            var text = PdfText(File.ReadAllBytes(pdf));
            Assert.Contains("/EmbeddedFiles", text);
            Assert.Matches(@"/F\s*\(sales\.csv\)", text);
            Assert.Matches(@"/AFRelationship\s*/Data", text);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Run_WithFacturX_ProducesAnEInvoice()
    {
        var dir = CreateTempDir();
        try
        {
            var html = Path.Combine(dir, "invoice.html");
            var xml = Path.Combine(dir, "invoice.xml");
            var pdf = Path.Combine(dir, "invoice.pdf");
            File.WriteAllText(html, Html);
            File.WriteAllText(xml, CiiEn16931);

            var options = ArgumentParser.Parse(
                [html, "--pdfa=3b", "--pdf-creation-date=2026-09-21", $"--facturx-xml={xml}", "-o", pdf]);
            var exit = await CliRunner.RunAsync(options, TextReader.Null, Stream.Null);

            Assert.Equal(0, exit);
            var text = PdfText(File.ReadAllBytes(pdf));
            Assert.Matches(@"/F\s*\(factur-x\.xml\)", text);
            Assert.Contains("<fx:ConformanceLevel>EN 16931</fx:ConformanceLevel>", text);
            Assert.Contains("<fx:DocumentFileName>factur-x.xml</fx:DocumentFileName>", text);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Run_WithSeveralInputs_AndFacturX_EmbedsTheInvoiceOnce()
    {
        var dir = CreateTempDir();
        try
        {
            var first = Path.Combine(dir, "one.html");
            var second = Path.Combine(dir, "two.html");
            var xml = Path.Combine(dir, "invoice.xml");
            var pdf = Path.Combine(dir, "both.pdf");
            File.WriteAllText(first, Html);
            File.WriteAllText(second, Html);
            File.WriteAllText(xml, CiiEn16931);

            var options = ArgumentParser.Parse([first, second, "--pdfa=3b", "--pdf-creation-date=2026-09-21", $"--facturx-xml={xml}", "-o", pdf]);
            var exit = await CliRunner.RunAsync(options, TextReader.Null, Stream.Null);

            Assert.Equal(0, exit);
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(PdfText(File.ReadAllBytes(pdf)), @"/Type\s*/Filespec"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Run_WithAMissingFacturXFile_FailsWithAnError()
    {
        var dir = CreateTempDir();
        try
        {
            var html = Path.Combine(dir, "invoice.html");
            File.WriteAllText(html, Html);

            var options = ArgumentParser.Parse([html, "--pdfa=3b", "--pdf-creation-date=2026-09-21", $"--facturx-xml={Path.Combine(dir, "nope.xml")}"]);
            var exit = await CliRunner.RunAsync(options, TextReader.Null, Stream.Null);

            Assert.Equal(1, exit);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Run_WithAProfileThatContradictsTheXml_FailsWithAnError()
    {
        var dir = CreateTempDir();
        try
        {
            var html = Path.Combine(dir, "invoice.html");
            var xml = Path.Combine(dir, "invoice.xml");
            File.WriteAllText(html, Html);
            File.WriteAllText(xml, CiiEn16931);

            var options = ArgumentParser.Parse([html, "--pdfa=3b", "--pdf-creation-date=2026-09-21", $"--facturx-xml={xml}", "--facturx-profile=extended"]);
            var exit = await CliRunner.RunAsync(options, TextReader.Null, Stream.Null);

            Assert.Equal(1, exit);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
