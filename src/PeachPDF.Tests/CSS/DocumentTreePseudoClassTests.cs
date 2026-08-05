using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Svg;
using System.Xml.Linq;

namespace PeachPDF.Tests.CSS;

/// <summary>
/// The three pseudo-classes that depend only on the document tree, so a static renderer can answer them:
/// <c>:empty</c> (Selectors 4 §9.5), <c>:any-link</c> (§7.1) and <c>:scope</c> (§6.6). They used to parse
/// and select nothing, alongside the interaction/shadow-DOM names that genuinely have nothing to select.
/// </summary>
public class DocumentTreePseudoClassTests
{
    private const string Blue = "rgb(0, 0, 255)";

    // ── :empty ─────────────────────────────────────────────────────────────────────────────────

    [Theory]
    // "no children at all, other than white space" — including a run of newlines and tabs, which the
    // parser does hand to the box tree as a text box.
    [InlineData("<p id='p'></p>", true)]
    [InlineData("<p id='p'>   </p>", true)]
    [InlineData("<p id='p'>\n\t\r\n </p>", true)]
    // A comment is not a child for :empty's purposes (and never reaches the box tree at all).
    [InlineData("<p id='p'><!-- a comment --></p>", true)]
    [InlineData("<p id='p'>\n  <!-- a comment -->\n</p>", true)]
    // Any text that is not white space.
    [InlineData("<p id='p'>x</p>", false)]
    [InlineData("<p id='p'> x </p>", false)]
    // U+00A0 is significant content, not collapsible white space.
    [InlineData("<p id='p'>&nbsp;</p>", false)]
    // Any element child, even an empty or void one.
    [InlineData("<p id='p'><span></span></p>", false)]
    [InlineData("<p id='p'><br></p>", false)]
    public async Task Empty_MatchesOnlyAnElementWithNoNonWhitespaceChildren(string body, bool shouldMatch)
    {
        var box = await Box(Html("p:empty { color: #0000ff; }", body), "p");
        Assert.Equal(shouldMatch, box.Color == Blue);
    }

    [Fact]
    public async Task Empty_IsUnaffectedByGeneratedContent()
    {
        // ::before/::after synthesize a real child box as a side effect of their own rule matching, in the
        // same cascade pass :empty is evaluated in — and PeachPDF's UA sheet matches `:before, :after` on
        // every element. :empty is defined over the source tree, so none of that may count as a child.
        var box = await Box(Html("p::before { content: 'x'; } p:empty { color: #0000ff; }", "<p id='p'></p>"), "p");
        Assert.Equal(Blue, box.Color);
    }

    [Fact]
    public async Task Empty_IsUnaffectedByASynthesizedMarker()
    {
        // An <li>'s ::marker box is generated too, so an empty list item is still :empty.
        var box = await Box(Html("li:empty { color: #0000ff; }", "<ul><li id='li'></li></ul>"), "li");
        Assert.Equal(Blue, box.Color);
    }

    [Fact]
    public async Task Empty_ComposesWithCombinatorsAndNegation()
    {
        var html = Html(
            ".row :empty { color: #0000ff; } .row p:not(:empty) { color: #ff0000; }",
            "<div class='row'><p id='blank'></p><p id='filled'>text</p></div>");

        Assert.Equal(Blue, (await Box(html, "blank")).Color);
        Assert.Equal("rgb(255, 0, 0)", (await Box(html, "filled")).Color);
    }

    [Fact]
    public async Task Empty_SharingAListWithAMatchableSelector_StillApplies()
    {
        // :empty was previously registered-but-unmatchable, so the list stayed valid. Making it match
        // must not change that half of the contract.
        var html = Html("h1, p:empty { color: #0000ff; }", "<h1 id='h'>Title</h1><p id='p'></p>");
        Assert.Equal(Blue, (await Box(html, "h")).Color);
        Assert.Equal(Blue, (await Box(html, "p")).Color);
    }

