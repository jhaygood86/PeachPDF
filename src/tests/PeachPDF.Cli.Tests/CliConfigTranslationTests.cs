using System;
using PeachPDF;
using PeachPDF.Cli;

namespace PeachPDF.Cli.Tests;

public class CliConfigTranslationTests
{
    [Fact]
    public void JxlPassthrough_IsOffUnlessRequested()
    {
        Assert.False(CliRunner.BuildConfig(ArgumentParser.Parse(["doc.html"])).JxlPassthrough);
        Assert.True(CliRunner.BuildConfig(ArgumentParser.Parse(["--jxl-passthrough", "doc.html"])).JxlPassthrough);
    }

    [Fact]
    public void PdfVersionAndBrotli_AreApplied()
    {
        var config = CliRunner.BuildConfig(ArgumentParser.Parse(["--pdf-version", "2.0", "--brotli", "doc.html"]));
        Assert.Equal(PdfVersion.Pdf20, config.PdfVersion);
        Assert.True(config.BrotliCompression);

        var legacy = CliRunner.BuildConfig(ArgumentParser.Parse(["--pdf-version=1.7", "doc.html"]));
        Assert.Equal(PdfVersion.Pdf17, legacy.PdfVersion);
        Assert.False(legacy.BrotliCompression);
    }

    [Theory]
    [InlineData("auto", PdfDocumentMode.Auto)]
    [InlineData("no-quirks", PdfDocumentMode.NoQuirks)]
    [InlineData("limited-quirks", PdfDocumentMode.LimitedQuirks)]
    [InlineData("Quirks", PdfDocumentMode.Quirks)]
    public void DocumentMode_IsAppliedToTheConfig(string value, PdfDocumentMode expected)
    {
        var config = CliRunner.BuildConfig(ArgumentParser.Parse([$"--document-mode={value}", "doc.html"]));

        Assert.Equal(expected, config.DocumentMode);
    }

    [Fact]
    public void DocumentMode_DefaultsToAuto_AndRejectsAnUnknownValue()
    {
        Assert.Equal(PdfDocumentMode.Auto, CliRunner.BuildConfig(ArgumentParser.Parse(["doc.html"])).DocumentMode);

        var options = ArgumentParser.Parse(["--document-mode", "strict", "doc.html"]);
        Assert.Contains(options.Errors, e => e.Contains("--document-mode"));

        var missing = ArgumentParser.Parse(["doc.html", "--document-mode"]);
        Assert.NotEmpty(missing.Errors);
        Assert.Equal(PdfDocumentMode.Auto, missing.DocumentMode);
    }

    [Theory]
    [InlineData("auto", PdfDocumentSyntax.Auto)]
    [InlineData("html", PdfDocumentSyntax.Html)]
    [InlineData("XML", PdfDocumentSyntax.Xml)]
    public void DocumentSyntax_IsParsed(string value, PdfDocumentSyntax expected)
    {
        Assert.Equal(expected, ArgumentParser.Parse([$"--document-syntax={value}", "doc.html"]).DocumentSyntax);
    }

    [Fact]
    public void DocumentSyntax_DefaultsToAuto_AndRejectsAnUnknownValue()
    {
        Assert.Equal(PdfDocumentSyntax.Auto, ArgumentParser.Parse(["doc.html"]).DocumentSyntax);
        Assert.Contains(ArgumentParser.Parse(["--document-syntax", "sgml", "doc.html"]).Errors, e => e.Contains("--document-syntax"));
        Assert.NotEmpty(ArgumentParser.Parse(["doc.html", "--document-syntax"]).Errors);
    }

    [Theory]
    [InlineData("a.xhtml", "File", PdfDocumentSyntax.Auto, null, PdfDocumentSyntax.Xml)]
    [InlineData("A.XHT", "File", PdfDocumentSyntax.Auto, null, PdfDocumentSyntax.Xml)]
    [InlineData("a.html", "File", PdfDocumentSyntax.Auto, null, PdfDocumentSyntax.Html)]
    [InlineData("a.xml", "File", PdfDocumentSyntax.Auto, null, PdfDocumentSyntax.Html)]
    [InlineData("a.xhtml", "File", PdfDocumentSyntax.Html, null, PdfDocumentSyntax.Html)]
    [InlineData("a.html", "File", PdfDocumentSyntax.Xml, null, PdfDocumentSyntax.Xml)]
    [InlineData("https://x/y", "Url", PdfDocumentSyntax.Auto, "application/xhtml+xml; charset=utf-8", PdfDocumentSyntax.Xml)]
    [InlineData("https://x/y.xhtml", "Url", PdfDocumentSyntax.Auto, "text/html", PdfDocumentSyntax.Html)]
    [InlineData("https://x/y", "Url", PdfDocumentSyntax.Auto, null, PdfDocumentSyntax.Html)]
    [InlineData("-", "Stdin", PdfDocumentSyntax.Auto, null, PdfDocumentSyntax.Html)]
    [InlineData("-", "Stdin", PdfDocumentSyntax.Xml, null, PdfDocumentSyntax.Xml)]
    public void ResolveSyntax_UsesTheExtensionOrTheContentType(string value, string kind, PdfDocumentSyntax requested, string? contentType, PdfDocumentSyntax expected)
    {
        Assert.Equal(expected, CliRunner.ResolveSyntax(new CliInput(Enum.Parse<CliInputKind>(kind), value), requested, contentType));
    }

