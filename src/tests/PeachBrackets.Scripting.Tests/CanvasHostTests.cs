namespace PeachBrackets.Scripting.Tests;

/// <summary>The canvas element itself: how its <c>width</c> and <c>height</c> are read from attributes and set from script, and what it lays out as.</summary>
public class CanvasHostTests
{
    // Loads a page with a canvas element and a script, without painting it (a page whose only content is an empty canvas has nothing to paint), and returns what the script logged.
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
        options.SetMargins(0);
        using var document = await HtmlDocument.LoadAsync(TestDocument.Wrap("<canvas id=c style=\"display:block\" width=60 height=40></canvas><script>" + script + "</script>"), context, options);
        Assert.True(logger.Errors.Count == 0, string.Join(" / ", logger.Errors));
        return logger.Texts;
    }

    // The attribute is set from script (so any text can be tried), then the element is asked what it comes to.
    private static async Task<List<string>> SizeAsync(string attributeValue)
    {
        var text = System.Text.Json.JsonSerializer.Serialize(attributeValue);
        return await LogAsync("var c = document.getElementById('c'); var v = " + text + "; c.setAttribute('width', v); c.setAttribute('height', v); " +
            "Log.info(c.width + ' ' + c.height + ' ' + getComputedStyle(c).getPropertyValue('width') + ' ' + getComputedStyle(c).getPropertyValue('height') + ' ' + (c.getAttribute('width') === v));");
    }

    [Theory]
    [InlineData("100", 100)]
    [InlineData("100.999", 100)]
    [InlineData("  100", 100)]
    [InlineData("\r\n\t\f100", 100)]
    [InlineData("+100", 100)]
    [InlineData("100em", 100)]
    [InlineData("100%", 100)]
    [InlineData("100junk", 100)]
    [InlineData("1e2", 1)]
    [InlineData("0100", 100)]
    [InlineData("0x100", 0)]
    [InlineData("0", 0)]
    [InlineData("-0", 0)]
    public async Task AnAttributeIsReadByTheHtmlRulesForNonNegativeIntegers(string attribute, int expected)
    {
        var log = await SizeAsync(attribute);

        Assert.Equal($"{expected} {expected} {expected}px {expected}px true", log[0]);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("2147483648")]
    [InlineData("99999999999999999999")]
    [InlineData(".5")]
    public async Task AnAttributeWithNoNumberToRead_OrOneThatIsTooLarge_IsTheDefaultSize(string attribute)
    {
        var log = await SizeAsync(attribute);

        Assert.Equal("300 150 300px 150px true", log[0]);
    }

    [Fact]
    public async Task SettingWidthOrHeight_TakesTheValueModulo2To32_AndTheDefaultWhenTooLarge()
    {
        var log = await LogAsync("""
            var c = document.getElementById('c');
            c.width = 200 - Math.pow(2, 32); c.height = 200 - Math.pow(2, 32);
            var a = c.width + ' ' + c.height;
            c.width = '400x'; c.height = 'foo';
            var b = c.width + ' ' + c.height;
            c.width = 2147483648; c.height = 2147483648;
            var d = c.width + ' ' + c.height + ' ' + c.getAttribute('width');
            c.width = 12.9; c.height = -1;
            Log.info([a, b, d, c.width + ' ' + c.height].join(' | '));
            """);

        // 12.9 is 12 and -1 is 4294967295, which is more than the attribute holds, so the default height.
        Assert.Equal(["200 200 | 0 0 | 300 150 300 | 12 150"], log);
    }

    [Fact]
    public async Task TheParserIsAvailableToHosts()
    {
        Assert.True(PeachBrackets.ScriptHosting.ScriptDocument.TryParseNonNegativeInteger("  42px", out var value));
        Assert.Equal(42u, value);
        Assert.False(PeachBrackets.ScriptHosting.ScriptDocument.TryParseNonNegativeInteger(null, out _));
        Assert.False(PeachBrackets.ScriptHosting.ScriptDocument.TryParseNonNegativeInteger("-5", out _));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ACanvasAsTheSourceOfDrawImage_IsTransparentWithNoContext_AndAnErrorWithNoSize()
    {
        var log = await LogAsync("""
            var g = document.getElementById('c').getContext('2d');
            g.fillStyle = '#0f0'; g.fillRect(0, 0, 10, 10);
            function name(f) { try { f(); return 'none'; } catch (e) { return e.name; } }
            var bare = document.createElement('canvas');
            var results = [name(function () { g.drawImage(bare, 0, 0); })];
            var zero = document.createElement('canvas'); zero.width = 0; zero.height = 50;
            results.push(name(function () { g.drawImage(zero, 0, 0); }));
            zero.width = 50; zero.height = 0;
            results.push(name(function () { g.drawImage(zero, 0, 0); }));
            results.push(name(function () { g.drawImage({}, 0, 0); }));
            results.push(name(function () { g.drawImage(bare, 0); }));
            results.push(Array.from(g.getImageData(5, 5, 1, 1).data).join(','));
            Log.info(results.join(' '));
            """);

        // The canvas with no context draws nothing (the green stays), a canvas with no width or height is an InvalidStateError, a non-image is a TypeError.
        Assert.Equal(["none InvalidStateError InvalidStateError TypeError TypeError 0,255,0,255"], log);
    }

    [Fact]
    public async Task AnImageThatIsNotLoadedYet_IsNotDrawnAndIsNotAnError()
    {
        var log = await LogAsync("""
            var g = document.getElementById('c').getContext('2d');
            g.fillStyle = '#0f0'; g.fillRect(0, 0, 10, 10);
            function name(f) { try { f(); return 'none'; } catch (e) { return e.name; } }
            var none = new Image();
            var loading = new Image(); loading.src = 'data:image/png;base64,' + 'x';
            Log.info([name(function () { g.drawImage(none, 0, 0); }), Array.from(g.getImageData(5, 5, 1, 1).data).join(',')].join(' '));
            """);

        Assert.Equal(["none 0,255,0,255"], log);
    }

    [Fact]
    public async Task ARectangleWithANegativeSize_IsTheSameRectangleAndDoesNotFlipTheImage()
    {
        var log = await LogAsync("""
            var g = document.getElementById('c').getContext('2d');
            var s = document.createElement('canvas'); s.width = 10; s.height = 10;
            var q = s.getContext('2d'); q.fillStyle = '#f00'; q.fillRect(0, 0, 5, 10); q.fillStyle = '#00f'; q.fillRect(5, 0, 5, 10);
            function px(x, y) { return Array.from(g.getImageData(x, y, 1, 1).data).join(','); }
            // The destination is reached back from (30, 20): the rectangle (20, 10) to (30, 20), and the image is not mirrored: red on the left, blue on the right.
            g.drawImage(s, 30, 20, -10, -10);
            Log.info([px(22, 15), px(27, 15)].join(' | '));
            """);

        Assert.Equal(["255,0,0,255 | 0,0,255,255"], log);
    }
}