    [Fact]
    public async Task Empty_SeesThroughAnAnonymousBox()
    {
        // The restructuring passes that run after the cascade wrap element/text children in anonymous
        // boxes, which are not source children — the test descends into them rather than stopping there.
        var root = await BuildRoot(Html(
            "p { display: block; }",
            "<div id='mixed'>loose text<p>block</p></div><div id='blank'></div>"));

        var mixed = DomUtils.GetBoxById(root, "mixed")!;
        var blank = DomUtils.GetBoxById(root, "blank")!;

        Assert.Contains(mixed.Boxes, b => b.HtmlTag is null && b.Boxes.Count > 0);
        Assert.False(((ICssDomNode)mixed).IsEmpty);
        Assert.True(((ICssDomNode)blank).IsEmpty);
        Assert.False(Matches(mixed, ":empty"));
        Assert.True(Matches(blank, ":empty"));
    }

    [Fact]
    public async Task Empty_NeverMatchesANonElementBox()
    {
        // Anonymous/text boxes have no tag, so they are not elements and can never be :empty.
        var root = await BuildRoot(Html("p { display: block; }", "<div id='mixed'>loose text<p>block</p></div>"));
        var anonymous = DomUtils.GetBoxById(root, "mixed")!.Boxes.First(b => b.HtmlTag is null);

        Assert.Null(((ICssDomNode)anonymous).TagName);
        Assert.False(Matches(anonymous, ":empty"));
    }

    // ── :any-link ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnyLink_MatchesAnAnchorWithAnHref_AndNothingElse()
    {
        var html = Html(
            ":any-link { color: #0000ff; }",
            "<a id='link' href='https://example.com'>link</a><a id='anchor' name='top'>anchor</a><p id='p'>text</p>");

        Assert.Equal(Blue, (await Box(html, "link")).Color);
        Assert.NotEqual(Blue, (await Box(html, "anchor")).Color);
        Assert.NotEqual(Blue, (await Box(html, "p")).Color);
    }

    [Fact]
    public async Task AnyLink_AndLink_SelectTheSameElements()
    {
        // :any-link is the union of :link and :visited; :visited never matches (no browsing history in a
        // static PDF), so the two must agree everywhere.
        var body = "<a id='link' href='#x'>link</a><a id='anchor'>anchor</a>";
        var withAnyLink = Html("a:any-link { color: #0000ff; }", body);
        var withLink = Html("a:link { color: #0000ff; }", body);

        foreach (var id in new[] { "link", "anchor" })
        {
            Assert.Equal(
                (await Box(withLink, id)).Color == Blue,
                (await Box(withAnyLink, id)).Color == Blue);
        }
    }

    [Fact]
    public async Task AnyLink_ComposesWithACompoundSelector()
    {
        var html = Html(
            "a.cta:any-link { color: #0000ff; }",
            "<a id='cta' class='cta' href='#x'>go</a><a id='plain' href='#y'>plain</a>");

        Assert.Equal(Blue, (await Box(html, "cta")).Color);
        Assert.NotEqual(Blue, (await Box(html, "plain")).Color);
    }

    // ── :scope ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Scope_MatchesTheRootElement_LikeRoot()
    {
        // With no scoping root in play, :scope is the document's root element — the same answer :root
        // gives, on the same element and on no other.
        var root = await BuildRoot(Html(":scope { color: #0000ff; }", "<p id='p'>text</p>"));
        var htmlBox = DomUtils.GetBoxByTagName(root, "html")!;

        Assert.Equal(Blue, htmlBox.Color);
        Assert.True(Matches(htmlBox, ":scope"));
        Assert.True(Matches(htmlBox, ":root"));

        foreach (var tag in new[] { "body", "p" })
        {
            var box = DomUtils.GetBoxByTagName(root, tag)!;
            Assert.False(Matches(box, ":scope"));
            Assert.False(Matches(box, ":root"));
        }
    }

