using PeachDrawing.Core;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfBackend;
using PeachPDF.PDF.Drawing;
using System.Diagnostics;
using System.Text;

namespace PeachPDF.Tests.CSS;

/// <summary>
/// The structural selectors (<c>:nth-child</c> family, <c>+</c>, <c>~</c>, <c>:has(+ S)</c>) answer from a per-parent
/// <see cref="SiblingIndex"/> once a parent is wide enough to be worth indexing. Every test here uses a parent past that
/// threshold, so the indexed path is the one under test, and checks it against a naive oracle computed in the test.
/// </summary>
public class SiblingIndexTests
{
    // 36 block children cycling article/section/aside, with a text node between every fifth so the element
    // list and the child list differ. The element at position 7 carries class "m".
    private const int Width = 36;

    private static string WideParent()
    {
        var sb = new StringBuilder("<div class=wide>");
        for (var i = 0; i < Width; i++)
        {
            var tag = (i % 3) switch { 0 => "article", 1 => "section", _ => "aside" };
            sb.Append($"<{tag} id=e{i}{(i == 7 ? " class=m" : "")}>x</{tag}>");
            if (i % 5 == 4) sb.Append("between");
        }

        return sb.Append("</div>").ToString();
    }

    private static string TagOf(int i) => (i % 3) switch { 0 => "article", 1 => "section", _ => "aside" };

    /// <summary>Indices of the elements carrying a non-transparent background after the cascade.</summary>
    private static async Task<HashSet<int>> Matched(string selector, string? body = null)
    {
        var html = $"<!DOCTYPE html><html><head><style>.wide > * {{ display:block }} {selector} {{ background-color: #ff0000; }}</style></head><body>{body ?? WideParent()}</body></html>";
        var root = await Build(html);
        var result = new HashSet<int>();
        Collect(root, result);
        return result;
    }

    private static void Collect(CssBox box, HashSet<int> into)
    {
        var id = box.HtmlTag?.TryGetAttribute("id");
        if (id is { Length: > 1 } && id[0] == 'e' && int.TryParse(id.AsSpan(1), out var n)
            && box.BackgroundColor != "transparent")
        {
            into.Add(n);
        }

        foreach (var child in box.Boxes) Collect(child, into);
    }

    private static async Task<CssBox> Build(string html)
    {
        var renderContext = new PdfRenderContext();
        var container = new HtmlContainerInt(renderContext);
        await container.SetHtml(html, null);

        var size = new Size(595, 842);
        container.PageSize = PeachPDF.Utilities.Utils.ToPixels(size, 1.0);
        container.MaxSize = PeachPDF.Utilities.Utils.ToPixels(size, 1.0);

        var measure = PdfGraphics.CreateMeasureContext(size);
        using var graphics = new PdfCanvas(renderContext, measure, 1.0);
        await container.PerformLayout(graphics);

        Assert.NotNull(container.Root);
        return container.Root!;
    }

    private static HashSet<int> Where(Func<int, bool> predicate) =>
        Enumerable.Range(0, Width).Where(predicate).ToHashSet();

    [Fact]
    public async Task NthChild_AnPlusB_MatchesEveryThirdFromTwo() =>
        Assert.Equal(Where(i => (i + 1) % 3 == 2), await Matched(".wide > :nth-child(3n+2)"));

    [Fact]
    public async Task NthChild_Odd_IgnoresTextNodesBetweenElements() =>
        Assert.Equal(Where(i => (i + 1) % 2 == 1), await Matched(".wide > :nth-child(odd)"));

    [Fact]
    public async Task NthLastChild_CountsFromTheEnd() =>
        Assert.Equal(Where(i => Width - i == 4), await Matched(".wide > :nth-last-child(4)"));

    [Fact]
    public async Task FirstChildAndLastChild_UseTheEnds() =>
        Assert.Equal(Where(i => i == 0 || i == Width - 1), await Matched(".wide > :first-child, .wide > :last-child"));

    [Fact]
    public async Task NthOfType_CountsPerTagName() =>
        Assert.Equal(Where(i => i / 3 % 2 == 0), await Matched(".wide > :nth-of-type(odd)"));

    [Fact]
    public async Task NthLastOfType_CountsPerTagNameFromTheEnd()
    {
        // Each tag has 12 siblings of its own type; the 2nd from the end of each is element index tagStart + 3 * 10.
        var expected = Where(i => i / 3 == 10);
        Assert.Equal(expected, await Matched(".wide > :nth-last-of-type(2)"));
    }

