using PeachBrackets.ScriptHosting;

namespace PeachBrackets.Scripting.Tests;

/// <summary>XMLSerializer, outerHTML and insertAdjacentHTML, the HTML*Unsafe parsing methods and the streaming ones: what a script sees.</summary>
public class ParsingAndSerializationTests
{
    private static async Task<List<string>> LogAsync(string script)
    {
        var logger = new CollectingLogger();
        var context = await TestDocument.NewContextAsync();
        var options = new HtmlDocumentOptions
        {
            MediaType = MediaType.Screen,
            PageWidthPoints = 400,
            ScriptHost = new JintScriptHost(new JintScriptOptions { Logger = logger }),
            MeasuringCanvasFactory = () => context.CreateCanvas(1, 1),
        };
        using var document = await HtmlDocument.LoadAsync(TestDocument.Wrap("<div id='t'></div><script>" + script + "</script>"), context, options);
        Assert.True(logger.Errors.Count == 0, string.Join(" / ", logger.Errors));
        return logger.Texts;
    }

    [Fact]
    public async Task XMLSerializer_WritesNamespacesPrefixesAndEscapes()
    {
        var log = await LogAsync("""
            var s = new XMLSerializer();
            var doc = new DOMParser().parseFromString('<root xmlns="urn:a" xmlns:p="urn:p"><p:child p:attr="a&amp;b &lt;"><![CDATA[x]]></p:child><!--c--><?pi data?><empty/></root>', 'text/xml');
            Log.info(s.serializeToString(doc));
            var el = new Document().createElement('r');
            el.setAttributeNS('urn:q', 'q:name', 'v\n');
            Log.info(s.serializeToString(el));
            Log.info(s.serializeToString(document.createElement('br')));
            Log.info(s.serializeToString(document.createTextNode('a<b&c')));
            try { s.serializeToString({}); } catch (e) { Log.info(e instanceof TypeError); }
            """);

        Assert.Equal(
        [
            "<root xmlns=\"urn:a\" xmlns:p=\"urn:p\"><p:child p:attr=\"a&amp;b &lt;\">x</p:child><!--c--><?pi data?><empty/></root>",
            "<r xmlns:ns1=\"urn:q\" ns1:name=\"v&#10;\"/>",
            "<br xmlns=\"http://www.w3.org/1999/xhtml\" />",
            "a&lt;b&amp;c",
            "true",
        ],
            log);
    }

    [Fact]
    public async Task ParsedXmlDocuments_AreDocuments_WithNoLocation_AndPlainElementsHaveNoNamespace()
    {
        var log = await LogAsync("""
            var x = new DOMParser().parseFromString('<a/>', 'text/xml');
            Log.info([x instanceof XMLDocument, x instanceof Document, String(x.location), String(x.documentElement.namespaceURI), String(x.createElement('q').namespaceURI)].join('|'));
            Log.info(document.implementation.createDocument(null, 'z') instanceof XMLDocument);
            """);

        Assert.Equal(["false|true|null|null|null", "true"], log);
    }

    [Fact]
    public async Task OuterHtml_CanBeSet_AndInsertAdjacentHtmlChecksTheParent()
    {
        var log = await LogAsync("""
            var t = document.getElementById('t');
            t.outerHTML = '<p id="n">a</p><b>b</b>';
            Log.info(document.body.innerHTML.indexOf('<p id="n">a</p><b>b</b>') >= 0);
            var loose = document.createElement('div');
            try { loose.insertAdjacentHTML('beforebegin', '<i></i>'); } catch (e) { Log.info(e.name); }
            try { document.documentElement.insertAdjacentHTML('afterend', '<i></i>'); } catch (e) { Log.info(e.name); }
            try { document.documentElement.outerHTML = '<x></x>'; } catch (e) { Log.info(e.name); }
            var d = document.createElement('div'); d.innerHTML = 'x';
            d.innerHTML = undefined; Log.info(d.innerHTML);
            d.innerHTML = null; Log.info('[' + d.innerHTML + ']');
            """);

        Assert.Equal(["true", "NoModificationAllowedError", "NoModificationAllowedError", "NoModificationAllowedError", "undefined", "[]"], log);
    }

