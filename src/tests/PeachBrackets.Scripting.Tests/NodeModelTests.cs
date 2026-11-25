namespace PeachBrackets.Scripting.Tests;

/// <summary>The node kinds beyond the box tree: comments, processing instructions, document types, attributes, other documents, observers, ranges and live lists.</summary>
public class NodeModelTests
{
    private static async Task<List<string>> LogAsync(string script, string body = "")
    {
        var logger = new CollectingLogger();
        var host = new JintScriptHost(new JintScriptOptions { Logger = logger });
        var context = await TestDocument.NewContextAsync();
        using var document = await HtmlDocument.LoadAsync(
            TestDocument.Wrap(body + "<script>" + script + "</script>"),
            context,
            new HtmlDocumentOptions { MediaType = MediaType.Screen, PageWidthPoints = 400, ScriptHost = host, MeasuringCanvasFactory = () => context.CreateCanvas(1, 1) });
        Assert.True(logger.Errors.Count == 0, string.Join(" / ", logger.Errors));
        return logger.Texts;
    }

    [Fact]
    public async Task CommentsProcessingInstructionsAndDoctypesAreNodes()
    {
        var log = await LogAsync("""
            var c = document.createComment('hi');
            var pi = document.createProcessingInstruction('xml-stylesheet', 'href="a.css"');
            var t = document.implementation.createDocumentType('html', '', '');
            document.body.appendChild(c); document.body.appendChild(pi);
            console.log([c.nodeType, c.nodeName, c.data, c.length, c instanceof Comment, c instanceof CharacterData].join());
            console.log([pi.nodeType, pi.target, pi.data, t.nodeType, t.name, document.doctype.nodeType, document.doctype.name].join());
            console.log([document.body.lastChild === pi, document.body.childNodes.length > 1, c.textContent].join());
            console.log([document.body.innerHTML.indexOf('<!--hi-->') >= 0, c.cloneNode().data].join());
            c.data = 'bye'; c.appendData('!'); console.log([c.data, c.substringData(1, 2)].join());
            try { c.appendChild(document.createElement('p')); } catch (e) { console.log(e.name); }
            """);

        Assert.Equal(
        [
            "8,#comment,hi,2,true,true", "7,xml-stylesheet,href=\"a.css\",10,html,10,html", "true,true,hi",
            "true,hi", "bye!,ye", "HierarchyRequestError",
        ], log);
    }

    [Fact]
    public async Task AnotherDocumentCanBeMadeAndNodesMovedIntoIt()
    {
        var log = await LogAsync("""
            var d = document.implementation.createHTMLDocument('T');
            var p = document.createElement('p'); p.textContent = 'x';
            document.body.appendChild(p);
            d.body.appendChild(p);
            console.log([d.title, d.body.firstChild === p, p.ownerDocument === d, document.body.contains(p), d.defaultView === null].join());
            var x = new DOMParser().parseFromString('<a><b id="k">t</b></a>', 'application/xml');
            console.log([x.documentElement.tagName, x.getElementById('k') === null, x.getElementsByTagName('b').length, x.contentType, x instanceof XMLDocument].join());
            var h = new DOMParser().parseFromString('<!DOCTYPE html><p>hey</p>', 'text/html');
            console.log([h.body.textContent, h.doctype.name, h.compatMode].join());
            var a = document.adoptNode(h.body.firstChild);
            console.log([a.ownerDocument === document, h.body.childNodes.length].join());
            """);

        Assert.Equal(["T,true,true,false,true", "a,false,1,application/xml,false", "hey,html,CSS1Compat", "true,0"], log);
    }

    [Fact]
    public async Task AttributesAreNodesWithNamespaces()
    {
        var log = await LogAsync("""
            var e = document.createElement('div');
            e.setAttributeNS('http://www.w3.org/1999/xlink', 'xlink:href', '#a');
            var a = e.getAttributeNodeNS('http://www.w3.org/1999/xlink', 'href');
            console.log([a.name, a.localName, a.prefix, a.namespaceURI, a.value, a.nodeType, a.ownerElement === e, e.getAttributeNS('http://www.w3.org/1999/xlink', 'href')].join());
            e.setAttribute('Data-X', '1');
            console.log([e.getAttribute('data-x'), e.attributes.length, e.attributes[1].name, e.attributes.getNamedItem('DATA-X').value].join());
            var made = document.createAttribute('Title'); made.value = 'v';
            e.setAttributeNode(made); console.log([e.title, made.ownerElement === e, e.removeAttributeNode(made) === made, e.hasAttribute('title'), made.value].join());
            console.log([e.lookupNamespaceURI(null) === 'http://www.w3.org/1999/xhtml', e.lookupPrefix('http://www.w3.org/1999/xlink')].join());
            try { e.setAttribute('a b', ''); } catch (x) { console.log(x.name); }
            """);

        Assert.Equal(
        [
            "xlink:href,href,xlink,http://www.w3.org/1999/xlink,#a,2,true,#a", "1,2,data-x,1", "v,true,true,false,v", "true,",
            "InvalidCharacterError",
        ], log);
    }

