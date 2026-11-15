using Avalonia.Automation.Provider;
using PeachBrackets.Accessibility;
using PeachBrackets.Avalonia.Tests.TestSupport;

namespace PeachBrackets.Avalonia.Tests;

/// <summary>What the view does when a screen reader asks for something that is not there to do: no document, a node that is not in it, a node that is not a scroll container.</summary>
public class AutomationGuardTests
{
    private const string Page =
        "<html><body style='margin:0'><p id='p'>Some text</p>" +
        "<div id='box' role='region' aria-label='box' tabindex='0' style='width:100px;height:100px;overflow:auto'><div style='height:600px;width:300px'>tall</div></div>" +
        "<input id='f' type='text' value='x'></body></html>";

    private static AccessibilityNode NodeOf(HtmlView view, string elementId) =>
        view.Document!.GetAccessibilityTree().Nodes.First(n => n.ElementId == elementId);

    [Fact]
    public async Task RequestsForAViewWithNoDocumentChangeNothingAndFindNothing()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, source) = await HeadlessSession.ShowAsync(Page, 400, 300);
            var box = NodeOf(source, "box");
            var field = NodeOf(source, "f");

            // A view that never loaded anything: a node of another document is not in it.
            var empty = new HtmlView();
            Assert.False(empty.ToggleFromAutomation(field));
            Assert.False(empty.SetValueFromAutomation(field, "y"));
            Assert.Null(empty.FieldValue(field));
            Assert.Null(empty.ScrollMetricsOf(box));
            Assert.Same(box, empty.CurrentNode(box));
            empty.FocusFromAutomation(field);
            empty.ScrollFromAutomation(box, ScrollAmount.LargeIncrement, ScrollAmount.LargeIncrement);
            empty.ScrollToPercentFromAutomation(box, 50, 50);
            Assert.False(empty.HasOpenFlyout);
            window.Close();
        });
    }

    [Fact]
    public async Task ANodeThatIsNotAScrollContainerOrHasNoPlaceIsLeftAlone()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Page, 400, 300);
            var paragraph = NodeOf(view, "p");
            Assert.Null(view.ScrollMetricsOf(paragraph));

            var before = view.ScrollPixels;
            view.ScrollFromAutomation(paragraph, ScrollAmount.LargeIncrement, ScrollAmount.SmallIncrement);
            view.ScrollToPercentFromAutomation(paragraph, 50, 50);
            Assert.Equal(before, view.ScrollPixels);

            // The root of the tree is not anywhere on a page: bringing it into view or invoking it does nothing, and it has no bounds.
            var root = view.Document!.GetAccessibilityTree().Root;
            Assert.Empty(root.Bounds);
            Assert.Equal(default, view.NodeBounds(root, default));
            view.BringIntoView(root);
            view.InvokeFromAutomation(root);
            Assert.Equal(before, view.ScrollPixels);
            window.Close();
        });
    }

    [Fact]
    public async Task AScreenReaderScrollsAContainerByStepsPagesAndPercentages()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Page, 400, 300);
            var box = NodeOf(view, "box");
            var start = view.ScrollMetricsOf(box);
            Assert.NotNull(start);
            Assert.Equal(0, start!.Value.Y);

            view.ScrollFromAutomation(box, ScrollAmount.NoAmount, ScrollAmount.SmallIncrement);
            var small = view.ScrollMetricsOf(box)!.Value;
            Assert.Equal(40, small.Y, 1);
            Assert.Equal(0, small.X, 1);

            view.ScrollFromAutomation(box, ScrollAmount.SmallIncrement, ScrollAmount.SmallDecrement);
            var back = view.ScrollMetricsOf(box)!.Value;
            Assert.Equal(0, back.Y, 1);
            Assert.Equal(40, back.X, 1);

            view.ScrollFromAutomation(box, ScrollAmount.NoAmount, ScrollAmount.LargeIncrement);
            var page = view.ScrollMetricsOf(box)!.Value;
            Assert.Equal(page.Viewport.Height * 0.875, page.Y, 1);

            view.ScrollFromAutomation(box, ScrollAmount.SmallDecrement, ScrollAmount.LargeDecrement);
            Assert.Equal(0, view.ScrollMetricsOf(box)!.Value.Y, 1);
            Assert.Equal(0, view.ScrollMetricsOf(box)!.Value.X, 1);

            // A percentage is of the range; a negative one leaves its axis where it is.
            view.ScrollToPercentFromAutomation(box, -1, 50);
            var half = view.ScrollMetricsOf(box)!.Value;
            Assert.Equal(half.MaxY / 2, half.Y, 1);
            Assert.Equal(0, half.X, 1);
            view.ScrollToPercentFromAutomation(box, 100, -1);
            var right = view.ScrollMetricsOf(box)!.Value;
            Assert.Equal(right.MaxX, right.X, 1);
            Assert.Equal(half.Y, right.Y, 1);
            window.Close();
        });
    }

    [Fact]
    public async Task TheWholeDocumentIsScrolledByStepsPagesAndPercentagesToo()
    {
        await HeadlessSession.Run(async () =>
        {
            var html = "<html><body style='margin:0'><div style='height:3000px;width:900px'>tall</div></body></html>";
            var (window, view) = await HeadlessSession.ShowAsync(html, 400, 300);
            var (_, startY, _, _, canX, canY) = view.DocumentScroll();
            Assert.True(canY);
            Assert.Equal(0, startY);

            view.ScrollDocumentFromAutomation(ScrollAmount.NoAmount, ScrollAmount.SmallIncrement);
            var small = view.ScrollPixels.Y;
            Assert.True(small > 0);

            view.ScrollDocumentFromAutomation(ScrollAmount.NoAmount, ScrollAmount.LargeIncrement);
            var large = view.ScrollPixels.Y - small;
            Assert.True(large > small, $"a page ({large}) is more than a step ({small})");

            view.ScrollDocumentFromAutomation(ScrollAmount.NoAmount, ScrollAmount.LargeDecrement);
            view.ScrollDocumentFromAutomation(ScrollAmount.NoAmount, ScrollAmount.SmallDecrement);
            Assert.Equal(0, view.ScrollPixels.Y, 1);

            if (canX)
            {
                view.ScrollDocumentFromAutomation(ScrollAmount.SmallIncrement, ScrollAmount.NoAmount);
                Assert.True(view.ScrollPixels.X > 0);
            }

            view.ScrollDocumentToPercentFromAutomation(-1, 100);
            var end = view.DocumentScroll();
            Assert.Equal(100, end.Y, 1);
            window.Close();
        });
    }
}
