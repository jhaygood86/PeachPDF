using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Utils;
using PeachBrackets.Svg;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachPDF.Rendering;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PeachBrackets.Engine.Tests.CSS;

/// <summary>
/// The allocation-free forms of the class/selector token walk, the length and data-URI parsers, the transform
/// validator and the string-set whitespace collapse give exactly what the array/substring/regex forms gave.
/// </summary>
public class SpanParsingEquivalenceTests
{
    [Theory]
    [InlineData("", new string[0])]
    [InlineData("   ", new string[0])]
    [InlineData("a", new[] { "a" })]
    [InlineData("a b", new[] { "a", "b" })]
    [InlineData("  a   b  ", new[] { "a", "b" })]
    [InlineData("a\tb c", new[] { "a\tb", "c" })]
    public void SpaceSeparatedTokens_MatchesSplitOnSpaceWithoutEmptyEntries(string value, string[] expected)
    {
        var tokens = new List<string>();
        foreach (var token in new SpaceSeparatedTokens(value)) tokens.Add(token.ToString());

        Assert.Equal(expected, tokens);
        Assert.Equal(value.Split(' ').Where(t => t.Length > 0), tokens);
    }

    [Fact]
    public async Task ClassSelectors_MatchTokensSeparatedByRunsOfSpaces()
    {
        var html = "<!DOCTYPE html><html><head><style>.beta { background-color: #ff0000; } .gamma.alpha { color: #00ff00; } [class~=delta] { color: #0000ff; }</style></head>" +
                   "<body><div class='  alpha   beta  delta '>t</div><div class='alphabeta'>t</div></body></html>";
        var root = await BuildRoot(html);
        var boxes = new List<CssBox>();
        Collect(root, "div", boxes);

        Assert.NotEqual("transparent", boxes[0].BackgroundColor);
        Assert.Equal("rgb(0, 0, 255)", boxes[0].Color);
        Assert.Equal("transparent", boxes[1].BackgroundColor);
    }

    [Fact]
    public async Task ComplexSelectors_StillWalkFromTheSubjectBackwards()
    {
        var html = "<!DOCTYPE html><html><head><style>.a > .b .c { background-color: #ff0000; }</style></head>" +
                   "<body><div class='a'><div class='b'><p><span class='c'>x</span></p></div></div><span class='c'>z</span></body></html>";
        var root = await BuildRoot(html);
        var spans = new List<CssBox>();
        Collect(root, "span", spans);

        Assert.NotEqual("transparent", spans[0].BackgroundColor);
        Assert.Equal("transparent", spans[1].BackgroundColor);
    }

    [Theory]
    [InlineData("12px", 12, "Pixels", false)]
    [InlineData("-1.5em", -1.5, "Ems", false)]
    [InlineData("2.5rem", 2.5, "Rem", false)]
    [InlineData("10pt", 10, "Points", false)]
    [InlineData("3zz", 0, "None", true)]
    [InlineData("abpx", 0, "Pixels", true)]
    public void CssLength_ParsesNumberAndUnit(string text, double number, string unit, bool hasError)
    {
        var length = new CssLength(text);

        Assert.Equal(hasError, length.HasError);
        if (!hasError)
        {
            Assert.Equal(unit, length.Unit.ToString());
            Assert.Equal(number, length.Number);
        }
    }

    [Fact]
    public void CssLength_ParsesWithInvariantCultureWhateverTheCurrentCultureIs()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal(1.5, new CssLength("1.5em").Number);
            Assert.Equal(1.5, new CssLength("1.5px").Number);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private const string PngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR42mP4//8/AAX+Av4N70a4AAAAAElFTkSuQmCC";

