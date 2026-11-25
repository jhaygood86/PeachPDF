using PeachBrackets.ScriptHosting;

namespace PeachBrackets.Scripting.Tests;

/// <summary>The smaller DOM pieces: event details, relaxed names, pseudo-attributes, token list iteration, Observable and the document's encoding.</summary>
public class LongTailTests
{
    private static async Task<List<string>> LogAsync(string script, string body = "")
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
        using var document = await HtmlDocument.LoadAsync(TestDocument.Wrap(body + "<script>" + script + "</script>"), context, options);
        Assert.True(logger.Errors.Count == 0, string.Join(" / ", logger.Errors));
        return logger.Texts;
    }

    [Fact]
    public async Task Events_HaveInterfaceMembers_PassiveListeners_AndSignals()
    {
        var log = await LogAsync("""
            var m = new MouseEvent('click', { clientX: '5', ctrlKey: 1 });
            Log.info([m.clientX, m.ctrlKey, m.button, String(m.relatedTarget), m instanceof UIEvent, String(new UIEvent('x').view), new KeyboardEvent('k', { key: 'a' }).key].join('|'));
            Log.info(document.createEvent('DragEvent') instanceof MouseEvent);
            var seen = [];
            document.addEventListener('touchstart', function (e) { e.preventDefault(); seen.push(e.defaultPrevented); });
            var plain = document.createElement('div');
            plain.addEventListener('touchstart', function (e) { e.preventDefault(); seen.push(e.defaultPrevented); });
            document.dispatchEvent(new Event('touchstart', { cancelable: true }));
            plain.dispatchEvent(new Event('touchstart', { cancelable: true }));
            Log.info(seen.join());
            var c = new AbortController(), n = 0;
            plain.addEventListener('ping', function () { n++; }, { signal: c.signal });
            plain.dispatchEvent(new Event('ping')); c.abort(); plain.dispatchEvent(new Event('ping'));
            Log.info(n);
            """);

        Assert.Equal(["5|true|0|null|true|null|a", "true", "false,true", "1"], log);
    }

    [Fact]
    public async Task Names_FollowTheRelaxedRules_AndDocumentsHaveTheirOwnImplementation()
    {
        var log = await LogAsync("""
            var out = [];
            for (var name of ['f}oo', '̀foo', ':a']) { try { document.createElement(name); out.push('ok'); } catch (e) { out.push(e.name); } }
            for (var name of ['}foo', '1a', 'a>']) { try { document.createElement(name); out.push('ok'); } catch (e) { out.push(e.name); } }
            Log.info(out.join());
            Log.info([document.createElementNS('urn:x', 'a:b:c').localName, document.createElementNS(null, 'f}oo').localName].join());
            var other = document.implementation.createHTMLDocument('t');
            var doctype = other.implementation.createDocumentType('~', '', '');
            Log.info([doctype.ownerDocument === other, other.URL, document.implementation.createDocument(null, 'r').compatMode].join('|'));
            """);

        Assert.Equal(["ok,ok,ok,InvalidCharacterError,InvalidCharacterError,InvalidCharacterError", "b:c,f}oo", "true|about:blank|CSS1Compat"], log);
    }

    [Fact]
    public async Task ProcessingInstruction_ReadsAndWritesPseudoAttributes()
    {
        var log = await LogAsync("""
            var doc = new DOMParser().parseFromString('<r/>', 'application/xml');
            var pi = doc.createProcessingInstruction('t', 'a="b" x="yy"');
            Log.info([pi.hasAttributes(), pi.getAttributeNames().join(), pi.getAttribute('x'), String(pi.getAttribute('X'))].join('|'));
            pi.setAttribute('x', 'z<'); pi.setAttribute('n', '1'); Log.info(pi.data);
            pi.removeAttribute('a'); Log.info(pi.data + ' ' + pi.toggleAttribute('n') + ' ' + pi.toggleAttribute('q'));
            Log.info(doc.createProcessingInstruction('t', 'a=b').hasAttributes() + ' ' + doc.createProcessingInstruction('t', 'a="1" a="2"').hasAttributes());
            """);

        Assert.Equal(["true|a,x|yy|null", "a=\"b\" x=\"z&lt;\" n=\"1\"", "x=\"z&lt;\" n=\"1\" false true", "false false"], log);
    }

    [Fact]
    public async Task TokenLists_Iterate()
    {
        var log = await LogAsync("""
            var d = document.createElement('div'); d.className = 'a b c';
            var seen = [];
            d.classList.forEach(function (t, i, list) { seen.push(t + i + (list === d.classList)); });
            Log.info(seen.join() + ' ' + [...d.classList].join('') + ' ' + Array.from(d.classList.entries()).join(';') + ' ' + (d.classList[Symbol.iterator] === d.classList.values));
            """);

        Assert.Equal(["a0true,b1true,c2true abc 0,a;1,b;2,c true"], log);
    }

    [Fact]
    public async Task Observable_Operators_AndWhen()
    {
        var log = await LogAsync("""
            (async function () {
                var a = await Observable.from([1, 2, 3, 4, 5]).filter(function (x) { return x % 2; }).map(function (x) { return x * 10; }).toArray();
                Log.info(a.join());
                Log.info((await Observable.from([1, 2, 3]).flatMap(function (x) { return [x, -x]; }).take(5).toArray()).join());
                Log.info(await Observable.from([1, 2, 3]).reduce(function (s, x) { return s + x; }, 0) + ' ' + await Observable.from([5, 6]).first() + ' ' + await Observable.from([5, 6]).last());
                var order = [];
                var o = new Observable(function (s) { s.addTeardown(function () { order.push('teardown'); }); s.next(1); s.complete(); s.next(2); });
                o.subscribe({ next: function (v) { order.push('next' + v); }, complete: function () { order.push('complete'); } });
                Log.info(order.join());
                var button = document.createElement('button'), clicks = [];
                var sub = new AbortController();
                button.when('click').subscribe(function (e) { clicks.push(e.type); }, { signal: sub.signal });
                button.dispatchEvent(new Event('click')); sub.abort(); button.dispatchEvent(new Event('click'));
                Log.info(clicks.join());
                try { await Observable.from([]).first(); } catch (e) { Log.info(e instanceof RangeError); }
            })();
            """);

        Assert.Equal(["10,30,50", "1,-1,2,-2,3", "6 5 6", "next1,teardown,complete", "click", "true"], log);
    }

    [Fact]
    public async Task Document_ReportsItsDeclaredEncoding_AndInsertedFramesLoad()
    {
        var log = await LogAsync("""
            var frame = document.createElement('iframe');
            frame.onload = function () { Log.info('loaded ' + frame.contentDocument.characterSet); };
            document.body.appendChild(frame);
            Log.info(document.characterSet + ' ' + document.charset + ' ' + document.inputEncoding);
            """, "<meta charset='LATIN1'>");

        Assert.Equal(["windows-1252 windows-1252 windows-1252", "loaded UTF-8"], log);
    }
}
