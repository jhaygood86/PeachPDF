using PeachBrackets.ScriptHosting;
using PeachBrackets.Scripting;

namespace PeachPDF.Cli.Tests;

/// <summary>Objects a PeachPDF user adds to the scripts through <see cref="PdfGenerateConfig.ScriptApis"/>.</summary>
public class ScriptApiConfigTests
{
    [Fact]
    public async Task AConfiguredApi_IsReachableFromTheDocumentsScripts_AndToldTheLayoutIsFinal()
    {
        var title = "";
        var pages = -1;
        var api = new ScriptApi("Settings").AddProperty("title", () => title, v => title = v.AsString() ?? string.Empty);
        api.LayoutFinished += layout => pages = layout.PageCount;

        var config = new PdfGenerateConfig { ScriptHost = new JintScriptHost() };
        config.ScriptApis.Add(api);

        var document = await new PdfGenerator().GeneratePdf("<html><body><p>hi</p><script>Settings.title = 'from the script';</script></body></html>", config);

        Assert.NotNull(document);
        Assert.Equal("from the script", title);
        Assert.Equal(1, pages);
    }

    [Fact]
    public async Task WithoutAScriptHost_TheApisAreIgnored()
    {
        var touched = false;
        var api = new ScriptApi("Settings").AddValue("x", 1);
        api.ScriptsStarting += _ => touched = true;
        var config = new PdfGenerateConfig();
        config.ScriptApis.Add(api);

        await new PdfGenerator().GeneratePdf("<html><body><script>Settings.x;</script></body></html>", config);

        Assert.False(touched);
    }
}
