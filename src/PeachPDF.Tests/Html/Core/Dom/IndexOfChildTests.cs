using PeachPDF.Tests.TestSupport;
using PeachBrackets.Html.Dom;

namespace PeachPDF.Tests.Html.Core.Dom;

/// <summary>
/// <see cref="CssBox.IndexOfChild"/> answers what <c>Boxes.IndexOf</c> would, whatever its remembered position is.
/// </summary>
public class IndexOfChildTests
{
    private static CssBox Parent(int children)
    {
        var parent = new CssBox(null, null);
        for (var i = 0; i < children; i++)
        {
            _ = new CssBox(parent, null);
        }

        return parent;
    }

    [Fact]
    public void ChildrenAskedForInOrderAreFound()
    {
        var parent = Parent(40);

        for (var i = 0; i < parent.Boxes.Count; i++)
            Assert.Equal(i, parent.IndexOfChild(parent.Boxes[i]));
    }

    [Fact]
    public void ChildrenAskedForInReverseOrderAreFound()
    {
        var parent = Parent(40);

        for (var i = parent.Boxes.Count - 1; i >= 0; i--)
            Assert.Equal(i, parent.IndexOfChild(parent.Boxes[i]));
    }

    [Fact]
    public void EveryOtherChildAndDistantJumpsAreFound()
    {
        var parent = Parent(100);

        for (var i = 0; i < parent.Boxes.Count; i += 2)
            Assert.Equal(i, parent.IndexOfChild(parent.Boxes[i]));

        Assert.Equal(97, parent.IndexOfChild(parent.Boxes[97]));
        Assert.Equal(3, parent.IndexOfChild(parent.Boxes[3]));
        Assert.Equal(60, parent.IndexOfChild(parent.Boxes[60]));
    }

    [Fact]
    public void ABoxThatIsNotAChildIsNotFound_AndTheNextLookupStillWorks()
    {
        var parent = Parent(10);

        Assert.Equal(7, parent.IndexOfChild(parent.Boxes[7]));
        Assert.Equal(-1, parent.IndexOfChild(new CssBox(null, null)));
        Assert.Equal(7, parent.IndexOfChild(parent.Boxes[7]));
    }

    [Fact]
    public void AParentWithNoChildrenFindsNothing()
    {
        Assert.Equal(-1, Parent(0).IndexOfChild(new CssBox(null, null)));
    }

    [Fact]
    public void ARemovedChildMovesTheOthersAndIsStillFound()
    {
        var parent = Parent(12);
        var last = parent.Boxes[11];
        Assert.Equal(11, parent.IndexOfChild(last));

        parent.Boxes[3].ParentBox = null;

        Assert.Equal(10, parent.IndexOfChild(last));
    }
}

public class SpacingBoxFlagTests
{
    [Fact]
    public async Task OnlyTheRowsBelowARowspanHoldASpacingBox()
    {
        var (root, _) = await PeachPDF.Tests.TestSupport.LayoutHarness.LayoutAsync(PeachPDF.Tests.TestSupport.LayoutHarness.Wrap(
            "<table><tr><td rowspan='2'>a</td><td>b</td></tr><tr><td>c</td></tr><tr><td>d</td><td>e</td></tr></table>"));

        var rows = new List<CssBox>();
        void Collect(CssBox box)
        {
            if (box.HtmlTag?.Name == "tr") rows.Add(box);
            foreach (var child in box.Boxes) Collect(child);
        }
        Collect(root);

        Assert.Equal(3, rows.Count);
        Assert.All(rows, row => Assert.Equal(row.Boxes.Any(b => b.GetType().Name == "CssSpacingBox"), row.HoldsSpacingBox));
        Assert.True(rows[1].HoldsSpacingBox);
        Assert.False(rows[0].HoldsSpacingBox);
        Assert.False(rows[2].HoldsSpacingBox);
    }
}
