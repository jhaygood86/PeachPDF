using PeachPDF;
using PeachPDF.Cli;

namespace PeachPDF.Cli.Tests;

public class CliArgumentParserTests
{
    [Fact]
    public void PositionalFile_IsClassifiedAsFileInput()
    {
        var options = ArgumentParser.Parse(["doc.html"]);

        Assert.Empty(options.Errors);
        var input = Assert.Single(options.Inputs);
        Assert.Equal(CliInputKind.File, input.Kind);
        Assert.Equal("doc.html", input.Value);
        Assert.Equal(CliOutputKind.Default, options.OutputKind);
    }

    [Fact]
    public void HttpInput_IsClassifiedAsUrl_AndRequiresOutput()
    {
        var options = ArgumentParser.Parse(["https://example.com/page"]);

        Assert.Equal(CliInputKind.Url, options.Inputs[0].Kind);
        // No -o given and the first input is a URL: an explicit output is required.
        Assert.NotEmpty(options.Errors);
    }

    [Fact]
    public void MhtmlExtension_IsClassifiedAsMhtml()
    {
        var options = ArgumentParser.Parse(["archive.mhtml"]);
        Assert.Equal(CliInputKind.Mhtml, options.Inputs[0].Kind);
    }

    [Fact]
    public void Dash_IsStdinInput()
    {
        var options = ArgumentParser.Parse(["-", "-o", "out.pdf"]);

        Assert.Equal(CliInputKind.Stdin, options.Inputs[0].Kind);
        Assert.Equal(CliOutputKind.File, options.OutputKind);
        Assert.Equal("out.pdf", options.OutputPath);
    }

    [Fact]
    public void Output_DashMeansStdout()
    {
        var options = ArgumentParser.Parse(["doc.html", "-o", "-"]);
        Assert.Equal(CliOutputKind.Stdout, options.OutputKind);
    }

    [Fact]
    public void LongOption_AcceptsEqualsForm()
    {
        var options = ArgumentParser.Parse(["--output=out.pdf", "doc.html"]);
        Assert.Equal(CliOutputKind.File, options.OutputKind);
        Assert.Equal("out.pdf", options.OutputPath);
    }

    [Fact]
    public void LongOption_AcceptsSpaceForm()
    {
        var options = ArgumentParser.Parse(["--output", "out.pdf", "doc.html"]);
        Assert.Equal("out.pdf", options.OutputPath);
    }

    [Fact]
    public void Style_IsRepeatable_InOrder()
    {
        var options = ArgumentParser.Parse(["-s", "a.css", "--style=b.css", "doc.html"]);
        Assert.Equal(["a.css", "b.css"], options.StyleSheets);
    }

    [Fact]
    public void HttpHeader_IsRepeatable()
    {
        var options = ArgumentParser.Parse(
            ["--http-header", "X-A: 1", "--http-header=X-B: 2", "doc.html"]);
        Assert.Equal(["X-A: 1", "X-B: 2"], options.HttpHeaders);
    }

    [Fact]
    public void MultipleFiles_AreAllInputs()
    {
        var options = ArgumentParser.Parse(["a.html", "b.html", "-o", "out.pdf"]);
        Assert.Equal(2, options.Inputs.Count);
        Assert.All(options.Inputs, i => Assert.Equal(CliInputKind.File, i.Kind));
    }

    [Theory]
    [InlineData("A4", PageSize.A4)]
    [InlineData("letter", PageSize.Letter)]
    [InlineData("Legal", PageSize.Legal)]
    public void PageSize_NamedSizes(string value, PageSize expected)
    {
        var options = ArgumentParser.Parse(["--page-size", value, "doc.html"]);
        Assert.Empty(options.Errors);
        Assert.Equal(expected, options.PageSize);
    }

    [Fact]
    public void PageSize_NamedWithOrientation()
    {
        var options = ArgumentParser.Parse(["--page-size", "A5 landscape", "doc.html"]);
        Assert.Equal(PageSize.A5, options.PageSize);
        Assert.Equal(PageOrientation.Landscape, options.Orientation);
    }

    [Fact]
    public void PageSize_TwoDimensions_ConvertToPoints()
    {
        var options = ArgumentParser.Parse(["--page-size", "210mm 297mm", "doc.html"]);
        Assert.Equal(PageSize.Undefined, options.PageSize);
        Assert.Equal(210.0 * 72.0 / 25.4, options.ManualPageWidthPt!.Value, 3);
        Assert.Equal(297.0 * 72.0 / 25.4, options.ManualPageHeightPt!.Value, 3);
    }

    [Fact]
    public void PageSize_SingleLength_IsSquare()
    {
        var options = ArgumentParser.Parse(["--page-size", "5in", "doc.html"]);
        Assert.Equal(360.0, options.ManualPageWidthPt!.Value, 3);
        Assert.Equal(360.0, options.ManualPageHeightPt!.Value, 3);
    }

    [Fact]
    public void PageSize_Invalid_IsError()
    {
        var options = ArgumentParser.Parse(["--page-size", "banana", "doc.html"]);
        Assert.NotEmpty(options.Errors);
    }

    [Theory]
    [InlineData("20mm", 56.6929, 56.6929, 56.6929, 56.6929)]
    [InlineData("10pt 20pt", 10, 20, 10, 20)]
    [InlineData("1pt 2pt 3pt", 1, 2, 3, 2)]
    [InlineData("1pt 2pt 3pt 4pt", 1, 2, 3, 4)]
    public void PageMargin_ShorthandOrdering(string value, double top, double right, double bottom, double left)
    {
        var options = ArgumentParser.Parse(["--page-margin", value, "doc.html"]);
        Assert.Empty(options.Errors);
        Assert.Equal(top, options.MarginTopPt!.Value, 3);
        Assert.Equal(right, options.MarginRightPt!.Value, 3);
        Assert.Equal(bottom, options.MarginBottomPt!.Value, 3);
        Assert.Equal(left, options.MarginLeftPt!.Value, 3);
    }