    [Fact]
    public void DataUri_Base64_PlainWrappedAndEscapedDecodeToTheSameBytes()
    {
        var expected = Convert.FromBase64String(PngBase64);

        Assert.True(DataUriUtils.TryDecodeDataUri("data:image/png;base64," + PngBase64, out var mime, out var plain));
        Assert.Equal("image/png", mime);
        Assert.Equal(expected, plain);

        var wrapped = Regex.Replace(PngBase64, "(.{16})", "$1\r\n");
        Assert.True(DataUriUtils.TryDecodeDataUri("data:image/png;base64," + wrapped, out _, out var wrappedBytes));
        Assert.Equal(expected, wrappedBytes);

        var escaped = PngBase64.Replace("/", "%2F").Replace("+", "%2B");
        Assert.True(DataUriUtils.TryDecodeDataUri("data:image/png;base64," + escaped, out _, out var escapedBytes));
        Assert.Equal(expected, escapedBytes);
    }

    [Fact]
    public void DataUri_HeaderAndTextPayloads()
    {
        Assert.True(DataUriUtils.TryDecodeDataUri("data:,Hello%20W%C3%B6rld", out var mime, out var bytes));
        Assert.Equal("text/plain", mime);
        Assert.Equal("Hello Wörld", Encoding.UTF8.GetString(bytes));

        Assert.True(DataUriUtils.TryDecodeDataUri("data:text/html;charset=utf-8;BASE64,SGk=", out mime, out bytes));
        Assert.Equal("text/html", mime);
        Assert.Equal("Hi", Encoding.UTF8.GetString(bytes));

        Assert.True(DataUriUtils.TryDecodeDataUri("data:image/png;base64,", out _, out bytes));
        Assert.Empty(bytes);
    }

    [Theory]
    [InlineData("data:image/png;base64,@@@@")]
    [InlineData("data:image/png;base64,abc")]
    [InlineData("data:nocomma")]
    public void DataUri_InvalidPayloadsAreRejected(string uri)
    {
        Assert.False(DataUriUtils.TryDecodeDataUri(uri, out _, out _));
    }

    [Theory]
    [InlineData("  a \t\n b  c  ")]
    [InlineData("plain")]
    [InlineData("one two")]
    [InlineData("one  two")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("x  y\u001Cz")]
    public void CollapseWhitespace_MatchesTheRegexForm(string text)
    {
        Assert.Equal(Regex.Replace(text.Trim(), @"\s+", " "), CssNamedStringEngine.CollapseWhitespace(text));
    }

    [Theory]
    [InlineData("translate(10px, 5px)", true)]
    [InlineData("translate(10deg)", false)]
    [InlineData("matrix(1,0,0,1,0,0)", true)]
    [InlineData("matrix(1px,0,0,1,0,0)", false)]
    [InlineData("scale(2, 50%)", true)]
    [InlineData("skew(10deg, 5deg)", true)]
    [InlineData("skew(10, 5deg)", false)]
    [InlineData("rotate3d(1,0,0,45DEG)", true)]
    [InlineData("rotate3d(1deg,0,0,45deg)", false)]
    [InlineData("matrix3d(1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1)", true)]
    [InlineData("translate3d(1px,2px,3deg)", false)]
    [InlineData("scale3d(1,2,3)", true)]
    public void TransformGrammar_ValidatesAsBefore(string value, bool valid)
    {
        Assert.Equal(valid, SvgTransformParser.IsValidCssTransform(value));
    }

    private static void Collect(CssBox box, string tag, List<CssBox> results)
    {
        if (box.HtmlTag?.Name.Equals(tag, StringComparison.OrdinalIgnoreCase) == true) results.Add(box);
        foreach (var child in box.Boxes) Collect(child, tag, results);
    }

    private static async Task<CssBox> BuildRoot(string html)
    {
        var renderContext = new HtmlRenderContext();
        var container = new HtmlContainerInt(renderContext);
        await container.SetHtml(html, null);

        var size = new Size(595, 842);
        container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
        container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);

        using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
        await container.PerformLayout(graphics);
        return container.Root!;
    }
}