    [Fact]
    public async Task FirstOfTypeAndLastOfType_PickOnePerTag()
    {
        Assert.Equal(Where(i => i < 3), await Matched(".wide > :first-of-type"));
        Assert.Equal(Where(i => i >= Width - 3), await Matched(".wide > :last-of-type"));
    }

    [Fact]
    public async Task AdjacentSibling_MatchesTheElementAfterAMatch() =>
        Assert.Equal(Where(i => i > 0 && TagOf(i) == "section" && TagOf(i - 1) == "article"),
            await Matched(".wide > article + section"));

    [Fact]
    public async Task AdjacentSibling_OfTheFirstElement_MatchesNothing() =>
        Assert.Empty(await Matched(".wide > article#e0 + article#e0"));

    [Fact]
    public async Task GeneralSibling_MatchesAnyLaterElementAfterAMatch() =>
        Assert.Equal(Where(i => i > 7 && TagOf(i) == "aside"), await Matched(".wide > .m ~ aside"));

    [Fact]
    public async Task GeneralSibling_WithNoEarlierMatch_MatchesNothing() =>
        Assert.Empty(await Matched(".wide > aside#e2 ~ #e1"));

    [Fact]
    public async Task HasNextSibling_MatchesTheElementBeforeAMatch() =>
        Assert.Equal(Where(i => i + 1 < Width && i + 1 == 7), await Matched(".wide > :has(+ .m)"));

    [Fact]
    public async Task HasFollowingSibling_MatchesEveryElementBeforeAMatch() =>
        Assert.Equal(Where(i => i < 7), await Matched(".wide > :has(~ .m)"));

    [Fact]
    public async Task NthChildOfSelector_KeepsTheFilteredCount()
    {
        // The "of S" form filters the siblings it counts, so it must not be answered from the unfiltered index:
        // among the 12 <section>s (elements 1, 4, 7, ...), the 3rd is element 7.
        Assert.Equal(Where(i => i == 7), await Matched(".wide > :nth-child(3 of section)"));
    }

    [Fact]
    public async Task NthChild_OnAListWithManyItems_CountsTheItems()
    {
        var body = "<ul class=wide>" + string.Concat(Enumerable.Range(0, Width).Select(i => $"<li id=e{i}>x</li>")) + "</ul>";
        Assert.Equal(Where(i => i % 2 == 1), await Matched(".wide > li:nth-child(even)", body));
    }

    // ── the index itself ──────────────────────────────────────────────────────────────

    private static async Task<List<CssBox>> WideChildren()
    {
        var root = await Build($"<!DOCTYPE html><html><body>{WideParent()}</body></html>");
        var wide = Find(root, b => b.HtmlTag?.TryGetAttribute("class") == "wide")!;
        return wide.Boxes;
    }

    private static CssBox? Find(CssBox box, Func<CssBox, bool> predicate)
    {
        if (predicate(box)) return box;
        foreach (var child in box.Boxes)
        {
            if (Find(child, predicate) is { } found) return found;
        }

        return null;
    }