    [Fact]
    public void PageMargin_Invalid_IsError()
    {
        var options = ArgumentParser.Parse(["--page-margin", "wide", "doc.html"]);
        Assert.NotEmpty(options.Errors);
    }

    [Fact]
    public void Metadata_And_Toggles_AreParsed()
    {
        var options = ArgumentParser.Parse([
            "--pdf-title", "T", "--pdf-author", "A", "--pdf-subject", "S",
            "--pdf-keywords", "K", "--pdf-creator", "C", "--pdf-lang", "en-US",
            "--tagged-pdf", "--no-compress", "--media", "screen", "--no-author-style",
            "doc.html",
        ]);

        Assert.Empty(options.Errors);
        Assert.Equal("T", options.PdfTitle);
        Assert.Equal("A", options.PdfAuthor);
        Assert.Equal("S", options.PdfSubject);
        Assert.Equal("K", options.PdfKeywords);
        Assert.Equal("C", options.PdfCreator);
        Assert.Equal("en-US", options.PdfLang);
        Assert.True(options.TaggedPdf);
        Assert.True(options.NoCompress);
        Assert.Equal("screen", options.Media);
        Assert.True(options.NoAuthorStyle);
    }

    [Fact]
    public void HttpTimeout_Invalid_IsError()
    {
        var options = ArgumentParser.Parse(["--http-timeout", "soon", "doc.html"]);
        Assert.NotEmpty(options.Errors);
    }

    [Theory]
    [InlineData("--help", nameof(CliAction.Help))]
    [InlineData("--version", nameof(CliAction.Version))]
    [InlineData("--show-license", nameof(CliAction.ShowLicense))]
    [InlineData("--credits", nameof(CliAction.Credits))]
    public void InformationalFlags_SetAction(string flag, string expectedAction)
    {
        var options = ArgumentParser.Parse([flag]);
        Assert.Equal(expectedAction, options.Action.ToString());
    }

    [Theory]
    [InlineData("--made-up-flag")]
    [InlineData("--another-unknown=value")]
    [InlineData("--xyz")]
    [InlineData("-z")]
    [InlineData("-q")]
    public void UnknownFlags_AreErrors(string flag)
    {
        var options = ArgumentParser.Parse([flag, "doc.html"]);
        Assert.Contains(options.Errors, e => e.Contains("unknown option"));
    }

    [Fact]
    public void InputList_ReadsEntriesFromFile()
    {
        var listPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(listPath, "a.html\n\nb.html\n");
            var options = ArgumentParser.Parse(["-l", listPath]);

            Assert.Empty(options.Errors);
            Assert.Equal(2, options.Inputs.Count);
            Assert.Equal("a.html", options.Inputs[0].Value);
            Assert.Equal("b.html", options.Inputs[1].Value);
        }
        finally
        {
            File.Delete(listPath);
        }
    }

    [Fact]
    public void InputList_MissingFile_IsError()
    {
        var options = ArgumentParser.Parse(["--input-list=/no/such/list.txt"]);
        Assert.NotEmpty(options.Errors);
    }

    [Fact]
    public void NetworkAndAccessOptions_AreParsed()
    {
        var options = ArgumentParser.Parse([
            "--baseurl", "https://example.com/",
            "--no-network", "--no-local-files",
            "--http-proxy", "http://proxy:8080",
            "--insecure", "doc.html",
        ]);

        Assert.Equal("https://example.com/", options.BaseUrl);
        Assert.True(options.NoNetwork);
        Assert.True(options.NoLocalFiles);
        Assert.Equal("http://proxy:8080", options.HttpProxy);
        Assert.True(options.Insecure);
    }

    [Fact]
    public void NoDefaultStyle_IsParsed()
    {
        var options = ArgumentParser.Parse(["--no-default-style", "doc.html"]);
        Assert.True(options.NoDefaultStyle);
    }

    [Fact]
    public void NoInput_IsError()
    {
        var options = ArgumentParser.Parse(["--verbose"]);
        Assert.NotEmpty(options.Errors);
    }

    [Fact]
    public void NoNetwork_WithUrlInput_IsError()
    {
        var options = ArgumentParser.Parse(["--no-network", "https://example.com", "-o", "out.pdf"]);
        Assert.NotEmpty(options.Errors);
    }

    [Fact]
    public void MissingValue_IsError()
    {
        var options = ArgumentParser.Parse(["doc.html", "-o"]);
        Assert.Contains(options.Errors, e => e.Contains("requires a value"));
    }

    [Theory]
    [InlineData("72pt", 72)]
    [InlineData("1in", 72)]
    [InlineData("96px", 72)]
    [InlineData("1pc", 12)]
    [InlineData("25.4mm", 72)]
    [InlineData("2.54cm", 72)]
    [InlineData("50", 50)]
    public void TryParseLength_ConvertsUnits(string token, double expectedPoints)
    {
        Assert.True(ArgumentParser.TryParseLength(token, out var points));
        Assert.Equal(expectedPoints, points, 3);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("10em")]
    [InlineData("")]
    public void TryParseLength_RejectsInvalid(string token)
    {
        Assert.False(ArgumentParser.TryParseLength(token, out _));
    }
}
