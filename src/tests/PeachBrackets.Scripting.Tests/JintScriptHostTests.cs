using PeachDrawing;
using PeachDrawing.Core;

namespace PeachBrackets.Scripting.Tests;

/// <summary>JavaScript run by the Jint host, checked by what the document is afterwards: its tree, and what is laid out and painted.</summary>
public class JintScriptHostTests
{
    private sealed class Reporter
    {
        public CollectingLogger Logger { get; } = new();

        public List<string> Errors => Logger.Errors;

        public List<string> Console => Logger.Messages;

        public JintScriptHost Host(Action<JintScriptOptions>? configure = null)
        {
            var options = new JintScriptOptions { Logger = Logger };
            configure?.Invoke(options);
            return new JintScriptHost(options);
        }
    }

    private static async Task<(Reporter Reporter, RasterRenderContext Context, HtmlDocument Document)> RunAsync(
        string body, Action<JintScriptOptions>? configure = null, string head = "")
    {
        var reporter = new Reporter();
        var context0 = await TestDocument.NewContextAsync();
        var (context, document) = await TestDocument.LoadAsync(
            TestDocument.Wrap(body, head),
            new HtmlDocumentOptions { MediaType = MediaType.Screen, PageWidthPoints = 400, ScriptHost = reporter.Host(configure), MeasuringCanvasFactory = () => context0.CreateCanvas(1, 1) });
        return (reporter, context, document);
    }

    [Fact]
    public async Task AnInlineScript_ChangesTheDocumentBeforeItIsLaidOut()
    {
        var (reporter, context, document) = await RunAsync(
            "<div id=box></div><script>var b = document.getElementById('box'); b.style.width = '100pt'; b.style.height = '50pt'; b.style.background = 'rgb(255, 0, 0)';</script>");

        Assert.True(reporter.Errors.Count == 0, string.Join(" / ", reporter.Errors));
        var bounds = document.GetElementBounds("box")!.Value;
        Assert.Equal(100, bounds.Width, 1);
        Assert.Equal(50, bounds.Height, 1);
        using var canvas = await TestDocument.PaintAsync(context, document);
        Assert.Equal(255, TestDocument.Pixel(canvas, (int)bounds.X + 10, (int)bounds.Y + 10).R);
    }

