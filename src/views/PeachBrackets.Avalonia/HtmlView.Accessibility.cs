using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using PeachBrackets.Accessibility;
using PeachBrackets.Avalonia.Logic;
using PeachBrackets.Updates;

namespace PeachBrackets.Avalonia;

// The accessibility tree of the document for assistive technology: the automation peer tree is built from it, and what changed between two builds is told to the peer.
public sealed partial class HtmlView
{
    private AccessibilityTree? _accessibilityTree;
    private HtmlViewAutomationPeer? _automationPeer;

    /// <summary>The tree the automation peers are built from; null until a document has laid out.</summary>
    internal AccessibilityTree? AccessibilityTree => _accessibilityTree;

    /// <summary>What changed in the last refresh of the tree, for the tests: null when there was no tree before it.</summary>
    internal AccessibilityTreeDiff? LastAccessibilityDiff { get; private set; }

    /// <summary>The context the document was laid out with, so a test can change the document through its own API.</summary>
    internal HtmlViewRenderContext? RenderContext => _context;

    /// <inheritdoc />
    protected override AutomationPeer OnCreateAutomationPeer() => _automationPeer = new HtmlViewAutomationPeer(this);

    // Called when a document has laid out or changed: the tree a screen reader walks is built again from it.
    private void RefreshAccessibility(HtmlDocument document)
    {
        var before = _accessibilityTree;
        var after = _accessibilityTree = document.GetAccessibilityTree();
        var diff = LastAccessibilityDiff = before is null ? null : AccessibilityTreeDiff.Compare(before, after);

        // Nothing at all is told when the tree is the same.
        if (diff is { IsEmpty: true } || _automationPeer is not { } peer)
            return;

        PostToUi(() => peer.Refresh(before, after, diff));
    }

    // The view scrolled or zoomed, so every node is somewhere else on the screen.
    private void OnScrolledForAccessibility() => _automationPeer?.RaiseBoundsChanged();

    /// <summary>Where a node is, in the coordinates the view's own peer reports, given where the view is.</summary>
    internal Rect NodeBounds(AccessibilityNode node, Rect viewBounds)
    {
        // A node that crosses a page break is on more than one page; it is reported where it starts.
        if (node.Bounds.Count == 0 || (uint)node.Bounds[0].PageIndex >= (uint)_pageTops.Length)
            return default;

        var (pageIndex, bounds) = node.Bounds[0];
        var x = (bounds.X * PixelsPerPoint * _zoom) - (_scrollX / _dpi);
        var y = ((_pageTops[pageIndex] + (bounds.Y * PixelsPerPoint)) * _zoom) - (_scrollY / _dpi);
        return new Rect(viewBounds.X + x, viewBounds.Y + y, bounds.Width * PixelsPerPoint * _zoom, bounds.Height * PixelsPerPoint * _zoom);
    }

    /// <summary>Whether a node lies outside what the view shows.</summary>
    internal bool IsOffscreen(AccessibilityNode node)
    {
        // An auto-height view shows all of itself; whether the list it is in shows it is for the list to say.
        if (IsAutoHeight)
            return false;

        var rect = NodeBounds(node, default);
        return rect.Bottom < 0 || rect.Top > Bounds.Height || rect.Right < 0 || rect.Left > Bounds.Width;
    }

    /// <summary>The node as the document describes it now: the tree is built again after a control changes, so a peer's own node can be an old one.</summary>
    internal AccessibilityNode CurrentNode(AccessibilityNode node) => _document?.GetAccessibilityTree().FindById(node.Id) ?? node;

    /// <summary>The text a field holds, for the value pattern.</summary>
    internal string? FieldValue(AccessibilityNode node) =>
        _document is { } document && node.ElementId is { } id && document.GetElement(id) is { } element ? document.GetFormState(element)?.Value : null;

    /// <summary>Scrolls a node into view when a screen reader moves to it.</summary>
    internal void BringIntoView(AccessibilityNode node)
    {
        if (node.Bounds.Count == 0 || (uint)node.Bounds[0].PageIndex >= (uint)_pageTops.Length)
            return;

        var (pageIndex, bounds) = node.Bounds[0];
        ScrollToDocumentY(Math.Max(0, _pageTops[pageIndex] + (bounds.Y * PixelsPerPoint) - 40 / _zoom));
    }

    /// <summary>Moves the page's focus to a node a screen reader asked for, and brings the view to it.</summary>
    internal void FocusFromAutomation(AccessibilityNode node)
    {
        if (_document is not { } document || document.GetElementOf(node) is not { } element)
            return;

        Focus();
        QueueFocus(() => FocusElementAsync(document, element));
    }

