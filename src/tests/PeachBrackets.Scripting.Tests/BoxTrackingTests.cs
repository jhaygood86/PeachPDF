using PeachBrackets.ScriptHosting;

namespace PeachBrackets.Scripting.Tests;

/// <summary>The boxes of a laid-out document, read by a script (<c>getPrinceBoxes</c>) or by a host (<see cref="ScriptLayout.Pages"/>).</summary>
public class BoxTrackingTests
{
    private static async Task<(HtmlDocument Document, CollectingLogger Logger)> RunAsync(string html, params ScriptApi[] apis)
    {
        var logger = new CollectingLogger();
        var context = await TestDocument.NewContextAsync();
        var options = new HtmlDocumentOptions
        {
            MediaType = MediaType.Print,
            PageWidthPoints = 300,
            PageHeightPoints = 200,
            ScriptHost = new JintScriptHost(new JintScriptOptions { Logger = logger }),
            MeasuringCanvasFactory = () => context.CreateCanvas(1, 1),
        };
        options.SetMargins(0);
        foreach (var api in apis)
            options.ScriptApis.Add(api);

        var document = await HtmlDocument.LoadAsync(TestDocument.Wrap(html), context, options);
        using var measuring = context.CreateCanvas(1, 1);
        await document.LayoutAsync(measuring);
        return (document, logger);
    }

    private static IEnumerable<BoxReport> Flatten(BoxReport box) => box.Children.SelectMany(c => Flatten(c).Prepend(c));

    private const string Boxy = "<div id=a style='margin:10pt;padding:5pt;border:2pt solid #000;width:100pt;height:40pt'>hello world</div>";

    private static string AfterLayout(string script) => "<script>Prince.trackBoxes = true; Prince.registerPostLayoutFunc(function () {" + script + "});</script>";

    [Fact]
    public async Task AnElementsBox_HasItsPlaceSizeAndUsedEdges_WithTheOriginAtTheBottomLeft()
    {
        var (document, logger) = await RunAsync(Boxy + AfterLayout(@"
            var b = document.getElementById('a').getPrinceBoxes();
            var x = b[0];
            Log.info([b.length, x.type, x.pageNum, x.x, x.y, x.w, x.h].join('|'));
            Log.info([x.marginTop, x.marginRight, x.marginBottom, x.marginLeft, x.paddingTop, x.paddingLeft, x.borderTop, x.borderRight].join('|'));"));
        using (document)
        {
            // Content 100 x 40 plus padding 5 and border 2 on each side: 114 x 54, 10 in from the left and 10 down from the top of a 200pt page.
            Assert.Equal(["Information:1|BOX|1|10|136|114|54", "Information:10|10|10|10|5|5|2|2"], logger.Messages);
        }
    }

    [Fact]
    public async Task TheBoxTree_HasThePageAsItsRoot_AndTheElementAndTextInside()
    {
        var (document, logger) = await RunAsync(Boxy + AfterLayout(@"
            var a = document.getElementById('a');
            var box = a.getPrinceBoxes()[0];
            var chain = [], up = box;
            while (up) { chain.push(up.type); up = up.parent; }
            function texts(b) { return b.children.reduce(function (all, c) { return all.concat(c.type === 'TEXT' ? [c] : texts(c)); }, []); }
            var text = texts(box);
            Log.info(chain[0] + ' ... ' + chain[chain.length - 1] + ' ' + (chain.length > 2));
            Log.info(text.length + ':' + text[0].text + ':' + (box.element === a) + ':' + (box.parent.element !== a));"));
        using (document)
        {
            Assert.Equal(["Information:BOX ... BODY true", "Information:1:hello world:true:true"], logger.Messages);
        }
    }

    [Fact]
    public async Task AnElementThatBreaksAcrossPages_HasABoxOnEachPage()
    {
        var rows = string.Concat(Enumerable.Repeat("<p style='margin:0;height:50pt'>row</p>", 6));
        var (document, logger) = await RunAsync("<div id=t>" + rows + "</div>" + AfterLayout(@"
            var b = document.getElementById('t').getPrinceBoxes();
            Log.info(b.map(function (x) { return x.pageNum + ':' + x.h; }).join('|'));"));
        using (document)
        {
            Assert.Equal(["Information:1:200|2:100"], logger.Messages);
        }
    }

    [Fact]
    public async Task WithoutTrackBoxes_NoBoxesAreReturned()
    {
        var (document, logger) = await RunAsync(Boxy + "<script>Prince.registerPostLayoutFunc(function () { Log.info(document.getElementById('a').getPrinceBoxes().length); });</script>");
        using (document)
        {
            Assert.Equal(["Information:0"], logger.Messages);
        }
    }

    [Fact]
    public async Task GeneratedContentIsAPseudoBox_AndAnImageHasItsSource()
    {
        var img = "<img id=i width=20 height=10 src='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=='>";
        var (document, logger) = await RunAsync(
            "<style>#p::before { content: 'pre'; }</style><p id=p>x</p>" + img + AfterLayout(@"
            var p = document.getElementById('p').getPrinceBoxes()[0];
            var pseudos = p.children.filter(function (c) { return c.pseudo !== null; }).map(function (c) { return c.pseudo; });
            var i = document.getElementById('i').getPrinceBoxes()[0];
            Log.info(pseudos.join(',') + ' ' + i.type + ' ' + i.w + 'x' + i.h + ' ' + (i.src.indexOf('data:image/png') === 0));"));
        using (document)
        {
            Assert.Equal(["Information:before IMAGE 15x7.5 true"], logger.Messages);
        }
    }

    [Fact]
    public async Task AHostReadsTheBoxesWithoutAnyScript()
    {
        var api = new ScriptApi("Host");
        ScriptDocument? script = null;
        IReadOnlyList<BoxReport>? pages = null;
        IReadOnlyList<BoxReport>? mine = null;
        api.ScriptsStarting += d => script = d;
        api.LayoutFinished += layout =>
        {
            pages = layout.Pages;
            mine = layout.GetBoxes(script!.GetElementById("a")!);
        };

        var (document, _) = await RunAsync(Boxy, api);
        using (document)
        {
            Assert.NotNull(pages);
            var page = Assert.Single(pages);
            Assert.Equal((BoxReportKind.Page, 1, 300d, 200d), (page.Kind, page.PageNumber, page.PageWidth, page.PageHeight));

            var box = Assert.Single(mine!);
            Assert.Equal(10, box.X, 3);
            Assert.Equal(10, box.Y, 3);
            Assert.Equal(114, box.Width, 3);
            Assert.Equal(54, box.Height, 3);
            Assert.Same(script!.GetElementById("a"), box.Element);
            Assert.Equal(new BoxEdges(10, 10, 10, 10), box.Margin);
            Assert.Equal(new BoxEdges(2, 2, 2, 2), box.Border);
            Assert.NotNull(box.Parent);
            Assert.Contains(Flatten(box), c => c is { Kind: BoxReportKind.Text, Text: "hello world" });
            Assert.DoesNotContain(box.Children, c => c.Element is not null);
        }
    }
}