    [Fact]
    public void PdfVersion_WithoutAValue_IsAnError()
    {
        var options = ArgumentParser.Parse(["doc.html", "--pdf-version"]);
        Assert.NotEmpty(options.Errors);
        Assert.Null(options.PdfVersion);
    }

    [Fact]
    public void InvalidPdfVersion_IsAnError()
    {
        var options = ArgumentParser.Parse(["--pdf-version", "3.1", "doc.html"]);
        Assert.Contains(options.Errors, e => e.Contains("--pdf-version"));
    }

    [Fact]
    public void Defaults_UseLetterPortrait_PrintMedia_Compressed()
    {
        var config = CliRunner.BuildConfig(ArgumentParser.Parse(["doc.html"]));

        Assert.Equal(PageSize.Letter, config.PageSize);
        Assert.Equal(PageOrientation.Portrait, config.PageOrientation);
        Assert.True(config.CompressContentStreams);
        Assert.False(config.EnableTaggedPdf);
        Assert.False(config.SnapBoxDecorationsToCssPixels);
        Assert.False(config.SnapBorderWidthsToCssPixels);
        Assert.False(config.EnableInteractivePdfForms);
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
            ["--tagged-pdf", "--snap-box-decorations-to-css-pixels", "--snap-border-widths-to-css-pixels", "--interactive-pdf-forms", "--no-compress", "--no-author-style", "doc.html"]));

        Assert.True(config.EnableTaggedPdf);
        Assert.True(config.SnapBoxDecorationsToCssPixels);
        Assert.True(config.SnapBorderWidthsToCssPixels);
        Assert.True(config.EnableInteractivePdfForms);
        Assert.False(config.CompressContentStreams);
        Assert.True(config.IgnoreAuthorStyleSheets);
    }

    [Fact]
    public void RasterDpi_DefaultsToTheLibraryDefault_AndMapsWhenGiven()
    {
        Assert.Equal(new PdfGenerateConfig().RasterizationDpi, CliRunner.BuildConfig(ArgumentParser.Parse(["doc.html"])).RasterizationDpi);

        Assert.Equal(600, CliRunner.BuildConfig(ArgumentParser.Parse(["--raster-dpi=600", "doc.html"])).RasterizationDpi);
        Assert.Equal(288, CliRunner.BuildConfig(ArgumentParser.Parse(["--raster-dpi", "288", "doc.html"])).RasterizationDpi);
    }

    [Fact]
    public void AnimationProgress_DefaultsToOff_AndMapsStartEndAndNumbers()
    {
        Assert.Null(CliRunner.BuildConfig(ArgumentParser.Parse(["doc.html"])).AnimationProgress);

        Assert.Equal(0, CliRunner.BuildConfig(ArgumentParser.Parse(["--animation-progress=start", "doc.html"])).AnimationProgress);
        Assert.Equal(1, CliRunner.BuildConfig(ArgumentParser.Parse(["--animation-progress=END", "doc.html"])).AnimationProgress);
        Assert.Equal(0.25, CliRunner.BuildConfig(ArgumentParser.Parse(["--animation-progress=0.25", "doc.html"])).AnimationProgress);
        Assert.Equal(0.5, CliRunner.BuildConfig(ArgumentParser.Parse(["--animation-progress", "0.5", "doc.html"])).AnimationProgress);
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("-0.1")]
    [InlineData("middle")]
    [InlineData("")]
    public void AnimationProgress_OutOfRangeOrMalformed_IsAnArgumentError(string value)
    {
        var options = ArgumentParser.Parse([$"--animation-progress={value}", "doc.html"]);

        Assert.NotEmpty(options.Errors);
    }

    [Fact]
    public void AnimationProgress_WithoutAValue_IsAnArgumentError() =>
        Assert.NotEmpty(ArgumentParser.Parse(["doc.html", "--animation-progress"]).Errors);

    [Fact]
    public void FlattenTransparency_DefaultsToReject_AndMapsWhenGiven()
    {
        Assert.Equal(TransparencyPolicy.Reject, CliRunner.BuildConfig(ArgumentParser.Parse(["doc.html"])).TransparencyPolicy);
        Assert.Equal(TransparencyPolicy.Flatten, CliRunner.BuildConfig(ArgumentParser.Parse(["--flatten-transparency", "doc.html"])).TransparencyPolicy);
    }

    [Fact]
    public void RasterAntiAliasing_DefaultsToOn_AndMapsWhenDisabled()
    {
        Assert.True(CliRunner.BuildConfig(ArgumentParser.Parse(["doc.html"])).RasterAntiAliasing);
        Assert.False(CliRunner.BuildConfig(ArgumentParser.Parse(["--no-raster-antialiasing", "doc.html"])).RasterAntiAliasing);
    }

    [Theory]
    [InlineData("50")]
    [InlineData("1201")]
    [InlineData("abc")]
    [InlineData("")]
    public void RasterDpi_OutOfRangeOrMalformed_IsAnArgumentError(string value)
    {
        var options = ArgumentParser.Parse([$"--raster-dpi={value}", "doc.html"]);

        Assert.NotEmpty(options.Errors);
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