    /// <summary>Does what a click on a node does - follows a link, presses a button - at the centre of where it starts.</summary>
    internal void InvokeFromAutomation(AccessibilityNode node)
    {
        if (node.Bounds.Count == 0)
            return;

        var (page, bounds) = node.Bounds[0];
        ClickAt(page, bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
    }

    /// <summary>Does what a click on a check box or radio button does, for a screen reader.</summary>
    internal bool ToggleFromAutomation(AccessibilityNode node)
    {
        if (_document is not { } document || document.GetElementOf(node) is not { } element)
            return false;

        var result = document.Activate(element);
        AfterAutomationChange(document, result);
        return result.Applied;
    }

    /// <summary>Sets what a text field or slider holds, as a screen reader does.</summary>
    internal bool SetValueFromAutomation(AccessibilityNode node, string value)
    {
        if (_document is not { } document || document.GetElementOf(node) is not { } element)
            return false;

        var result = document.SetValue(element, value);
        AfterAutomationChange(document, result);
        return result.Applied;
    }

    // A change made for a screen reader is shown and told like any other change of the document.
    private void AfterAutomationChange(HtmlDocument document, UpdateResult result)
    {
        ApplyUpdate(result);
        if (document.NeedsRestyle)
            _ = RestyleForAutomationAsync(document);
    }

    private async Task RestyleForAutomationAsync(HtmlDocument document)
    {
        if (_context is not { } context || !ReferenceEquals(document, _document))
            return;

        using var measuring = new MeasuringCanvas(context);
        var result = await document.RestyleFormAsync(measuring.Canvas);
        if (ReferenceEquals(document, _document))
            ApplyUpdate(result);
    }

    /// <summary>Where a scroll container of the document is scrolled to, when the node's element is one.</summary>
    internal ScrollMetrics? ScrollMetricsOf(AccessibilityNode node) =>
        _document is { } document && document.GetElementOf(node) is { } element ? document.GetScrollMetrics(element) : null;

    // A screen reader scrolls a container by a small step (an arrow key's) or a large one (a page).
    internal void ScrollFromAutomation(AccessibilityNode node, global::Avalonia.Automation.Provider.ScrollAmount horizontal, global::Avalonia.Automation.Provider.ScrollAmount vertical)
    {
        if (_document is not { } document || document.GetElementOf(node) is not { } element || document.GetScrollMetrics(element) is not { } metrics)
            return;

        static double Step(global::Avalonia.Automation.Provider.ScrollAmount amount, double port) => amount switch
        {
            global::Avalonia.Automation.Provider.ScrollAmount.LargeIncrement => port * 0.875,
            global::Avalonia.Automation.Provider.ScrollAmount.LargeDecrement => -port * 0.875,
            global::Avalonia.Automation.Provider.ScrollAmount.SmallIncrement => 40,
            global::Avalonia.Automation.Provider.ScrollAmount.SmallDecrement => -40,
            _ => 0,
        };

        InvalidateRegions(document.ScrollBy(element, Step(horizontal, metrics.Viewport.Width), Step(vertical, metrics.Viewport.Height)));
    }

    // A screen reader scrolls a container to a percentage of its range; a negative percentage leaves the axis where it is.
    internal void ScrollToPercentFromAutomation(AccessibilityNode node, double horizontalPercent, double verticalPercent)
    {
        if (_document is not { } document || document.GetElementOf(node) is not { } element || document.GetScrollMetrics(element) is not { } metrics)
            return;

        var x = horizontalPercent < 0 ? double.NaN : metrics.MinX + ((metrics.MaxX - metrics.MinX) * Math.Clamp(horizontalPercent, 0, 100) / 100);
        var y = verticalPercent < 0 ? double.NaN : metrics.MinY + ((metrics.MaxY - metrics.MinY) * Math.Clamp(verticalPercent, 0, 100) / 100);
        InvalidateRegions(document.SetScrollPosition(element, x, y));
    }

    /// <summary>The scroll position of the whole document as percentages, and the part of it the view shows, for the view's own scroll pattern.</summary>
    internal (double X, double Y, double ViewWidth, double ViewHeight, bool CanX, bool CanY) DocumentScroll()
    {
        var maxX = (_contentWidth * Scale) - ViewportWidthPx;
        var maxY = (_contentHeight * Scale) - ViewportHeightPx;
        var contentW = _contentWidth * Scale;
        var contentH = _contentHeight * Scale;
        return (
            maxX > 0 ? _scrollX / maxX * 100 : -1,
            maxY > 0 ? _scrollY / maxY * 100 : -1,
            contentW > 0 ? Math.Min(100, ViewportWidthPx / contentW * 100) : 100,
            contentH > 0 ? Math.Min(100, ViewportHeightPx / contentH * 100) : 100,
            maxX > 0,
            maxY > 0);
    }

    /// <summary>Scrolls the whole document from a screen reader, by a step or a page.</summary>
    internal void ScrollDocumentFromAutomation(global::Avalonia.Automation.Provider.ScrollAmount horizontal, global::Avalonia.Automation.Provider.ScrollAmount vertical)
    {
        double Step(global::Avalonia.Automation.Provider.ScrollAmount amount, double port) => amount switch
        {
            global::Avalonia.Automation.Provider.ScrollAmount.LargeIncrement => port * 0.875,
            global::Avalonia.Automation.Provider.ScrollAmount.LargeDecrement => -port * 0.875,
            global::Avalonia.Automation.Provider.ScrollAmount.SmallIncrement => 40 * _dpi,
            global::Avalonia.Automation.Provider.ScrollAmount.SmallDecrement => -40 * _dpi,
            _ => 0,
        };

        ScrollTo(_scrollX + Step(horizontal, ViewportWidthPx), _scrollY + Step(vertical, ViewportHeightPx));
    }

    /// <summary>Scrolls the whole document to a percentage of its range from a screen reader; a negative percentage leaves the axis where it is.</summary>
    internal void ScrollDocumentToPercentFromAutomation(double horizontalPercent, double verticalPercent)
    {
        var maxX = Math.Max(0, (_contentWidth * Scale) - ViewportWidthPx);
        var maxY = Math.Max(0, (_contentHeight * Scale) - ViewportHeightPx);
        ScrollTo(horizontalPercent < 0 ? _scrollX : maxX * Math.Clamp(horizontalPercent, 0, 100) / 100, verticalPercent < 0 ? _scrollY : maxY * Math.Clamp(verticalPercent, 0, 100) / 100);
    }
}
