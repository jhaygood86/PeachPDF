using System.Text;
using System.Text.RegularExpressions;
using PeachBrackets.Scripting;

namespace PeachPDF.Cli.Tests;

/// <summary>The <c>PDF</c> object and <c>Prince.failStatus</c>: what a document's scripts say about the file PeachPDF writes.</summary>
public class PdfScriptObjectTests
{
    private static string Utf16Hex(string text) => "FEFF" + Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(text));

    private static async Task<string> RenderAsync(string script, Action<PdfGenerateConfig>? configure = null, string body = "<p>hi</p>")
    {
        var config = new PdfGenerateConfig { ScriptHost = new JintScriptHost(), CompressContentStreams = false };
        configure?.Invoke(config);
        var document = await new PdfGenerator().GeneratePdf($"<html><body>{body}<script>{script}</script></body></html>", config);

        using var stream = new MemoryStream();
        document.Save(stream);
        // A long hex string is wrapped over several lines in the file; its digits are what is compared.
        return Regex.Replace(Encoding.Latin1.GetString(stream.ToArray()), @"(?<=[0-9A-Fa-f])
?
(?=[0-9A-Fa-f])", string.Empty);
    }

    [Fact]
    public async Task DocumentInformation_SetByTheScript_IsWritten_AndTheConfigurationWinsOverIt()
    {
        var pdf = await RenderAsync("PDF.title = 'Scripted title'; PDF.author = 'Scripted author'; PDF.keywords = 'a, b';");

        Assert.Contains(Utf16Hex("Scripted title"), pdf, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Utf16Hex("Scripted author"), pdf, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Utf16Hex("a, b"), pdf, StringComparison.OrdinalIgnoreCase);

        var configured = await RenderAsync("PDF.title = 'Scripted title';", c => c.Metadata = new PdfDocumentMetadata { Title = "Configured title" });
        Assert.Contains(Utf16Hex("Configured title"), configured, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Utf16Hex("Scripted title"), configured, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HowTheViewerOpens_IsWrittenToTheCatalog()
    {
        var pdf = await RenderAsync("PDF.pageMode = 'fullscreen'; PDF.pageLayout = 'two-column-left'; PDF.openAction = 'fit-width'; PDF.printScaling = 'none'; PDF.duplex = 'duplex-flip-short-edge'; PDF.displayDocTitle = true;");

        Assert.Matches(@"/PageMode\s*/FullScreen", pdf);
        Assert.Matches(@"/PageLayout\s*/TwoColumnLeft", pdf);
        Assert.Matches(@"/OpenAction\s*\[\s*\d+ 0 R\s*/FitH null\s*\]", pdf);
        Assert.Matches(@"/PrintScaling\s*/None", pdf);
        Assert.Matches(@"/Duplex\s*/DuplexFlipShortEdge", pdf);
        Assert.Matches(@"/DisplayDocTitle\s+true", pdf);
    }

    [Fact]
    public async Task APrintOpenAction_IsANamedAction_AndTheConfigurationChoiceWins()
    {
        var print = await RenderAsync("PDF.openAction = 'print';");
        Assert.Matches(@"/S\s*/Named", print);
        Assert.Matches(@"/N\s*/Print", print);

        var configured = await RenderAsync("PDF.pageMode = 'fullscreen';", c => c.PageMode = PdfPageMode.UseThumbs);
        Assert.Matches(@"/PageMode\s*/UseThumbs", configured);
        Assert.DoesNotContain("/FullScreen", configured, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APropertyThatTakesNames_RefusesOthers_AndKeyBitsCanOnlyBe256()
    {
        var pdf = await RenderAsync("try { PDF.pageMode = 'sideways'; } catch (e) { PDF.title = 'refused: ' + e.message; } try { PDF.keyBits = 128; } catch (e) { PDF.subject = 'key: ' + e.message; } PDF.keyBits = '256';");

        Assert.Contains(Utf16Hex("refused: PDF.pageMode cannot be 'sideways'; it is one of auto, show-bookmarks, show-thumbnails, fullscreen, show-attachments."), pdf, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Utf16Hex("key: PDF.keyBits can only be 256: PeachPDF encrypts with AES-256 and does not write the weaker 40 and 128 bit formats."), pdf, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AScriptCanAskForEncryption_ButCannotChangeWhatTheConfigurationSet()
    {
        var encrypted = await RenderAsync("PDF.encrypt = true; PDF.userPassword = 'open'; PDF.ownerPassword = 'owner'; PDF.allowPrint = false;");
        Assert.Matches(@"/Encrypt\s+\d+ 0 R", encrypted);

        var plain = await RenderAsync("PDF.title = 'x';");
        Assert.DoesNotContain("/Encrypt", plain, StringComparison.Ordinal);

        // With encryption configured the script's passwords are not used: the file opens with the configured one only. (The same object
        // is written, so what shows is that nothing about the script's request changed the document's encryption dictionary.)
        var configured = await RenderAsync("PDF.encrypt = true; PDF.userPassword = 'script';", c => c.Encryption = new PdfEncryptionOptions { OwnerPassword = "owner" });
        Assert.Matches(@"/Encrypt\s+\d+ 0 R", configured);
    }

    [Fact]
    public async Task AFileTheScriptAttaches_IsEmbedded()
    {
        var pdf = await RenderAsync("PDF.attachFile('data:text/plain;base64,aGVsbG8=', 'A greeting');");

        Assert.Contains("/Type /EmbeddedFile", pdf, StringComparison.Ordinal);
        Assert.Contains("/Type /Filespec", pdf, StringComparison.Ordinal);
        Assert.True(pdf.Contains("(A greeting)", StringComparison.Ordinal) || pdf.Contains(Utf16Hex("A greeting"), StringComparison.OrdinalIgnoreCase), "no description");
        Assert.Contains("attachment", pdf, StringComparison.Ordinal);
        Assert.Contains("hello", pdf, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailStatus_EndsTheConversionWithNoPdf()
    {
        var config = new PdfGenerateConfig { ScriptHost = new JintScriptHost() };

        await Assert.ThrowsAsync<PdfFailSafeException>(() => new PdfGenerator().GeneratePdf(
            "<html><body><p>hi</p><script>Prince.addEventListener('complete', function () { Prince.failStatus = Prince.pageCount > 0; });</script></body></html>", config));

        var fine = await new PdfGenerator().GeneratePdf("<html><body><p>hi</p><script>Prince.failStatus = false;</script></body></html>", new PdfGenerateConfig { ScriptHost = new JintScriptHost() });
        Assert.NotNull(fine);
    }

    [Fact]
    public async Task ThePagesAreAvailableToTheCompleteHandler_WithTheirBoxes()
    {
        var pdf = await RenderAsync(
            "Prince.addEventListener('complete', function () { var p = PDF.pages; PDF.title = 'pages:' + p.length + ':' + p[0].type + ':' + p[0].pageNum + ':' + (p[0].children.length > 0); });");

        Assert.Contains(Utf16Hex("pages:1:BODY:1:true"), pdf, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WithoutAScriptHost_NothingIsRegistered_AndTheNewOptionsStillWork()
    {
        var document = await new PdfGenerator().GeneratePdf("<html><body><p>hi</p></body></html>", new PdfGenerateConfig
        {
            CompressContentStreams = false,
            PageLayout = PdfPageLayout.OneColumn,
            ViewerOptions = new PdfViewerOptions { HideToolbar = true },
        });

        using var stream = new MemoryStream();
        document.Save(stream);
        var pdf = Encoding.Latin1.GetString(stream.ToArray());
        Assert.Matches(@"/PageLayout\s*/OneColumn", pdf);
        Assert.Matches(@"/HideToolbar\s+true", pdf);
        Assert.DoesNotContain("/OpenAction", pdf, StringComparison.Ordinal);
    }
}