    [Fact]
    public async Task Scope_WorksAsTheLeftHandSideOfACombinator()
    {
        // The idiomatic use: `:scope x` / `:scope > x`, which stylesheets write in place of `:root x`.
        var html = Html(
            ":scope p { color: #0000ff; } :scope > body > p.direct { font-weight: bold; }",
            "<p id='deep' class='direct'>text</p>");

        var box = await Box(html, "deep");
        Assert.Equal(Blue, box.Color);
        Assert.Equal("bold", box.FontWeight.ToString());
    }

    [Fact]
    public async Task Scope_AndRoot_ShareTheirDeclarationsInAList()
    {
        var box = await Box(Html(":root, :scope { --brand: #0000ff; } p { color: var(--brand); }", "<p id='p'>text</p>"), "p");
        Assert.Equal(Blue, box.Color);
    }

    // ── SVG: :empty is a document-tree question, so the SVG nodes answer it too ─────────────────

    [Theory]
    [InlineData("<rect/>", true)]
    [InlineData("<rect></rect>", true)]
    [InlineData("<rect>   </rect>", true)]
    [InlineData("<rect><!-- note --></rect>", true)]
    [InlineData("<rect><title>t</title></rect>", false)]
    [InlineData("<rect>text</rect>", false)]
    [InlineData("<rect><![CDATA[data]]></rect>", false)]
    public void StandaloneSvg_EmptyMatchesOnlyAnElementWithNoRealContent(string markup, bool shouldMatch)
    {
        var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\">"
                  + "<style>rect:empty { fill: #00ff00; }</style>"
                  + markup
                  + "</svg>";

        var root = XDocument.Parse(svg).Root!;
        var cssData = SvgCssStyling.BuildStyleData(SvgCssStyling.CollectStyleText(root));
        var rect = root.Descendants().Single(e => e.Name.LocalName == "rect");
        var matched = SvgCssStyling.GetMatchedDeclarations(new SvgXmlDomNode(rect, root), cssData, "print");

        Assert.Equal(shouldMatch, matched!.ContainsKey("fill"));
    }

    [Fact]
    public async Task InlineSvg_EmptyIsAnsweredOverTheBoxTree()
    {
        // The SVG-flavored view of an inline <svg>'s boxes runs the same box-tree test the HTML side does.
        // Read before layout: CssBoxSvg clears its child boxes once it has built its scene graph, which is
        // also why this node type only ever answers during SvgTreeBuilder's own styling pass.
        var root = await BuildDom(Html(
            "",
            "<svg id='svg' xmlns='http://www.w3.org/2000/svg'><rect id='r' width='10' height='10'/><g id='g'><rect width='4' height='4'/></g></svg>"));

        var svgBox = DomUtils.GetBoxById(root, "svg")!;
        var rect = DomUtils.GetBoxById(root, "r")!;
        var group = DomUtils.GetBoxById(root, "g")!;

        Assert.True(((ICssDomNode)new SvgCssBoxDomNode(rect, svgBox)).IsEmpty);
        Assert.False(((ICssDomNode)new SvgCssBoxDomNode(group, svgBox)).IsEmpty);
    }

    // ── Helpers (mirrors UnmatchableSelectorRegistrationTests.cs conventions) ───────────────────

    /// <summary>
    /// Does <paramref name="selector"/> match <paramref name="node"/>? Asked through the ordinary
    /// author-rule lookup, so the rule index and the matcher both run exactly as they do in a cascade.
    /// </summary>
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

    /// <summary>The box tree as the cascade left it, without a layout pass.</summary>
    private static async Task<CssBox> BuildDom(string html)
    {
        var adapter = new PdfSharpAdapter();
        var container = new HtmlContainerInt(adapter);
        await container.SetHtml(html, null);

        Assert.NotNull(container.Root);
        return container.Root!;
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
