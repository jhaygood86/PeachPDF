using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration;

/// <summary>
/// CSS 2.1 §9.5.1: right floats that fit side by side sit side by side, each as far right as it can go (rule 9) without overlapping the
/// outer edge of another (rules 2 and 3). The lookup that finds what a float being placed overlaps also reports one that merely lies
/// to its right, which is the line flow's question; with three floats it sent the third back and forth between two blockers for ever.
/// </summary>
public class RightFloatsSideBySideTests
{
    [Fact]
    public async Task ThreeRightFloatsThatFit_SitSideBySideAndLayoutFinishes()
    {
        var html = LayoutHarness.Wrap(
            "<div style='width:260pt'>" +
            "<div id='a' style='float:right;width:83pt;height:26pt'></div>" +
            "<div id='b' style='float:right;width:69pt;height:121pt'></div>" +
            "<div id='c' style='float:right;width:76pt;height:43pt'></div></div>");

        // Before the fix this never returned.
        var (root, _) = await Task.Run(() => LayoutHarness.LayoutAsync(html, pageHeight: 400))
            .WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        var a = LayoutHarness.FindById(root, "a")!;
        var b = LayoutHarness.FindById(root, "b")!;
        var c = LayoutHarness.FindById(root, "c")!;

        // Each lies to the left of the one placed before it, on the same row: 83 + 69 + 76 = 228 of 260.
        Assert.Equal(a.Location.Y, b.Location.Y, 1);
        Assert.Equal(a.Location.Y, c.Location.Y, 1);
        Assert.True(b.ActualRight <= a.Location.X + 0.5, $"b ends at {b.ActualRight}, a starts at {a.Location.X}");
        Assert.True(c.ActualRight <= b.Location.X + 0.5, $"c ends at {c.ActualRight}, b starts at {b.Location.X}");
    }

    // A fourth that no longer fits goes below the tallest blocker it would overlap (rule 3 and §9.5's wrap), and still finishes.
    [Fact]
    public async Task FourthRightFloatThatDoesNotFit_GoesBelowAndLayoutFinishes()
    {
        var html = LayoutHarness.Wrap(
            "<div style='width:260pt'>" +
            "<div id='a' style='float:right;width:83pt;height:26pt'></div>" +
            "<div id='b' style='float:right;width:69pt;height:121pt'></div>" +
            "<div id='c' style='float:right;width:76pt;height:43pt'></div>" +
            "<div id='d' style='float:right;width:60pt;height:20pt'></div></div>");

        var (root, _) = await Task.Run(() => LayoutHarness.LayoutAsync(html, pageHeight: 400))
            .WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        var d = LayoutHarness.FindById(root, "d")!;
        var a = LayoutHarness.FindById(root, "a")!;

        Assert.True(d.Location.Y >= a.ActualBottom - 0.5, $"d is at y={d.Location.Y}, beside a (which ends at {a.ActualBottom})");
    }
}