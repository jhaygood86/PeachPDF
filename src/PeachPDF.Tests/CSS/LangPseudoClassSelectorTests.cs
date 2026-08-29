using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Svg;
using System.Linq;
using System.Xml.Linq;

namespace PeachPDF.Tests.CSS;

/// <summary>
/// CSS 2.1 §5.11.4 <c>:lang(C)</c> - matches an element whose language (the nearest ancestor-or-self
/// "lang" attribute, checking the element itself first) is C, or has C as an ASCII-case-insensitive
/// hyphen-delimited prefix. It used to be registered as a "parses but never matches" functional
/// pseudo-class (<c>UnmatchableSelectors.FunctionalPseudoClasses</c>) alongside <c>:host()</c>/
/// <c>:state()</c>; this proves it now actually matches.
/// </summary>
public class LangPseudoClassSelectorTests
{
    private const string Blue = "rgb(0, 0, 255)";

    [Theory]
    [InlineData("en", "en", true)]      // exact match
    [InlineData("en", "en-US", true)]   // hyphen-delimited prefix
    [InlineData("en", "english", false)] // not a hyphen boundary - must not match as a bare substring
    [InlineData("en", "fr", false)]
    [InlineData("EN", "en", true)]      // argument-side case-insensitivity
    [InlineData("en", "EN-us", true)]   // attribute-side case-insensitivity
    public async Task Lang_MatchesExactOrHyphenPrefix_CaseInsensitive(string range, string lang, bool shouldMatch)
    {
        var box = await Box(Html($"p:lang({range}) {{ color: #0000ff; }}", $"<p id='p' lang='{lang}'>x</p>"), "p");
        Assert.Equal(shouldMatch, box.Color == Blue);
    }

    [Fact]
    public async Task Lang_OwnAttributeMatches_NoAncestorInvolved()
    {
        var box = await Box(Html(
            "p:lang(fr) { color: #0000ff; }",
            "<div><p id='p' lang='fr'>x</p></div>"), "p");
        Assert.Equal(Blue, box.Color);
    }

    [Fact]
    public async Task Lang_InheritsFromNearestAncestorWithLangAttribute()
    {
        // Neither <body> nor <p> declares its own "lang" - the nearest ancestor with one is <html>.
        var root = await BuildRoot(
            "<!DOCTYPE html><html lang='fr'><head><style>p:lang(fr) { color: #0000ff; }</style></head>" +
            "<body><p id='p'>x</p></body></html>");
        var box = DomUtils.GetBoxById(root, "p")!;
        Assert.Equal(Blue, box.Color);
    }

    [Fact]
    public async Task Lang_OwnAttributeOverridesAnAncestors()
    {
        var matchOwn = await BuildRoot(
            "<!DOCTYPE html><html lang='fr'><head><style>p:lang(en) { color: #0000ff; }</style></head>" +
            "<body><p id='p' lang='en'>x</p></body></html>");
        Assert.Equal(Blue, DomUtils.GetBoxById(matchOwn, "p")!.Color);

        var noLongerMatchesAncestor = await BuildRoot(
            "<!DOCTYPE html><html lang='fr'><head><style>p:lang(fr) { color: #0000ff; }</style></head>" +
            "<body><p id='p' lang='en'>x</p></body></html>");
        Assert.NotEqual(Blue, DomUtils.GetBoxById(noLongerMatchesAncestor, "p")!.Color);
    }

    [Fact]
    public async Task Lang_NoLangAnywhereInTheDocument_DoesNotMatch()
    {
        var box = await Box(Html("p:lang(en) { color: #0000ff; }", "<p id='p'>x</p>"), "p");
        Assert.NotEqual(Blue, box.Color);
    }

    [Fact]
    public async Task Lang_QuotedStringArgument_AlsoMatches()
    {
        var box = await Box(Html("p:lang(\"en\") { color: #0000ff; }", "<p id='p' lang='en'>x</p>"), "p");
        Assert.Equal(Blue, box.Color);
    }

    [Fact]
    public async Task Lang_ComposesInsideACompoundSelector_RestrictedToItsTagName()
    {
        var root = await BuildRoot(Html(
            "p:lang(fr) { color: #0000ff; }",
            "<div lang='fr'><span id='span'>x</span><p id='p'>y</p></div>"));
        Assert.NotEqual(Blue, DomUtils.GetBoxById(root, "span")!.Color);
        Assert.Equal(Blue, DomUtils.GetBoxById(root, "p")!.Color);
    }

