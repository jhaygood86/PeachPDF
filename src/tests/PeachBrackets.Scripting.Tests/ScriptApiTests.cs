using PeachBrackets.ScriptHosting;

namespace PeachBrackets.Scripting.Tests;

/// <summary>Objects a host adds to the scripts through <see cref="ScriptApi"/>, with no reference to the script engine.</summary>
public class ScriptApiTests
{
    private static async Task<(HtmlDocument Document, CollectingLogger Logger)> RunAsync(string body, params ScriptApi[] apis)
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
        foreach (var api in apis)
            options.ScriptApis.Add(api);

        var document = await HtmlDocument.LoadAsync(TestDocument.Wrap(body), context, options);
        using var measuring = context.CreateCanvas(1, 1);
        await document.LayoutAsync(measuring);
        return (document, logger);
    }

    [Fact]
    public async Task APropertyIsReadAndWrittenByTheScript()
    {
        var stored = "start";
        var api = new ScriptApi("Settings")
            .AddProperty("title", () => stored, value => stored = value.AsString() ?? string.Empty)
            .AddProperty("fixed", () => 42)
            .AddValue("label", "constant");

        var (document, logger) = await RunAsync("<script>Log.info(Settings.title); Settings.title = 'changed'; Settings.fixed = 7; Log.info(Settings.title + Settings.fixed + Settings.label);</script>", api);
        using (document)
        {
            Assert.Equal(["Information:start", "Information:changed42constant"], logger.Messages);
            Assert.Equal("changed", stored);
            Assert.Empty(logger.Errors);
        }
    }

    [Fact]
    public async Task AMethodGetsTheScriptsArgumentsAndReturnsAValue()
    {
        IReadOnlyList<ScriptValue>? seen = null;
        var api = new ScriptApi("Host").AddMethod("describe", args =>
        {
            seen = args;
            return ScriptValue.From(new Dictionary<string, ScriptValue> { ["count"] = args.Count, ["items"] = ScriptValue.From(new ScriptValue[] { 1, "two", true, ScriptValue.Null }) });
        });

        var (document, logger) = await RunAsync(
            "<script>var r = Host.describe(1, 'a', true, null, undefined, [1, 2], { x: 5 }); Log.info(r.count + ':' + r.items.join('|') + ':' + Array.isArray(r.items));</script>", api);
        using (document)
        {
            Assert.Equal(["Information:7:1|two|true|:true"], logger.Messages);
            Assert.NotNull(seen);
            Assert.Equal(
                [ScriptValueKind.Number, ScriptValueKind.String, ScriptValueKind.Boolean, ScriptValueKind.Null, ScriptValueKind.Undefined, ScriptValueKind.Array, ScriptValueKind.Object],
                seen.Select(v => v.Kind));
            Assert.Equal(2, seen[5].AsArray().Count);
            Assert.Equal(5, seen[6].AsObject()["x"].AsNumber());
        }
    }

    [Fact]
    public async Task AFunctionTheScriptPassesCanBeCalledByTheHost()
    {
        ScriptCallback? kept = null;
        var api = new ScriptApi("Host").AddMethod("onReady", args =>
        {
            kept = args[0].AsCallback();
            return ScriptValue.Undefined;
        });

        var (document, logger) = await RunAsync("<script>Host.onReady(function (a, b) { Log.info('called ' + a + b); return a * b; });</script>", api);
        using (document)
        {
            Assert.NotNull(kept);
            Assert.Equal(12, kept.Invoke(3, 4).AsNumber());
            Assert.Equal(["Information:called 34"], logger.Messages);
        }
    }

    [Fact]
    public async Task AHostFunctionCanBeCalledByTheScript_AndANodeCrossesBothWays()
    {
        ScriptDocument? script = null;
        var api = new ScriptApi("Host")
            .AddMethod("twice", _ => ScriptValue.From(new ScriptCallback(args => args[0].AsNumber() * 2)))
            .AddMethod("tag", args => args[0].AsNode()!.TagName)
            .AddMethod("body", _ => ScriptValue.From(script!.Body!));
        api.ScriptsStarting += d => script = d;

        var (document, logger) = await RunAsync(
            "<p id=p></p><script>var f = Host.twice(); Log.info(f(21) + ' ' + Host.tag(document.getElementById('p')) + ' ' + (Host.body() === document.body));</script>", api);
        using (document)
        {
            Assert.Equal(["Information:42 p true"], logger.Messages);
        }
    }

    [Fact]
    public async Task AnApiNamedLikeAnExistingGlobal_AddsToItInsteadOfReplacingIt()
    {
        var failed = false;
        var api = new ScriptApi("Prince").AddProperty("failStatus", () => failed, v => failed = v.AsBoolean());

        var (document, logger) = await RunAsync(
            "<script>Prince.failStatus = true; Log.info(typeof Prince.registerPostLayoutFunc + ' ' + Prince.failStatus + ' ' + typeof Prince.pageCount);</script>", api);
        using (document)
        {
            Assert.True(failed);
            Assert.Equal(["Information:function true number"], logger.Messages);
        }
    }

    [Fact]
    public async Task AMethodThatThrows_IsAnErrorTheScriptCanCatch()
    {
        var api = new ScriptApi("Host").AddMethod("fail", _ => throw new InvalidOperationException("no good"));

        var (document, logger) = await RunAsync(
            "<script>try { Host.fail(); } catch (e) { Log.info('caught ' + e.message + ' ' + (e instanceof Error)); } Host.fail(); Log.info('not reached');</script>", api);
        using (document)
        {
            Assert.Equal(["Information:caught no good true"], logger.Messages);
            Assert.Contains(logger.Errors, e => e.Contains("no good", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task AnEventIsDeliveredToListenersAndTheOnHandler()
    {
        var api = new ScriptApi("Host").AddEvent("tick");
        api.AddMethod("go", args =>
        {
            api.Raise("tick", args[0]);
            return ScriptValue.Undefined;
        });

        var (document, logger) = await RunAsync(
            "<script>Host.addEventListener('tick', function (e) { Log.info('listener ' + e.type + ' ' + e.detail.n); }); Host.ontick = function (e) { Log.info('handler ' + e.detail.n); }; Host.go({ n: 5 });</script>", api);
        using (document)
        {
            Assert.Equal(["Information:listener tick 5", "Information:handler 5"], logger.Messages);
        }
    }

    [Fact]
    public async Task TheApiIsToldWhenTheScriptsHaveRunAndHowTheLayoutCameOut()
    {
        var told = new List<string>();
        var api = new ScriptApi("Host");
        api.ScriptsStarting += d => told.Add("starting " + d.Apis.Count);
        api.ScriptsCompleted += d => told.Add("scripts " + d.Apis.Count);
        api.LayoutCompleted += l => told.Add("layout " + l.Pass + " pages " + l.PageCount);
        api.LayoutFinished += l => told.Add("finished pages " + l.PageCount);
        api.Closed += () => told.Add("closed");

        var (document, _) = await RunAsync("<p>x</p><script></script>", api);
        document.Dispose();

        Assert.Equal(["starting 1", "scripts 1", "layout 1 pages 1", "finished pages 1", "closed"], told);
    }

    [Fact]
    public async Task AnApiThatObservesNothing_ReleasesTheDocumentAfterTheScripts_AndStillClosesOnce()
    {
        var closed = 0;
        var api = new ScriptApi("Host");
        api.Closed += () => closed++;

        var (document, _) = await RunAsync("<script>Log.info('hi');</script>", api);
        document.Dispose();
        document.Dispose();

        Assert.Equal(1, closed);
    }

    [Fact]
    public async Task APostLayoutChangeAnApiMakes_IsLaidOutAgain()
    {
        var api = new ScriptApi("Host");
        ScriptDocument? script = null;
        var done = false;
        api.ScriptsStarting += d => script = d;
        api.LayoutCompleted += _ =>
        {
            if (done)
                return;

            done = true;
            var more = script!.CreateElement("div");
            more.InnerHtml = string.Concat(Enumerable.Repeat("<p style='margin:0;height:50pt'>x</p>", 6));
            script.Body!.AppendChild(more);
        };

        var (document, _) = await RunAsync("<p style='margin:0;height:50pt'>one</p>", api);
        using (document)
        {
            Assert.True(document.Pages.Count > 1, "pages: " + document.Pages.Count);
        }
    }

    [Fact]
    public void Members_AreValidated()
    {
        Assert.Throws<ArgumentException>(() => new ScriptApi("not valid"));
        Assert.Throws<ArgumentException>(() => new ScriptApi("1x"));
        var api = new ScriptApi("Ok").AddValue("a", 1);
        Assert.Throws<ArgumentException>(() => api.AddMethod("a", _ => ScriptValue.Undefined));
        Assert.Throws<InvalidOperationException>(() => api.Raise("nope"));
        Assert.Throws<InvalidOperationException>(() => api.Call("missing"));
        Assert.Equal(ScriptValue.Undefined, api.GetProperty("missing"));
        Assert.False(api.IsWritable("a"));
        api.SetProperty("a", 2);
        Assert.Equal(1, api.GetProperty("a").AsNumber());
    }

    [Fact]
    public void ScriptValue_ConvertsAndCompares()
    {
        Assert.Equal(ScriptValueKind.Undefined, default(ScriptValue).Kind);
        Assert.True(ScriptValue.Null.IsNullish);
        Assert.Equal("3", ((ScriptValue)3).AsString());
        Assert.Equal("true", ((ScriptValue)true).AsString());
        Assert.Equal(7.5, ((ScriptValue)"7.5").AsNumber());
        Assert.True(double.IsNaN(((ScriptValue)"x").AsNumber()));
        Assert.True(((ScriptValue)"x").AsBoolean());
        Assert.False(((ScriptValue)"").AsBoolean());
        Assert.False(((ScriptValue)0).AsBoolean());
        Assert.Equal(ScriptValue.Null, (ScriptValue)(string?)null);
        Assert.Equal((ScriptValue)"a", (ScriptValue)"a");
        Assert.NotEqual((ScriptValue)1, (ScriptValue)"1");
        Assert.True((ScriptValue)1 == (ScriptValue)1.0);
        Assert.True((ScriptValue)1 != (ScriptValue)2);
        Assert.Equal("1,two", ScriptValue.From(new ScriptValue[] { 1, "two" }).AsString());
        Assert.Equal("[1, two]", ScriptValue.From(new ScriptValue[] { 1, "two" }).ToString());
        Assert.Empty(((ScriptValue)1).AsArray());
        Assert.Empty(((ScriptValue)1).AsObject());
        Assert.Null(((ScriptValue)1).AsCallback());
        Assert.Null(((ScriptValue)1).AsNode());
        Assert.Equal("undefined", ScriptValue.Undefined.ToString());
        Assert.Equal("null", ScriptValue.Null.ToString());
        Assert.Equal("{object}", ScriptValue.From(new Dictionary<string, ScriptValue>()).ToString());
        Assert.Equal("{function}", ScriptValue.From(new ScriptCallback(_ => 1)).ToString());
        Assert.NotEqual(0, ((ScriptValue)"a").GetHashCode() + 1);
        Assert.False(((ScriptValue)1).Equals((object?)"1"));
    }
}