    [Fact]
    public async Task UnsafeParsingMethods_PutMarkupInPlace()
    {
        var log = await LogAsync("""
            var host = document.createElement('div');
            host.innerHTML = '<b id="m"></b>';
            var m = host.firstChild;
            m.beforeHTMLUnsafe('<i>1</i>'); m.afterHTMLUnsafe('<u>2</u>');
            host.appendHTMLUnsafe('<s>3</s>'); host.prependHTMLUnsafe('<q>0</q>');
            Log.info(host.innerHTML);
            m.replaceWithHTMLUnsafe('<em>x</em>');
            Log.info(host.innerHTML);
            host.setHTMLUnsafe('<p>fresh</p>');
            Log.info(host.innerHTML);
            Log.info(Document.parseHTMLUnsafe('<title>T</title><p>q</p>').title);
            """);

        Assert.Equal(["<q>0</q><i>1</i><b id=\"m\"></b><u>2</u><s>3</s>", "<q>0</q><i>1</i><em>x</em><u>2</u><s>3</s>", "<p>fresh</p>", "T"], log);
    }

    [Fact]
    public async Task StreamHtmlUnsafe_ParsesWhatIsWritten_AndClearsFirst()
    {
        var log = await LogAsync("""
            (async function () {
                var host = document.createElement('div');
                host.innerHTML = '<span>old</span>';
                var writable = host.streamHTMLUnsafe();
                Log.info(writable instanceof WritableStream);
                Log.info('[' + host.innerHTML + ']');
                var w = writable.getWriter();
                await w.write('<b>a');
                await w.write('b</b>c');
                Log.info(host.innerHTML);
                await w.close();
                var after = document.createElement('div');
                after.innerHTML = '<i id="r">r</i>';
                var w2 = after.firstChild.streamAppendHTMLUnsafe().getWriter();
                await w2.write('+');
                await w2.close();
                Log.info(after.innerHTML);
            })();
            """);

        Assert.Equal(["true", "[]", "<b>ab</b>c", "<i id=\"r\">r+</i>"], log);
    }

    [Fact]
    public async Task MathMLElements_AreNodesAScriptCanReach_WithTheirTextAndEndTags()
    {
        var log = await LogAsync("""
            var t = document.getElementById('t');
            t.innerHTML = '<math><mspace id="s" width="1em"/><mfrac id="f"><mi>x</mi><mn>2</mn></mfrac></math>';
            Log.info([document.getElementById('s') !== null, document.getElementById('f').children.length, document.getElementById('f').textContent].join('|'));
            Log.info(t.innerHTML);
            var parsed = new DOMParser().parseFromString('<math><mi id="m">y</mi></math>', 'text/html');
            Log.info(parsed.getElementById('m').textContent);
            """);

        Assert.Equal(
        [
            "true|2|x2",
            "<math><mspace id=\"s\" width=\"1em\"></mspace><mfrac id=\"f\"><mi>x</mi><mn>2</mn></mfrac></math>",
            "y",
        ],
            log);
    }

    [Fact]
    public async Task SelfClosingForeignElements_AreSerializedWithAnEndTag()
    {
        var log = await LogAsync("""
            var t = document.getElementById('t');
            t.innerHTML = '<svg><path d="M0 0"/><rect/></svg><br>';
            Log.info(t.innerHTML);
            """);

        Assert.Equal(["<svg><path d=\"M0 0\"></path><rect></rect></svg><br>"], log);
    }

    [Fact]
    public async Task DocumentFonts_IsAnIterableSetThatHasAlreadyLoaded()
    {
        var log = await LogAsync("""
            Log.info([[...document.fonts].length, document.fonts.size, document.fonts.status, document.fonts === document.fonts, document.fonts.check('12px x')].join('|'));
            for (const face of document.fonts) Log.info('never');
            document.fonts.ready.then(() => Log.info('ready'));
            Promise.allSettled([...document.fonts].map(f => f.load())).then(r => Log.info('settled ' + r.length));
            """);

        Assert.Equal(["0|0|loaded|true|true", "ready", "settled 0"], log);
    }
}