    [Fact]
    public async Task AnObserverIsToldOfChangesInOneBatch()
    {
        var log = await LogAsync("""
            var target = document.createElement('div'); document.body.appendChild(target);
            var seen = [];
            var o = new MutationObserver(function (records, observer) {
                seen.push(records.map(function (r) { return r.type + ':' + (r.attributeName || '') + ':' + r.addedNodes.length + ':' + r.removedNodes.length + ':' + r.oldValue; }).join('|'));
                console.log(seen.join(';') + ' ' + (observer === o));
            });
            o.observe(target, { attributes: true, childList: true, subtree: true, attributeOldValue: true });
            target.setAttribute('a', '1'); target.setAttribute('a', '2');
            var child = target.appendChild(document.createElement('i'));
            child.setAttribute('b', 'x'); child.remove();
            """);

        Assert.Equal(["attributes:a:0:0:null|attributes:a:0:0:1|childList::1:0:null|attributes:b:0:0:null|childList::0:1:null true"], log);
    }

    [Fact]
    public async Task ARangeSelectsExtractsAndFollowsChanges()
    {
        var log = await LogAsync("""
            var p = document.createElement('p'); p.innerHTML = 'ab<b>cd</b>ef'; document.body.appendChild(p);
            var r = document.createRange();
            r.setStart(p.firstChild, 1); r.setEnd(p.lastChild, 1);
            console.log([r.toString(), r.collapsed, r.commonAncestorContainer === p, r.startOffset, r.endOffset].join());
            var live = document.createRange(); live.setStart(p.lastChild, 1); live.setEnd(p.lastChild, 2);
            var f = r.extractContents();
            console.log([p.innerHTML, f.childNodes.length, f.textContent, r.collapsed].join());
            p.firstChild.insertData(1, 'ZZ');
            console.log(live.startContainer === p.lastChild ? live.startOffset : 'moved');
            var s = document.createRange(); s.selectNodeContents(p); s.insertNode(document.createElement('hr'));
            console.log([p.firstChild.nodeName, s.toString()].join());
            try { r.setStart(document.doctype, 0); } catch (e) { console.log(e.name); }
            getSelection().selectAllChildren(p); console.log([getSelection().toString(), getSelection().rangeCount].join());
            """);

        Assert.Equal(["bcde,false,true,1,1", "af,3,bcde,true", "0", "HR,aZZf", "InvalidNodeTypeError", "aZZf,1"], log);
    }

    [Fact]
    public async Task ListsFollowTheTree()
    {
        var log = await LogAsync("""
            var div = document.createElement('div'); document.body.appendChild(div);
            var kids = div.childNodes, els = div.children, spans = div.getElementsByTagName('span'), same = div.childNodes === kids;
            div.innerHTML = '<span></span><span class="k"></span>text';
            console.log([kids.length, els.length, spans.length, div.getElementsByClassName('k').length, same, kids[2].nodeType, kids.item(1) === els[1]].join());
            div.firstChild.remove();
            console.log([kids.length, els.length, spans.length, Array.prototype.slice.call(spans).length, 'length' in spans, 5 in kids].join());
            console.log([document.getElementsByTagName('SPAN').length, document.body.getElementsByTagNameNS('*', 'span').length].join());
            """);

        Assert.Equal(["3,2,2,1,true,3,true", "2,1,1,1,true,false", "1,1"], log);
    }

    [Fact]
    public async Task CdataSectionsBelongToXmlDocuments()
    {
        var log = await LogAsync("""
            var x = new DOMParser().parseFromString('<a/>', 'text/xml');
            var c = x.createCDATASection('d');
            console.log([c.nodeType, c.nodeName, c instanceof CDATASection, c instanceof Text, c.cloneNode().nodeType].join());
            try { document.createCDATASection('d'); } catch (e) { console.log(e.name); }
            try { x.createCDATASection('a]]>b'); } catch (e) { console.log(e.name); }
            """);

        Assert.Equal(["4,#cdata-section,true,true,4", "NotSupportedError", "InvalidCharacterError"], log);
    }
}
