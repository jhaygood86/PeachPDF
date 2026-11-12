using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Parse;

namespace PeachBrackets.Engine.Tests.Html.Core.Dom;

/// <summary>
/// What a selector sees as a box's children: its boxes with each anonymous wrapper replaced by what it holds, remembered per parent so the
/// structural selectors do not look for a wrapper among all N children once for each of them.
/// </summary>
public class SelectorChildrenViewTests
{
    private static CssBox Element(CssBox parent, string tag) => new(parent, new HtmlTag(tag, false, []));

    private static CssBox Parent(int elements)
    {
        var parent = Element(null!, "tbody");
        for (var i = 0; i < elements; i++) Element(parent, "tr");
        return parent;
    }

    /// <summary>Moves <paramref name="children"/> into a new anonymous box that takes the place of the first of them.</summary>
    private static CssBox Wrap(CssBox parent, params CssBox[] children)
    {
        var at = parent.Boxes.IndexOf(children[0]);
        var wrapper = new CssBox(parent, null);
        parent.Boxes.Remove(wrapper);
        foreach (var child in children)
        {
            parent.Boxes.Remove(child);
            wrapper.Boxes.Add(child);
        }

        parent.Boxes.Insert(at, wrapper);
        return wrapper;
    }

    [Fact]
    public void WithoutAWrapper_TheAnswerIsTheBoxesThemselves_AtAnyWidth()
    {
        foreach (var width in new[] { 3, SiblingIndex.MinChildren, 40 })
        {
            var parent = Parent(width);
            Assert.Same(parent.Boxes, parent.SelectorChildren);
            Assert.Same(parent.Boxes, parent.SelectorChildren);
        }
    }

    [Fact]
    public void AWrapperIsReplacedByWhatItHolds_AtAnyWidth()
    {
        foreach (var width in new[] { 5, SiblingIndex.MinChildren + 4, 40 })
        {
            var parent = Parent(width);
            var inner = parent.Boxes.Skip(1).Take(3).ToArray();
            Wrap(parent, inner);

            var seen = parent.SelectorChildren;
            Assert.Equal(width, seen.Count);
            Assert.Equal(parent.Boxes[0], seen[0]);
            Assert.Equal<ICssDomNode>(inner, seen.Skip(1).Take(3));
        }
    }

    [Fact]
    public void AWrapperInsideAWrapper_IsFlattenedToo()
    {
        var parent = Parent(30);
        var first = parent.Boxes[2];
        var second = parent.Boxes[3];
        var third = parent.Boxes[4];
        var outer = Wrap(parent, first, second, third);
        Wrap(outer, second, third);

        var seen = parent.SelectorChildren;
        Assert.Equal(30, seen.Count);
        Assert.Equal<ICssDomNode>([first, second, third], seen.Skip(2).Take(3));
    }

    [Fact]
    public void ThePerParentAnswerIsKept_SoTheSiblingIndexCanBeKeptForIt()
    {
        var parent = Parent(40);
        Wrap(parent, parent.Boxes[4], parent.Boxes[5]);

        var seen = parent.SelectorChildren;
        Assert.Same(seen, parent.SelectorChildren);

        // A flattened list is a List<CssBox>, which is what SiblingIndex keys on, so a parent with a wrapper is indexed like any other.
        var index = SiblingIndex.For(seen, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(index);
        Assert.Equal(40, index!.Elements.Count);
        Assert.Same(index, SiblingIndex.For(parent.SelectorChildren, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AddingAChild_RetiresTheKeptAnswer()
    {
        var parent = Parent(40);
        Wrap(parent, parent.Boxes[4], parent.Boxes[5]);
        var before = parent.SelectorChildren;

        var added = Element(parent, "tr");

        var after = parent.SelectorChildren;
        Assert.NotSame(before, after);
        Assert.Equal(41, after.Count);
        Assert.Same(added, after[^1]);
    }

    [Fact]
    public void ABoxThatLaterFillsAnEmptyAnonymousBox_IsSeenThroughWithoutTheParentChanging()
    {
        var parent = Parent(40);
        var empty = new CssBox(parent, null);
        var other = parent.Boxes[2];
        Assert.Same(parent.Boxes, parent.SelectorChildren);

        // The same size and the same first and last child: only the anonymous box's own content changes.
        parent.Boxes.Remove(other);
        empty.Boxes.Add(other);
        parent.Boxes.Insert(10, Element(null!, "tr"));

        var seen = parent.SelectorChildren;
        Assert.DoesNotContain(empty, seen);
        Assert.Contains(other, seen);
    }
}
