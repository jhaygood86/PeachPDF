namespace PeachBrackets.Scripting.Tests;

/// <summary>The parts of the DOM standard the web-platform-tests of /dom check: DOMException, events, lists, character data, tokens, names, traversal.</summary>
public class DomStandardTests
{
    // Runs the script in a document and returns what it logged, each console call as its text; a script error fails the test.
    private static async Task<List<string>> LogAsync(string script, string body = "")
    {
        var errors = new List<string>();
        var lines = new List<string>();
        var host = new JintScriptHost(new JintScriptOptions { ScriptError = errors.Add, Console = (_, text) => lines.Add(text) });
        var context = await TestDocument.NewContextAsync();
        using var document = await HtmlDocument.LoadAsync(
            TestDocument.Wrap(body + "<script>" + script + "</script>"),
            context,
            new HtmlDocumentOptions { MediaType = MediaType.Screen, PageWidthPoints = 400, ScriptHost = host, MeasuringCanvasFactory = () => context.CreateCanvas(1, 1) });
        Assert.True(errors.Count == 0, string.Join(" / ", errors));
        return lines;
    }

    [Fact]
    public async Task ATreeError_IsADomExceptionWithItsNameAndLegacyCode()
    {
        var log = await LogAsync("""
            try { document.body.appendChild(document.documentElement); } catch (e) {
                console.log([e instanceof DOMException, e instanceof Error, e.name, e.code, e.constructor === DOMException].join());
            }
            try { document.body.removeChild(document.createElement('p')); } catch (e) { console.log(e.name + ':' + e.code); }
            try { document.querySelector('['); } catch (e) { console.log(e.name + ':' + e.code); }
            console.log(Object.prototype.toString.call(new DOMException('m', 'AbortError')) + new DOMException('m', 'AbortError').code + DOMException.ABORT_ERR);
            """);

        Assert.Equal(["true,true,HierarchyRequestError,3,true", "NotFoundError:8", "SyntaxError:12", "[object DOMException]2020"], log);
    }

    [Fact]
    public async Task AnEventCanBeMadeAndInitializedTheOldWay()
    {
        var log = await LogAsync("""
            var e = document.createEvent('Event');
            console.log([e.type === '', e.bubbles, e.cancelable].join());
            try { document.dispatchEvent(e); } catch (x) { console.log(x.name); }
            e.initEvent('ping', true, true);
            var seen = [];
            document.addEventListener('ping', function (ev) { seen.push(ev.type); ev.preventDefault(); });
            console.log(document.dispatchEvent(e) + ':' + seen + ':' + e.defaultPrevented + ':' + e.returnValue);
            try { Event(); } catch (x) { console.log(x instanceof TypeError); }
            try { new Event(); } catch (x) { console.log(x instanceof TypeError); }
            try { document.createEvent('Nonsense'); } catch (x) { console.log(x.name); }
            var c = new Event('x'); c.cancelBubble = true;
            console.log(c.cancelBubble + ':' + Event.AT_TARGET + ':' + c.AT_TARGET);
            """);

        Assert.Equal(["true,false,false", "InvalidStateError", "false:ping:true:false", "true", "true", "NotSupportedError", "true:2:2"], log);
    }

    [Fact]
    public async Task EventTargetTextAndFragmentCanBeMadeWithNew()
    {
        var log = await LogAsync("""
            var t = new EventTarget(), n = 0;
            t.addEventListener('a', function () { n++; });
            t.dispatchEvent(new Event('a'));
            class Sub extends EventTarget {}
            var s = new Sub();
            var text = new Text('hi');
            var fragment = new DocumentFragment();
            fragment.appendChild(text);
            console.log([n, s instanceof Sub, s instanceof EventTarget, text.data, fragment.childNodes.length, text instanceof Text].join());
            """);

        Assert.Equal(["1,true,true,hi,1,true"], log);
    }

    [Fact]
    public async Task ListsAreNodeListAndHtmlCollection()
    {
        var log = await LogAsync("""
            var lists = [document.body.childNodes, document.body.querySelectorAll('div'), document.body.children, document.getElementsByTagName('div')];
            console.log([lists[0] instanceof NodeList, lists[1] instanceof NodeList, lists[2] instanceof HTMLCollection, lists[3] instanceof HTMLCollection].join());
            console.log([lists[3].item(0).id, lists[3].item(5), lists[3].namedItem('b').id, lists[3].namedItem('zz'), lists[3].length].join());
            """, "<div id=a></div><div id=b></div>");

        Assert.Equal(["true,true,true,true", "a,,b,,2"], log);
    }

    [Fact]
    public async Task TextCanBeEditedByOffset()
    {
        var log = await LogAsync("""
            var t = document.createTextNode('0123456789');
            t.deleteData(2, 3); t.insertData(0, 'ab'); t.replaceData(1, 2, 'XYZ'); t.appendData('!');
            var out = [t.data, t.substringData(1, 3), t.length];
            try { t.substringData(99, 1); } catch (e) { out.push(e.name); }
            var p = document.createElement('p'); p.appendChild(t);
            var tail = t.splitText(3);
            out.push(t.data, tail.data, p.childNodes.length, tail.previousSibling === t);
            t.before('B'); t.after('A'); out.push(p.textContent);
            console.log(out.join('|'));
            """);

        Assert.Equal(["aXYZ156789!|XYZ|11|IndexSizeError|aXY|Z156789!|2|true|BaXYAZ156789!"], log);
    }

