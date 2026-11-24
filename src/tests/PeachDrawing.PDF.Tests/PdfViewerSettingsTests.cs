using System.Text;
using System.Text.RegularExpressions;
using PeachDrawing.PDF;

namespace PeachDrawing.PDF.Tests;

/// <summary>The catalog entries a viewer reads when it opens the file: page mode, page layout, viewer preferences and the open action.</summary>
public class PdfViewerSettingsTests
{
    private static string Write(Action<PdfComposerOptions> configure, int pages = 1)
    {
        var options = new PdfComposerOptions { CompressContentStreams = false };
        configure(options);
        var context = new PdfRenderContext();
        using var composer = new PdfComposer(context, options);
        for (var i = 0; i < pages; i++)
            composer.AddPage(200, 300);

        using var stream = new MemoryStream();
        composer.Save(stream);
        return Encoding.Latin1.GetString(stream.ToArray());
    }

    [Theory]
    [InlineData(PdfPageMode.UseOutlines, "/UseOutlines")]
    [InlineData(PdfPageMode.UseThumbs, "/UseThumbs")]
    [InlineData(PdfPageMode.FullScreen, "/FullScreen")]
    [InlineData(PdfPageMode.UseAttachments, "/UseAttachments")]
    public void PageMode_IsWrittenToTheCatalog(PdfPageMode mode, string name)
    {
        var pdf = Write(o => o.PageMode = mode);

        Assert.Matches(new Regex(@"/PageMode\s*" + Regex.Escape(name)), pdf);
    }

    [Theory]
    [InlineData(PdfPageLayout.OneColumn, "/OneColumn")]
    [InlineData(PdfPageLayout.TwoColumnLeft, "/TwoColumnLeft")]
    [InlineData(PdfPageLayout.TwoPageRight, "/TwoPageRight")]
    public void PageLayout_IsWrittenToTheCatalog(PdfPageLayout layout, string name)
    {
        var pdf = Write(o => o.PageLayout = layout);

        Assert.Matches(new Regex(@"/PageLayout\s*" + Regex.Escape(name)), pdf);
    }

    [Fact]
    public void NothingSet_WritesNoneOfThem()
    {
        var pdf = Write(_ => { });

        Assert.DoesNotContain("/PageLayout", pdf);
        Assert.DoesNotContain("/OpenAction", pdf);
        Assert.DoesNotContain("/ViewerPreferences", pdf);
        Assert.DoesNotContain("/PageMode", pdf);
    }

    [Fact]
    public void ViewerPreferences_AreWrittenAsTheyAreSet()
    {
        var pdf = Write(o => o.Viewer = new PdfViewerOptions
        {
            DisplayDocTitle = true,
            HideToolbar = true,
            FitWindow = true,
            PrintScaling = PdfPrintScaling.None,
            Duplex = PdfDuplex.FlipLongEdge,
        });

        var preferences = Regex.Match(pdf, @"/ViewerPreferences.*?>>", RegexOptions.Singleline).Value;
        _ = preferences;
        Assert.Matches(@"/DisplayDocTitle\s+true", pdf);
        Assert.Matches(@"/HideToolbar\s+true", pdf);
        Assert.Matches(@"/FitWindow\s+true", pdf);
        Assert.Matches(@"/PrintScaling\s*/None", pdf);
        Assert.Matches(@"/Duplex\s*/DuplexFlipLongEdge", pdf);
        Assert.DoesNotContain("/HideMenubar", pdf);
        Assert.DoesNotContain("/CenterWindow", pdf);
    }

    [Theory]
    [InlineData(PdfOpenAction.FitPage, "/Fit")]
    [InlineData(PdfOpenAction.FitWidth, "/FitH null")]
    public void AnOpenDestination_PointsAtTheFirstPage(PdfOpenAction action, string fit)
    {
        var pdf = Write(o => o.OpenAction = action, pages: 2);

        // The catalog's /OpenAction is an array that starts with the object number of the first page.
        var open = Regex.Match(pdf, @"/OpenAction\s*\[\s*(\d+) 0 R\s*" + Regex.Escape(fit) + @"\s*\]");
        Assert.True(open.Success, "no destination array in the catalog");
        var firstPage = Regex.Match(pdf, @"/Kids\s*\[\s*(\d+) 0 R");
        Assert.Equal(firstPage.Groups[1].Value, open.Groups[1].Value);
    }

    [Fact]
    public void PrintAsTheOpenAction_IsANamedAction()
    {
        var pdf = Write(o => o.OpenAction = PdfOpenAction.Print);

        Assert.Matches(@"/OpenAction\s*<<", pdf);
        Assert.Matches(@"/S\s*/Named", pdf);
        Assert.Matches(@"/N\s*/Print", pdf);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PrintAsTheOpenAction_IsRefusedInAPdfAOrPdfXFile(bool pdfA)
    {
        var options = new PdfComposerOptions { OpenAction = PdfOpenAction.Print };
        if (pdfA)
            options.Conformance = PdfAConformance.PdfA2B;
        else
            options.XConformance = PdfXConformance.X4;

        var context = new PdfRenderContext();
        using var composer = new PdfComposer(context);

        Assert.Throws<InvalidOperationException>(() => composer.Configure(options));
    }
}
