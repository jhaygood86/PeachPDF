using PeachPDF;
using PeachPDF.Cli;

namespace PeachPDF.Cli.Tests;

public class CliConfigTranslationTests
{
    [Fact]
    public void Defaults_UseLetterPortrait_PrintMedia_Compressed()
    {
        var config = CliRunner.BuildConfig(ArgumentParser.Parse(["doc.html"]));

        Assert.Equal(PageSize.Letter, config.PageSize);
        Assert.Equal(PageOrientation.Portrait, config.PageOrientation);
        Assert.Equal("print", config.Media);
        Assert.True(config.CompressContentStreams);
        Assert.False(config.EnableTaggedPdf);
        Assert.False(config.IgnoreAuthorStyleSheets);
        Assert.Null(config.Metadata);
    }

    [Fact]
    public void NamedPageSizeAndOrientation_AreApplied()
    {
        var config = CliRunner.BuildConfig(ArgumentParser.Parse(["--page-size", "A5 landscape", "doc.html"]));
        Assert.Equal(PageSize.A5, config.PageSize);
        Assert.Equal(PageOrientation.Landscape, config.PageOrientation);
    }

    [Fact]
    public void ManualPageSize_SetsUndefinedWithDimensions()
    {
        var config = CliRunner.BuildConfig(ArgumentParser.Parse(["--page-size", "200pt 300pt", "doc.html"]));
        Assert.Equal(PageSize.Undefined, config.PageSize);
        Assert.Equal(200.0, config.ManualPageWidth, 3);
        Assert.Equal(300.0, config.ManualPageHeight, 3);
    }

    [Fact]
    public void PageMargin_IsRoundedToPoints()
    {
        var config = CliRunner.BuildConfig(ArgumentParser.Parse(["--page-margin", "10pt 20pt 30pt 40pt", "doc.html"]));
        Assert.Equal(10, config.MarginTop);
        Assert.Equal(20, config.MarginRight);
        Assert.Equal(30, config.MarginBottom);
        Assert.Equal(40, config.MarginLeft);
    }

    [Fact]
    public void Toggles_MapToConfig()
    {
        var config = CliRunner.BuildConfig(ArgumentParser.Parse(
            ["--tagged-pdf", "--no-compress", "--media", "screen", "--no-author-style", "doc.html"]));

        Assert.True(config.EnableTaggedPdf);
        Assert.False(config.CompressContentStreams);
        Assert.Equal("screen", config.Media);
        Assert.True(config.IgnoreAuthorStyleSheets);
    }

    [Fact]
    public void Metadata_OverridesArePopulated()
    {
        var config = CliRunner.BuildConfig(ArgumentParser.Parse([
            "--pdf-title", "T", "--pdf-author", "A", "--pdf-subject", "S",
            "--pdf-keywords", "K", "--pdf-creator", "C", "doc.html",
        ]));

        Assert.NotNull(config.Metadata);
        Assert.Equal("T", config.Metadata!.Title);
        Assert.Equal("A", config.Metadata.Author);
        Assert.Equal("S", config.Metadata.Subject);
        Assert.Equal("K", config.Metadata.Keywords);
        Assert.Equal("C", config.Metadata.Creator);
    }

    [Fact]
    public void PdfLang_MapsToDefaultLanguage()
    {
        var config = CliRunner.BuildConfig(ArgumentParser.Parse(["--pdf-lang", "fr-FR", "doc.html"]));
        Assert.Equal("fr-FR", config.DefaultLanguage);
    }

    [Fact]
    public void NoMetadataFlags_LeaveMetadataNull()
    {
        var config = CliRunner.BuildConfig(ArgumentParser.Parse(["doc.html"]));
        Assert.Null(config.Metadata);
    }
}
