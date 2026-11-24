using PeachBrackets.ScriptHosting;

namespace PeachBrackets.Tests;

/// <summary>The seam a script package plugs into: a host that is given the parsed document, changes it, and what it leaves is what is laid out.</summary>
public class ScriptHostTests
{
    private sealed class DelegateHost(Func<ScriptDocument, ValueTask> run) : IScriptHost
    {
        public int Calls { get; private set; }

        public ValueTask RunAsync(ScriptDocument document, CancellationToken cancellationToken = default)
        {
            Calls++;
            return run(document);
        }
    }

    private static HtmlDocumentOptions Options(IScriptHost? host) =>
        new() { MediaType = MediaType.Screen, PageWidthPoints = 400, ScriptHost = host };

    private static async Task<(PeachDrawing.RasterRenderContext Context, HtmlDocument Document)> LoadAsync(string body, IScriptHost? host, string head = "") =>
        await TestDocument.LoadAsync(TestDocument.Wrap(body, head), Options(host));

    [Fact]
    public async Task WithNoHost_TheScriptsAreIgnoredAndNoscriptShows()
    {
        var (_, document) = await LoadAsync("<script>x</script><noscript><p id=n>no script</p></noscript>", null);

        Assert.NotNull(document.GetElement("n"));
    }

    [Fact]
    public async Task AHostSeesTheScriptsInDocumentOrderWithTheirText()
    {
        List<string> seen = [];
        var host = new DelegateHost(document =>
        {
            foreach (var script in document.Scripts)
                seen.Add(script.TextContent + "|" + (script.GetAttribute("src") ?? ""));

            return ValueTask.CompletedTask;
        });

        await LoadAsync("<script>one()</script><p>x</p><script src=\"a.js\"></script><script>two(1 < 2 && 3 > 2)</script>", host);

        Assert.Equal(["one()|", "|a.js", "two(1 < 2 && 3 > 2)|"], seen);
        Assert.Equal(1, host.Calls);
    }

    [Fact]
    public async Task WithAHost_NoscriptContentIsNotShown()
    {
        var host = new DelegateHost(_ => ValueTask.CompletedTask);
        var (_, document) = await LoadAsync("<noscript><p id=n>no script</p></noscript><p id=y>yes</p>", host);

        Assert.Null(document.GetElement("n"));
        Assert.NotNull(document.GetElement("y"));
    }

    [Fact]
    public async Task WhatTheHostBuilds_IsLaidOutAndPainted()
    {
        var host = new DelegateHost(document =>
        {
            var box = document.CreateElement("div");
            box.SetAttribute("id", "made");
            box.SetAttribute("style", "width: 100pt; height: 100pt; background: rgb(255, 0, 0)");
            document.Body!.AppendChild(box);
            return ValueTask.CompletedTask;
        });

        var (context, document) = await LoadAsync("<p>hi</p>", host);

        Assert.NotNull(document.GetElement("made"));
        var bounds = document.GetElementBounds("made")!.Value;
        Assert.Equal(100, bounds.Width, 1);
        using var canvas = await TestDocument.PaintAsync(context, document);
        var (r, _, _, _) = TestDocument.Pixel(canvas, (int)bounds.X + 50, (int)bounds.Y + 50);
        Assert.Equal(255, r);
    }

    [Fact]
    public async Task TheDocumentApi_NavigatesAndChangesTheTree()
    {
        var host = new DelegateHost(document =>
        {
            var list = document.GetElementById("list")!;
            Assert.Equal(["a", "b"], list.ChildElements.Select(e => e.TextContent));
            Assert.Equal("b", list.QuerySelector("li.second")!.TextContent);
            Assert.Equal(2, document.QuerySelectorAll("#list > li").Count);
            Assert.True(list.ChildElements[0].Matches("li:first-child"));
            Assert.Same(list, list.ChildElements[1].Closest("ul"));
            Assert.Same(list.ChildElements[0], list.ChildElements[1].PreviousSibling);

            list.InnerHtml = "<li>x</li><li class=\"new\">y &amp; z</li>";
            Assert.Equal("x", list.FirstChild!.TextContent);
            Assert.Equal("<li>x</li><li class=\"new\">y &amp; z</li>", list.InnerHtml);

            list.ChildElements[0].Remove();
            var item = list.ChildElements[0];
            item.TextContent = "<replaced>";
            Assert.Equal("<li class=\"new\">&lt;replaced&gt;</li>", list.InnerHtml);
            Assert.Equal("<ul id=\"list\"><li class=\"new\">&lt;replaced&gt;</li></ul>", list.OuterHtml);

            var copy = item.CloneNode(deep: true);
            Assert.Null(copy.Parent);
            list.AppendChild(copy);
            Assert.Equal(2, list.ChildElements.Count);
            Assert.Throws<InvalidOperationException>(() => list.AppendChild(list));
            Assert.True(document.Version > 0);
            return ValueTask.CompletedTask;
        });

        await LoadAsync("<ul id=list><li>a</li><li class=second>b</li></ul>", host);
    }

    [Fact]
    public async Task TheScriptedDocument_SurvivesAnotherLoad_WithoutRunningTheScriptsAgain()
    {
        var host = new DelegateHost(document =>
        {
            document.GetElementById("t")!.SetAttribute("data-run", "yes");
            document.Body!.AppendChild(document.CreateElement("hr"));
            return ValueTask.CompletedTask;
        });

        var (_, document) = await LoadAsync("<p id=t>text</p>", host);
        using var measuring = (await TestDocument.NewContextAsync()).CreateCanvas(1, 1);

        // A class change loads the document again; the scripted tree must be what is loaded, and the host must not be asked again.
        var element = document.GetElement("t")!;
        await document.AddClassAsync(element, "x", measuring);

        Assert.Equal("yes", document.GetAttribute(document.GetElement("t")!, "data-run"));
        Assert.Equal(1, host.Calls);
    }

    [Fact]
    public async Task TheScriptingMediaFeature_ReportsEnabledOnlyWithAHost()
    {
        const string css = "<style>#p { width: 10pt; height: 10pt; background: rgb(255, 0, 0) } @media (scripting) { #p { background: rgb(0, 0, 255) } }</style>";

        var (without, withoutDocument) = await LoadAsync("<div id=p></div>", null, css);
        var (with, withDocument) = await LoadAsync("<div id=p></div>", new DelegateHost(_ => ValueTask.CompletedTask), css);

        using var a = await TestDocument.PaintAsync(without, withoutDocument);
        using var b = await TestDocument.PaintAsync(with, withDocument);
        Assert.Equal(255, TestDocument.Pixel(a, 5, 5).R);
        Assert.Equal(255, TestDocument.Pixel(b, 5, 5).B);
    }

    [Fact]
    public async Task WithAHost_NoscriptIsNotDisplayed()
    {
        var host = new DelegateHost(_ => ValueTask.CompletedTask);
        var (context, document) = await LoadAsync("<noscript id=ns>hello hello hello</noscript><p id=y>yes</p>", host);

        using var canvas = await TestDocument.PaintAsync(context, document);
        var yes = document.GetElementBounds("y")!.Value;
        // The noscript text would push the paragraph down by a line; with it hidden the paragraph is the first thing on the page.
        var (_, baseline) = await LoadAsync("<p id=y>yes</p>", host);
        Assert.True(Math.Abs(yes.Y - baseline.GetElementBounds("y")!.Value.Y) < 0.5, $"the paragraph starts at {yes.Y}, not {baseline.GetElementBounds("y")!.Value.Y}");
    }
}
