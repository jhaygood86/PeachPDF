using PeachBrackets.ScriptHosting;

namespace PeachBrackets.Scripting.Tests;

/// <summary>Element properties that reflect content attributes: each type's get and set rules, run against the elements a page makes.</summary>
public class ReflectionTests
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
        using var document = await HtmlDocument.LoadAsync(
            TestDocument.Wrap("<base href='http://example.test/dir/page.html'><script>" + script + "</script>"), context, options);
        Assert.True(logger.Errors.Count == 0, string.Join(" / ", logger.Errors));
        return logger.Texts;
    }

    [Fact]
    public async Task Strings_Urls_AndBooleans_ReflectTheirAttributes()
    {
        var log = await LogAsync("""
            var a = document.createElement('a');
            Log.info('[' + a.href + '][' + a.title + '][' + a.hidden + ']');
            a.setAttribute('href', '../x?y#z'); a.title = 7; a.hidden = 'yes';
            Log.info([a.href, a.getAttribute('title'), a.getAttribute('hidden'), a.hidden].join('|'));
            a.hidden = 0;
            Log.info(a.hasAttribute('hidden'));
            var img = document.createElement('img');
            img.border = null; img.alt = null;
            Log.info(img.getAttribute('border') + '|' + img.getAttribute('alt'));
            """);

        Assert.Equal(["[][][false]", "http://example.test/x?y#z|7||true", "false", "|null"], log);
    }

    [Fact]
    public async Task Integers_AndDoubles_ParseAndClampAsTheStandardSays()
    {
        var log = await LogAsync("""
            var i = document.createElement('input'), t = document.createElement('td'), ol = document.createElement('ol');
            i.setAttribute('size', ' 12abc'); t.setAttribute('colspan', '2000'); ol.setAttribute('start', '-5');
            Log.info([i.size, t.colSpan, ol.start, i.tabIndex, document.createElement('div').tabIndex].join(','));
            i.setAttribute('maxlength', 'bad');
            Log.info(i.maxLength);
            try { i.maxLength = -2; } catch (e) { Log.info(e.name); }
            try { i.size = 0; } catch (e) { Log.info(e.name); }
            i.size = 4294967295; Log.info(i.getAttribute('size'));
            t.colSpan = 3.9; i.tabIndex = -1.5; Log.info(t.getAttribute('colspan') + ',' + i.getAttribute('tabindex'));
            var m = document.createElement('meter'); m.value = 1e21; Log.info(m.getAttribute('value'));
            try { m.value = NaN; } catch (e) { Log.info(e.name); }
            """);

        Assert.Equal(["12,1000,-5,0,-1", "-1", "IndexSizeError", "IndexSizeError", "20", "3,-1", "1e+21", "TypeError"], log);
    }

    [Fact]
    public async Task EnumeratedAttributes_ReportKeywords_DefaultsAndNullability()
    {
        var log = await LogAsync("""
            var d = document.createElement('div'), img = document.createElement('img');
            d.setAttribute('dir', 'RTL');
            Log.info(d.dir + '|' + d.ariaBusy);
            d.dir = 'sideways'; Log.info('[' + d.dir + ']' + d.getAttribute('dir'));
            d.ariaBusy = 'true'; d.ariaLabel = 'L';
            Log.info(d.getAttribute('aria-busy') + d.getAttribute('aria-label') + d.ariaLabel);
            d.ariaLabel = undefined; Log.info(d.hasAttribute('aria-label'));
            Log.info(img.crossOrigin + '|' + img.decoding);
            img.setAttribute('crossorigin', 'junk'); Log.info(img.crossOrigin);
            img.crossOrigin = null; Log.info(img.hasAttribute('crossorigin'));
            """);

        Assert.Equal(["rtl|false", "[]sideways", "trueLL", "false", "null|auto", "anonymous", "false"], log);
    }

    [Fact]
    public async Task TokenLists_ReflectTheirOwnAttribute_AndOnlyHtmlElementsHaveTheProperties()
    {
        var log = await LogAsync("""
            var link = document.createElement('link');
            link.relList.add('preload'); link.setAttribute('rel', 'a b a');
            Log.info(link.relList.length + ' ' + link.relList.value + ' ' + link.getAttribute('rel') + ' ' + (link.relList === link.relList));
            var f = document.createElement('iframe'); f.sandbox = 'allow-forms'; Log.info(f.sandbox.value + ' ' + f.getAttribute('sandbox'));
            var svg = document.createElementNS('http://www.w3.org/2000/svg', 'a');
            Log.info(svg.relList === undefined);
            """);

        Assert.Equal(["2 a b a a b a true", "allow-forms allow-forms", "true"], log);
    }
}