    [Fact]
    public async Task Lang_SharingAListWithAMatchableSelector_StillApplies()
    {
        // Mirrors DocumentTreePseudoClassTests'/UnmatchableSelectorRegistrationTests' own convention:
        // the :lang() half selecting nothing must not affect the h1 half.
        var root = await BuildRoot(Html(
            "h1, p:lang(zz) { color: #0000ff; }",
            "<h1 id='h1'>Title</h1><p id='p'>x</p>"));
        Assert.Equal(Blue, DomUtils.GetBoxById(root, "h1")!.Color);
        Assert.NotEqual(Blue, DomUtils.GetBoxById(root, "p")!.Color);
    }

    [Fact]
    public void Lang_SelectorText_RoundTrips()
    {
        var sheet = ":lang(en) { color: red; }".ToCssStylesheet();
        var rule = Assert.Single(sheet.StyleRules);
        Assert.Equal(":lang(en)", rule.SelectorText);
    }

    // ── Lower-level Matches() check (mirrors DocumentTreePseudoClassTests' own convention) ───────

    [Fact]
    public void Matches_AncestorTwoLevelsUp_StillResolvesTheLanguage()
    {
        var grandparent = new CssBox(null, new HtmlTag("div", false, new Dictionary<string, string> { ["lang"] = "de" }));
        var parent = new CssBox(grandparent, new HtmlTag("div", false, new Dictionary<string, string>()));
        var child = new CssBox(parent, new HtmlTag("span", false, new Dictionary<string, string>()));

        Assert.True(Matches(child, ":lang(de)"));
        Assert.False(Matches(child, ":lang(en)"));
    }

    // ── SVG: the generic ICssDomNode.GetAttribute("lang") walk answers for SVG nodes too ─────────

    [Fact]
    public async Task InlineSvg_LangReachesFromAnEnclosingHtmlAncestor()
    {
        // Mirrors DocumentTreePseudoClassTests.InlineSvg_EmptyIsAnsweredOverTheBoxTree: read before
        // layout, since CssBoxSvg clears its child boxes once it has built its own scene graph.
        var root = await BuildDom(
            "<!DOCTYPE html><html lang='fr'><head></head><body>" +
            "<svg id='svg' xmlns='http://www.w3.org/2000/svg'><rect id='r' width='10' height='10'/></svg>" +
            "</body></html>");

        var svgBox = DomUtils.GetBoxById(root, "svg")!;
        var rect = DomUtils.GetBoxById(root, "r")!;
        ICssDomNode node = new SvgCssBoxDomNode(rect, svgBox);

        Assert.True(Matches(node, ":lang(fr)"));
        Assert.False(Matches(node, ":lang(en)"));
    }

    [Fact]
    public void StandaloneSvg_LangMatchesItsOwnLangAttribute()
    {
        var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\">"
                  + "<style>rect:lang(fr) { fill: #00ff00; }</style>"
                  + "<rect lang=\"fr\"/>"
                  + "</svg>";

        var root = XDocument.Parse(svg).Root!;
        var cssData = SvgCssStyling.BuildStyleData(SvgCssStyling.CollectStyleText(root));
        var rect = root.Descendants().Single(e => e.Name.LocalName == "rect");
        var matched = SvgCssStyling.GetMatchedDeclarations(new SvgXmlDomNode(rect, root), cssData, "print");

        Assert.True(matched!.ContainsKey("fill"));
    }

    // ── Helpers (mirrors DocumentTreePseudoClassTests.cs conventions) ───────────────────────────

    /// <summary>The box tree as the cascade left it, without a layout pass.</summary>
    private static async Task<CssBox> BuildDom(string html)
    {
        var adapter = new PdfSharpAdapter();
        var container = new HtmlContainerInt(adapter);
        await container.SetHtml(html, null);

        Assert.NotNull(container.Root);
        return container.Root!;
    }

    private static bool Matches(ICssDomNode node, string selector)
    {
        var cssData = new CssData();
        cssData.Stylesheets.Add(CssParser.ParseStyleSheet($"{selector} {{ color: red; }}"));
        return cssData.GetAuthorStyleRules(MediaQueryContext.TypeOnly("print"), node).Any();
    }

    private static string Html(string css, string body) =>
        $"<!DOCTYPE html><html><head><style>{css}</style></head><body>{body}</body></html>";

    private static async Task<CssBox> Box(string html, string id)
    {
        var root = await BuildRoot(html);
        var box = DomUtils.GetBoxById(root, id);
        Assert.NotNull(box);
        return box!;
    }

    private static async Task<CssBox> BuildRoot(string html)
    {
        var adapter = new PdfSharpAdapter();
        var container = new HtmlContainerInt(adapter);
        await container.SetHtml(html, null);

        var size = new XSize(595, 842);
        container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
        container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

        var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
        using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
        await container.PerformLayout(graphics);

        Assert.NotNull(container.Root);
        return container.Root!;
    }
}