    [Fact]
    public async Task Scripts_CanBuildElementsAndText()
    {
        var (reporter, _, document) = await RunAsync(@"<ul id=list></ul><script>
            for (var i = 0; i < 3; i++) {
                var li = document.createElement('li');
                li.id = 'item' + i;
                li.textContent = 'Item ' + i;
                li.className = 'row';
                document.getElementById('list').appendChild(li);
            }
            document.body.insertAdjacentHTML('beforeend', '<p id=\'tail\'>tail &amp; more</p>');
        </script>");

        Assert.True(reporter.Errors.Count == 0, string.Join(" / ", reporter.Errors));
        Assert.NotNull(document.GetElement("item0"));
        Assert.NotNull(document.GetElement("item2"));
        Assert.Equal("row", document.GetAttribute(document.GetElement("item1")!, "class"));
        Assert.NotNull(document.GetElement("tail"));
    }

    [Fact]
    public async Task Selectors_ClassList_AndAttributes_Work()
    {
        var (reporter, _, document) = await RunAsync(@"<div id=a class='x y'></div><div id=b></div><script>
            var a = document.querySelector('#a.x');
            a.classList.add('z'); a.classList.remove('x'); a.classList.toggle('y');
            a.setAttribute('data-n', a.classList.contains('z') ? 'yes' : 'no');
            var all = document.querySelectorAll('div');
            document.getElementById('b').setAttribute('data-count', String(all.length));
            document.getElementById('b').dataset.ignored = 1;
        </script>");

        Assert.True(reporter.Errors.Count == 0, string.Join(" / ", reporter.Errors));
        var a = document.GetElement("a")!;
        Assert.Equal("z", document.GetAttribute(a, "class"));
        Assert.Equal("yes", document.GetAttribute(a, "data-n"));
        Assert.Equal("2", document.GetAttribute(document.GetElement("b")!, "data-count"));
    }

    [Fact]
    public async Task Timers_AndAnimationFrames_RunOnAVirtualClock_UntilThePageSettles()
    {
        var (reporter, _, document) = await RunAsync(@"<div id=t></div><script>
            var order = [];
            setTimeout(function () { order.push('t100'); }, 100);
            setTimeout(function () { order.push('t10'); }, 10);
            requestAnimationFrame(function () { order.push('raf'); });
            var n = 0;
            var h = setInterval(function () { if (++n == 3) { clearInterval(h); order.push('iv'); } }, 20);
            Promise.resolve().then(function () { order.push('micro'); });
            setTimeout(function () { document.getElementById('t').setAttribute('data-order', order.join(',')); }, 500);
        </script>");

        Assert.True(reporter.Errors.Count == 0, string.Join(" / ", reporter.Errors));
        Assert.Equal("micro,t10,raf,iv,t100", document.GetAttribute(document.GetElement("t")!, "data-order"));
    }

    [Fact]
    public async Task LifecycleEvents_AndListeners_RunInOrder()
    {
        var (reporter, _, document) = await RunAsync(@"<div id=t></div><script>
            var log = [];
            document.addEventListener('DOMContentLoaded', function () { log.push('dcl:' + document.readyState); });
            window.addEventListener('load', function () { log.push('load:' + document.readyState); });
            var t = document.getElementById('t');
            t.addEventListener('click', function (e) { log.push('target'); });
            document.body.addEventListener('click', function (e) { log.push('bubble:' + (e.target === t) + ':' + (e.currentTarget === document.body)); }, false);
            document.body.addEventListener('click', function (e) { log.push('capture'); }, true);
            t.dispatchEvent(new Event('click', { bubbles: true }));
            var c = new CustomEvent('ping', { detail: 7 }); t.addEventListener('ping', function (e) { log.push('detail:' + e.detail); }); t.dispatchEvent(c);
            window.onload = function () { document.getElementById('t').setAttribute('data-log', log.join('|')); };
            document.readyState == 'loading' && log.push('loading');
        </script>");

        Assert.Empty(reporter.Errors.Select(e => e));
        Assert.Equal("capture|target|bubble:true:true|detail:7|loading|dcl:interactive|load:complete", document.GetAttribute(document.GetElement("t")!, "data-log"));
    }

    [Fact]
    public async Task AnAttributeHandler_OnTheBody_RunsAtLoad()
    {
        var reporter = new Reporter();
        var (_, document) = await TestDocument.LoadAsync(
            "<!DOCTYPE html><html><body onload=\"document.getElementById('t').setAttribute('data-ok', 'yes')\"><div id=t></div></body></html>",
            new HtmlDocumentOptions { ScriptHost = reporter.Host() });

        Assert.True(reporter.Errors.Count == 0, string.Join(" / ", reporter.Errors));
        Assert.Equal("yes", document.GetAttribute(document.GetElement("t")!, "data-ok"));
    }

    [Fact]
    public async Task AScriptThatThrows_IsReported_AndTheRestStillRuns()
    {
        var (reporter, _, document) = await RunAsync(@"<div id=t></div>
            <script>throw new Error('boom');</script>
            <script>this is not javascript</script>
            <script>document.getElementById('t').setAttribute('data-after', 'ran');</script>");

        Assert.Equal(2, reporter.Errors.Count);
        Assert.Contains("boom", reporter.Errors[0]);
        Assert.Equal("ran", document.GetAttribute(document.GetElement("t")!, "data-after"));
    }

    [Fact]
    public async Task AnInfiniteLoop_IsStoppedByTheTimeout()
    {
        var (reporter, _, document) = await RunAsync(
            "<div id=t></div><script>while (true) {}</script><script>document.getElementById('t').setAttribute('data-after', 'ran');</script>",
            o => o.ScriptTimeout = TimeSpan.FromMilliseconds(200));

        Assert.Single(reporter.Errors);
        Assert.Equal("ran", document.GetAttribute(document.GetElement("t")!, "data-after"));
    }

    [Fact]
    public async Task Console_GoesToTheSink()
    {
        var (reporter, _, _) = await RunAsync("<script>console.log('a', 1); console.error('b');</script>");

        Assert.Equal(["Information:a 1", "Error:b"], reporter.Console);
    }

    [Fact]
    public async Task AReftestWaitPage_IsCompleteWhenTheClassIsRemovedByATimer()
    {
        var reporter = new Reporter();
        var html = "<!DOCTYPE html><html class=reftest-wait><body><div id=t></div><script>setTimeout(function () { document.getElementById('t').style.width = '40pt'; document.documentElement.classList.remove('reftest-wait'); }, 50);</script></body></html>";
        var (_, document) = await TestDocument.LoadAsync(html, new HtmlDocumentOptions { ScriptHost = reporter.Host() });

        Assert.True(reporter.Errors.Count == 0, string.Join(" / ", reporter.Errors));
        Assert.Null(document.GetAttribute(document.GetElement("t")!, "class"));
        Assert.Contains("width: 40pt", document.GetAttribute(document.GetElement("t")!, "style"));
    }

    [Fact]
    public async Task ExternalScripts_AreLoadedThroughTheDocumentsLoader()
    {
        var requested = new List<string>();
        var context = await TestDocument.NewContextAsync();
        context.ResourceLoader = uri =>
        {
            requested.Add(uri.ToString());
            return ValueTask.FromResult<RNetworkResponse?>(uri.AbsolutePath.EndsWith("lib.js", StringComparison.Ordinal)
                ? new RNetworkResponse(new MemoryStream(System.Text.Encoding.UTF8.GetBytes("window.fromLib = 'yes';")), new() { ["Content-Type"] = ["text/javascript"] })
                : null);
        };

        var reporter = new Reporter();
        using var document = await HtmlDocument.LoadAsync(
            "<!DOCTYPE html><html><head><base href=\"http://test.invalid/dir/\"></head><body><div id=t></div><script src=\"lib.js\"></script>"
            + "<script>document.getElementById('t').setAttribute('data-lib', window.fromLib);</script></body></html>",
            context, new HtmlDocumentOptions { ScriptHost = reporter.Host() });

        Assert.True(reporter.Errors.Count == 0, string.Join(" / ", reporter.Errors));
        Assert.Contains("http://test.invalid/dir/lib.js", requested);
        Assert.Equal("yes", document.GetAttribute(document.GetElement("t")!, "data-lib"));
    }

    [Fact]
    public async Task LayoutReads_SeeTheDocumentAsTheScriptLeftItSoFar()
    {
        var (reporter, _, document) = await RunAsync(@"<style>#a { width: 120px; height: 30px; background: rgb(0,128,0) } #b { color: rgb(1, 2, 3) }</style><div id=a></div><p id=b>text</p><div id=out></div><script>
            var a = document.getElementById('a');
            var first = a.offsetWidth + 'x' + a.offsetHeight;
            a.style.width = '200px';
            var r = a.getBoundingClientRect();
            var cs = getComputedStyle(document.getElementById('b'));
            document.getElementById('out').setAttribute('data-r', first + '|' + Math.round(r.width) + '|' + cs.color + '|' + cs.display);
        </script>");

        Assert.True(reporter.Errors.Count == 0, string.Join(" / ", reporter.Errors));
        Assert.Equal("120x30|200|rgb(1, 2, 3)|block", document.GetAttribute(document.GetElement("out")!, "data-r"));
    }

    [Fact]
    public async Task InnerHtml_OfATablePart_ParsesItsCellsAndRows()
    {
        var (reporter, _, document) = await RunAsync(@"<table><tbody id=b></tbody></table><table><tr id=r></tr></table><script>
            document.getElementById('b').innerHTML = '<tr><td id=c1>one</td><td>two</td></tr>';
            document.getElementById('r').innerHTML = '<td id=c2>x</td>';
        </script>");

        Assert.True(reporter.Errors.Count == 0, string.Join(" / ", reporter.Errors));
        Assert.NotNull(document.GetElement("c1"));
        Assert.NotNull(document.GetElement("c2"));
    }

    [Fact]
    public async Task LayoutReads_AreRightWhenThePageHasNoscriptContentBeforeTheElement()
    {
        var (reporter, _, document) = await RunAsync(@"<noscript><p>fallback</p><p>more</p></noscript><div id=a style='width:90px;height:10px'></div><div id=out></div><script>
            document.getElementById('out').setAttribute('data-w', String(document.getElementById('a').getBoundingClientRect().width));
        </script>");

        Assert.True(reporter.Errors.Count == 0, string.Join(" / ", reporter.Errors));
        Assert.Equal("90", document.GetAttribute(document.GetElement("out")!, "data-w"));
    }

    [Fact]
    public async Task MicrotasksQueuedInsideATimerCallback_RunBeforeThePageSettles()
    {
        var (reporter, _, document) = await RunAsync(@"<div id=t></div><script>
            setTimeout(function () {
                queueMicrotask(function () { document.getElementById('t').setAttribute('data-a', 'queue'); });
                Promise.resolve().then(function () { document.getElementById('t').setAttribute('data-b', 'promise'); });
            }, 10);
        </script>");

        Assert.True(reporter.Errors.Count == 0, string.Join(" / ", reporter.Errors));
        var t = document.GetElement("t")!;
        Assert.Equal("queue", document.GetAttribute(t, "data-a"));
        Assert.Equal("promise", document.GetAttribute(t, "data-b"));
    }

    [Fact]
    public async Task BoundFunctions_AndInterfaces_BehaveLikeABrowsers()
    {
        var (reporter, _, document) = await RunAsync(@"<div id=d></div><iframe id=f></iframe><div id=out></div><script>
            var hits = 0;
            var bound = function (a, e) { hits += a; }.bind(null, 5);
            var d = document.getElementById('d');
            d.addEventListener('click', bound);
            d.dispatchEvent(new Event('click'));
            document.getElementById('out').setAttribute('data-r', [hits, d instanceof HTMLDivElement, d instanceof HTMLIFrameElement,
                document.getElementById('f') instanceof HTMLIFrameElement, d instanceof Element, d instanceof Node, d instanceof EventTarget,
                document instanceof Document].join(','));
        </script>");

        Assert.True(reporter.Errors.Count == 0, string.Join(" / ", reporter.Errors));
        Assert.Equal("5,true,false,true,true,true,true,true", document.GetAttribute(document.GetElement("out")!, "data-r"));
    }
}
