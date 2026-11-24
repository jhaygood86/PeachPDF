using PeachDrawing;
using PeachDrawing.Core;

namespace PeachBrackets.Scripting.Tests;

/// <summary>
/// Runs real React 18 (the UMD builds, fetched once into <c>~/.peachbrackets-react</c> and never committed) in the Jint host: a framework that exercises a
/// lot of the DOM at once. The tests skip when the files cannot be fetched (no network).
/// </summary>
public class ReactSmokeTests
{
    private const string Version = "18.3.1";

    private static readonly Lazy<Task<Dictionary<string, byte[]>?>> Files = new(FetchAsync);

    private static async Task<Dictionary<string, byte[]>?> FetchAsync()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".peachbrackets-react", Version);
        Directory.CreateDirectory(directory);
        var files = new Dictionary<string, byte[]>();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        foreach (var (name, url) in new[]
        {
            ("react.js", $"https://unpkg.com/react@{Version}/umd/react.production.min.js"),
            ("react-dom.js", $"https://unpkg.com/react-dom@{Version}/umd/react-dom.production.min.js"),
        })
        {
            var path = Path.Combine(directory, name);
            try
            {
                if (!File.Exists(path))
                    await File.WriteAllBytesAsync(path + ".tmp", await http.GetByteArrayAsync(url)).ConfigureAwait(false);
                if (File.Exists(path + ".tmp"))
                    File.Move(path + ".tmp", path, overwrite: true);

                files[name] = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                return null;
            }
        }

        return files;
    }

    private static async Task<(List<string> Errors, HtmlDocument Document, RasterRenderContext Context)> RunAsync(string body)
    {
        var files = await Files.Value;
        Assert.SkipUnless(files is not null, "React could not be fetched (no network).");

        var context = await TestDocument.NewContextAsync();
        context.ResourceLoader = uri => ValueTask.FromResult<RNetworkResponse?>(
            uri.Host == "react.test" && files!.TryGetValue(uri.AbsolutePath.TrimStart('/'), out var bytes)
                ? new RNetworkResponse(new MemoryStream(bytes), new() { ["Content-Type"] = ["text/javascript"] })
                : null);

        var errors = new List<string>();
        var host = new JintScriptHost(new JintScriptOptions { ScriptError = errors.Add, ScriptTimeout = TimeSpan.FromSeconds(20) });
        var html = "<!DOCTYPE html><html><head><base href=\"http://react.test/\"><style>* { font-family: " + TestDocument.Family + " }</style></head><body>"
            + "<div id=root></div><script src=\"react.js\"></script><script src=\"react-dom.js\"></script>" + body + "</body></html>";

        var document = await HtmlDocument.LoadAsync(html, context, new HtmlDocumentOptions
        {
            MediaType = MediaType.Screen,
            PageWidthPoints = 400,
            ScriptHost = host,
            MeasuringCanvasFactory = () => context.CreateCanvas(1, 1),
        });

        using (var measuring = context.CreateCanvas(1, 1))
            await document.LayoutAsync(measuring);

        return (errors, document, context);
    }

    [Fact]
    public async Task ReactRendersAComponentTree()
    {
        var (errors, document, _) = await RunAsync(@"<script>
            var h = React.createElement;
            function Item(props) { return h('li', { id: 'item-' + props.n, className: props.n % 2 ? 'odd' : 'even' }, 'Item ', props.n); }
            function App() {
                var items = [1, 2, 3, 4].map(function (n) { return h(Item, { key: n, n: n }); });
                return h('div', { id: 'app', style: { width: '120px', height: '30px', background: 'rgb(255, 0, 0)' } },
                    h('h1', null, 'Hello React'), h('ul', null, items));
            }
            ReactDOM.createRoot(document.getElementById('root')).render(h(App));
        </script>");

        Assert.True(errors.Count == 0, string.Join(" / ", errors));
        Assert.NotNull(document.GetElement("app"));
        for (var n = 1; n <= 4; n++)
            Assert.NotNull(document.GetElement("item-" + n));

        Assert.Equal("odd", document.GetAttribute(document.GetElement("item-3")!, "class"));
        // Points: 120 CSS px is 90 pt.
        Assert.Equal(90, document.GetElementBounds("app")!.Value.Width, 1);
    }

    [Fact]
    public async Task ReactStateEffectsAndEventsWork()
    {
        var (errors, document, _) = await RunAsync(@"<script>
            var h = React.createElement;
            function Counter() {
                var state = React.useState(0), count = state[0], setCount = state[1];
                React.useEffect(function () {
                    var t = setTimeout(function () { setCount(function (c) { return c + 10; }); }, 50);
                    return function () { clearTimeout(t); };
                }, []);
                return h('button', { id: 'btn', 'data-count': String(count), onClick: function () { setCount(count + 1); } }, 'Count ' + count);
            }
            ReactDOM.createRoot(document.getElementById('root')).render(h(Counter));
            // A click goes through React's delegated listener on the root, as a browser's would.
            setTimeout(function () {
                var btn = document.getElementById('btn');
                btn.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
            }, 200);
        </script>");

        Assert.True(errors.Count == 0, string.Join(" / ", errors));
        // The effect's timer added 10 and the click added 1.
        Assert.Equal("11", document.GetAttribute(document.GetElement("btn")!, "data-count"));
    }
}