    [Fact]
    public async Task ClassList_FollowsTheTokenListRules()
    {
        var log = await LogAsync("""
            var d = document.createElement('div');
            d.setAttribute('class', '  a  a b ');
            var out = [d.classList.length, d.classList.item(1), d.classList.contains('b')];
            d.classList.add(); out.push(d.className);
            d.className = 'a b c a'; d.classList.remove('a'); out.push(d.className);
            d.className = 'a b'; out.push(d.classList.replace('b', 'c'), d.className, d.classList.replace('zz', 'q'));
            d.className = 'a b c'; d.classList.replace('c', 'a'); out.push(d.className);
            out.push(d.classList.toggle('x'), d.classList.toggle('x'), d.classList.toggle('y', false), d.classList.toggle('a', true), d.className);
            for (var bad of ['', 'a b', 'a\t']) { try { d.classList.add(bad); } catch (e) { out.push(e.name); } }
            d.classList = 'one two'; out.push(d.className);
            var e2 = document.createElement('div'); e2.classList.remove('q'); out.push(e2.hasAttribute('class'));
            console.log(out.join('|'));
            """);

        Assert.Equal(["2|b|true|a b|b c|true|a c|false|a b|true|false|false|true|a b|SyntaxError|InvalidCharacterError|InvalidCharacterError|one two|false"], log);
    }

    [Fact]
    public async Task ElementNames_AreValidatedAndNamespacesKept()
    {
        var log = await LogAsync("""
            var out = [];
            for (var name of ['', 'a b', '1a', 'a>']) { try { document.createElement(name); } catch (e) { out.push(e.name); } }
            var svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg:linearGradient');
            out.push(svg.namespaceURI, svg.prefix, svg.localName, svg.tagName);
            var h = document.createElementNS('http://www.w3.org/1999/xhtml', 'DIV');
            out.push(h.tagName, h.prefix, document.createElement('div').namespaceURI, document.createElement('div').prefix);
            var cases = [[null, 'a:b'], ['x', 'f:o:o'], ['http://www.w3.org/XML/1998/namespace', 'a:b'], [null, 'xmlns'], ['http://www.w3.org/2000/xmlns/', 'a:b'], ['', ':a']];
            for (var c of cases) { try { document.createElementNS(c[0], c[1]); out.push('ok'); } catch (e) { out.push(e.name); } }
            console.log(out.join('|'));
            """);

        Assert.Equal(["InvalidCharacterError|InvalidCharacterError|InvalidCharacterError|InvalidCharacterError|http://www.w3.org/2000/svg|svg|linearGradient|svg:linearGradient|DIV||http://www.w3.org/1999/xhtml||NamespaceError|NamespaceError|ok|NamespaceError|NamespaceError|NamespaceError"], log);
    }

    [Fact]
    public async Task CompareDocumentPosition_OrdersNodes()
    {
        var log = await LogAsync("""
            var a = document.getElementById('a'), b = document.getElementById('b'), i = document.getElementById('i');
            var lone = document.createElement('p');
            console.log([a.compareDocumentPosition(a), a.compareDocumentPosition(b), b.compareDocumentPosition(a), a.compareDocumentPosition(i), i.compareDocumentPosition(a),
                a.compareDocumentPosition(lone) & 1].join());
            """, "<div id=a><i id=i></i></div><div id=b></div>");

        Assert.Equal(["0,4,2,20,10,1"], log);
    }

    [Fact]
    public async Task AbortControllerAbortsItsSignal()
    {
        var log = await LogAsync("""
            var c = new AbortController(), seen = [];
            c.signal.addEventListener('abort', function () { seen.push('event'); });
            c.signal.onabort = function () { seen.push('on'); };
            console.log(c.signal.aborted);
            c.abort(); c.abort();
            console.log([c.signal.aborted, c.signal.reason.name, seen.join(), c.signal instanceof EventTarget, AbortSignal.abort().aborted].join());
            try { c.signal.throwIfAborted(); } catch (e) { console.log(e.name); }
            try { new AbortSignal(); } catch (e) { console.log(e instanceof TypeError); }
            """);

        Assert.Equal(["false", "true,AbortError,event,on,true,true", "AbortError", "true"], log);
    }

    [Fact]
    public async Task ATreeWalkerAndANodeIterator_VisitTheNodesTheyShow()
    {
        var log = await LogAsync("""
            var root = document.getElementById('r');
            var all = [], w = document.createTreeWalker(root, NodeFilter.SHOW_ELEMENT);
            while (w.nextNode()) all.push(w.currentNode.id);
            var back = [];
            while (w.previousNode()) back.push(w.currentNode.id);
            var skipB = document.createTreeWalker(root, NodeFilter.SHOW_ELEMENT, { acceptNode: function (n) { return n.id === 'b' ? NodeFilter.FILTER_REJECT : NodeFilter.FILTER_ACCEPT; } });
            var kids = []; var k = skipB.firstChild(); while (k) { kids.push(k.id); k = skipB.nextSibling(); }
            var it = document.createNodeIterator(root, NodeFilter.SHOW_ELEMENT), seen = [], n;
            while ((n = it.nextNode())) seen.push(n.id);
            var rev = []; while ((n = it.previousNode())) rev.push(n.id);
            console.log([all.join(), back.join(), kids.join(), seen.join(), rev.join(), w instanceof TreeWalker].join('|'));
            """, "<div id=r><p id=a><i id=c></i></p><p id=b><i id=d></i></p><p id=e></p></div>");

        Assert.Equal(["a,c,b,d,e|d,b,c,a,r|a,e|r,a,c,b,d,e|e,d,b,c,a,r|true"], log);
    }
}