    [Fact]
    public async Task For_ReturnsTheSameIndexWhileTheListIsUnchanged()
    {
        var children = await WideChildren();
        var first = SiblingIndex.For(children, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(first);
        Assert.Same(first, SiblingIndex.For(children, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(Width, first.Elements.Count);
    }

    [Fact]
    public async Task For_BuildsANewIndexWhenAChildIsAddedOrRemoved()
    {
        var children = await WideChildren();
        var before = SiblingIndex.For(children, StringComparison.OrdinalIgnoreCase)!;

        var removed = children.First(c => ((ICssDomNode)c).TagName is not null);
        children.Remove(removed);

        var after = SiblingIndex.For(children, StringComparison.OrdinalIgnoreCase)!;
        Assert.NotSame(before, after);
        Assert.Equal(Width - 1, after.Elements.Count);
        Assert.Equal(-1, after.IndexOf(removed));
        Assert.Equal(0, after.IndexOf(after.Elements[0]));
    }

    [Fact]
    public async Task IndexOf_RefusesAnAnswerOnceTheListChangedUnderTheSameCount()
    {
        var children = await WideChildren();
        var index = SiblingIndex.For(children, StringComparison.OrdinalIgnoreCase)!;

        var elementSlots = Enumerable.Range(0, children.Count).Where(i => ((ICssDomNode)children[i]).TagName is not null).ToList();
        var (a, b) = (elementSlots[10], elementSlots[11]);
        (children[a], children[b]) = (children[b], children[a]);

        // Same count and same ends, so For still hands the index back, but it must not vouch for the moved elements.
        Assert.Same(index, SiblingIndex.For(children, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(-1, index.IndexOf(children[a]));
        Assert.Equal(-1, index.IndexOf(children[b]));
    }

    [Fact]
    public async Task For_BuildsANewIndexWhenTheLastElementIsReplacedAtTheSameCount()
    {
        var children = await WideChildren();
        var before = SiblingIndex.For(children, StringComparison.OrdinalIgnoreCase)!;

        var lastSlot = Enumerable.Range(0, children.Count).Last(i => ((ICssDomNode)children[i]).TagName is not null);
        var replaced = children[lastSlot];
        children[lastSlot] = children.First(c => ((ICssDomNode)c).TagName is null);

        var after = SiblingIndex.For(children, StringComparison.OrdinalIgnoreCase)!;
        Assert.NotSame(before, after);
        Assert.Equal(Width - 1, after.Elements.Count);
        Assert.Equal(-1, after.IndexOf(replaced));
    }

    [Fact]
    public async Task IndexOf_RejectsANodeThatIsNotAnElementOfThisParent()
    {
        var children = await WideChildren();
        var index = SiblingIndex.For(children, StringComparison.OrdinalIgnoreCase)!;

        var text = children.First(c => ((ICssDomNode)c).TagName is null);
        Assert.Equal(-1, index.IndexOf(text));
        Assert.Equal((-1, 0), index.TypePositionOf(text));
    }

    [Fact]
    public async Task For_DoesNotIndexANarrowParentOrAForeignList()
    {
        var narrow = (await Build("<!DOCTYPE html><html><body><div id=n><p>a</p><p>b</p></div></body></html>"));
        var div = Find(narrow, b => b.HtmlTag?.TryGetAttribute("id") == "n")!;
        Assert.Null(SiblingIndex.For(div.Boxes, StringComparison.OrdinalIgnoreCase));

        IReadOnlyList<ICssDomNode> notABoxList = (await WideChildren()).Cast<ICssDomNode>().ToList();
        Assert.Null(SiblingIndex.For(notABoxList, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TypePositionOf_CountsSameTagSiblings()
    {
        var children = await WideChildren();
        var index = SiblingIndex.For(children, StringComparison.OrdinalIgnoreCase)!;

        // The 3rd <section> (element 7) is the 3rd of 12, and the 1st <article> is the 1st of 12.
        Assert.Equal((2, Width / 3), index.TypePositionOf(index.Elements[7]));
        Assert.Equal((0, Width / 3), index.TypePositionOf(index.Elements[0]));
    }

    // ── cost ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ZebraStripedTable_CascadeIsNotQuadraticInRows()
    {
        // Each row used to scan and copy all its siblings, so doubling the rows quadrupled the cascade. Compared as a
        // ratio of two sizes on the same machine so it needs no absolute budget; the bound is deliberately loose.
        await CascadeMilliseconds(500); // warm-up: JIT and the one-time UA stylesheet parse
        var small = await CascadeMilliseconds(3000);
        var large = await CascadeMilliseconds(24000);

        // 8x the rows: linear is ~8x, quadratic ~64x.
        Assert.True(large < small * 24 + 300, $"3000 rows: {small}ms, 24000 rows: {large}ms");
    }

    private static async Task<long> CascadeMilliseconds(int rows)
    {
        var sb = new StringBuilder("<!DOCTYPE html><html><head><style>tr:nth-child(even) td { background:#eee } tr + tr { margin:0 }</style></head><body><table><tbody>");
        for (var i = 0; i < rows; i++) sb.Append("<tr><td>a</td><td>b</td></tr>");
        sb.Append("</tbody></table></body></html>");

        var renderContext = new PdfRenderContext();
        var container = new HtmlContainerInt(renderContext);

        var watch = Stopwatch.StartNew();
        await container.SetHtml(sb.ToString(), null);
        watch.Stop();
        return watch.ElapsedMilliseconds;
    }
}
