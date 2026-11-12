using PeachBrackets.Annotations;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Fragments;
using PeachBrackets.Html.Handlers;
using PeachBrackets.Html.Utils;
using PeachDrawing.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace PeachBrackets;

public sealed partial class HtmlDocument
{
    // The document again with other <details> open or closed: what the page would look like if a reader changed one, for a format that can show
    // that when the reader does. Made on the first request after a layout, and dropped when the document is laid out again.
    private readonly List<HtmlDocument> _disclosureTwins = [];
    private IReadOnlyList<PageDisclosure>? _disclosures;

    // Each open details offered for collapsing costs a layout of the document of its own.
    private const int MaxCollapsibleDetails = 16;

    /// <summary>
    /// The <c>&lt;details&gt;</c> elements of the laid-out pages that a reader of the pages can open or close, each with where its summary is on
    /// its page and the content to show over the page when the reader changes it (<see cref="PaintDisclosureAsync"/>), so a format with a reader
    /// that can do that (a PDF with interactive forms) can offer to. Nothing moves: the content lies over what follows the summary.
    /// </summary>
    /// <remarks>
    /// A closed <c>details</c> is offered to be opened. An open one is offered to be collapsed when it sits on one page and holds no other
    /// <c>details</c>, up to a small number per document. A <c>details</c> inside a closed one is not listed (it is not there to click), nor is one
    /// with no content, one the layout gave no room, or one in a document built from a <see cref="PeachBrackets.Building.DeclarativePage"/>.
    /// </remarks>
    /// <param name="measuringCanvas">a canvas made by the same render context, used to measure text; nothing is drawn on it</param>
    /// <returns>the <c>details</c> that can be toggled: the open ones, then the closed ones, each in document order</returns>
    /// <exception cref="ArgumentNullException"><paramref name="measuringCanvas"/> is null</exception>
    /// <exception cref="InvalidOperationException">the document has not been laid out</exception>
    public async ValueTask<IReadOnlyList<PageDisclosure>> GetDisclosuresAsync(Canvas measuringCanvas)
    {
        ArgumentNullException.ThrowIfNull(measuringCanvas);
        _ = Pages;
        if (_disclosures is not null)
            return _disclosures;

        if (_source is null || _options is null || _container.Root is not { } root)
            return _disclosures = [];

        var closed = new List<CssBox>();
        CollectClosedDetails(root, closed);
        var open = new List<CssBox>();
        CollectOpenLeafDetails(root, open);
        if (closed.Count == 0 && open.Count == 0)
            return _disclosures = [];

        foreach (var old in _disclosureTwins)
            old.Dispose();

        _disclosureTwins.Clear();
        var found = new List<PageDisclosure>();
        var here = PlaceBoxes(this);

        if (closed.Count > 0)
        {
            var overrides = new Dictionary<int, Dictionary<string, string?>>();
            foreach (var box in closed)
                overrides[box.HtmlTag!.SourceOrdinal] = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["open"] = string.Empty };

            var twin = await LoadTwinAsync(overrides, measuringCanvas);
            var there = PlaceBoxes(twin);
            foreach (var box in closed)
            {
                var ordinal = box.HtmlTag!.SourceOrdinal;
                if (!here.TryGetValue(ordinal, out var summaryOn) || !there.TryGetValue(ordinal, out var openOn))
                    continue;

                // The details box itself is where a reader clicks and where the content goes; the twin's is how far down it would reach.
                var toggle = summaryOn.Bounds;
                var sheetHeight = Pages[summaryOn.PageIndex].HeightPoints;
                var twinSheetHeight = twin.Pages[openOn.PageIndex].HeightPoints;
                var height = Math.Min(Math.Min(openOn.Bounds.Height, sheetHeight - toggle.Y), twinSheetHeight - openOn.Bounds.Y);
                if (toggle.Width <= 0 || toggle.Height <= 0 || height <= toggle.Height + 0.5)
                    continue;

                found.Add(new PageDisclosure
                {
                    PageIndex = summaryOn.PageIndex,
                    ToggleBounds = toggle,
                    ContentBounds = new Rect(toggle.X, toggle.Y, toggle.Width, height),
                    AccessibleName = NameOf(box),
                    Twin = twin,
                    TwinPageIndex = openOn.PageIndex,
                    Source = new Rect(openOn.Bounds.X, openOn.Bounds.Y, toggle.Width, height),
                });
            }
        }

        // An open details collapses to what the page is with it closed: its heading, and the rest of the page as the closed layout has it.
        foreach (var box in open.Take(MaxCollapsibleDetails))
        {
            var ordinal = box.HtmlTag!.SourceOrdinal;
            if (!here.TryGetValue(ordinal, out var openOn) || !openOn.OnePage || _container.FragmentTree?.Fragmentainers is not { } fragmentainers)
                continue;

            var twin = await LoadTwinAsync(
                new Dictionary<int, Dictionary<string, string?>> { [ordinal] = new(StringComparer.OrdinalIgnoreCase) { ["open"] = null } }, measuringCanvas);
            if (!PlaceBoxes(twin).TryGetValue(ordinal, out var closedOn) || closedOn.PageIndex != openOn.PageIndex)
                continue;

            var geometry = fragmentainers[openOn.PageIndex].Geometry;
            var left = geometry.ContentLeftPt;
            var right = geometry.SheetWidthPt - (geometry.MarginRightPt + geometry.BorderRightPt + geometry.PaddingRightPt);
            var bottom = geometry.SheetHeightPt - (geometry.MarginBottomPt + geometry.BorderBottomPt + geometry.PaddingBottomPt);
            var toggle = new Rect(openOn.Bounds.X, openOn.Bounds.Y, openOn.Bounds.Width, closedOn.Bounds.Height);
            var bodyHeight = bottom - toggle.Bottom;
            if (toggle.Width <= 0 || toggle.Height <= 0 || bodyHeight <= 1 || right <= left)
                continue;

            found.Add(new PageDisclosure
            {
                PageIndex = openOn.PageIndex,
                InitiallyOpen = true,
                ToggleBounds = toggle,
                ContentBounds = new Rect(left, toggle.Bottom, right - left, bodyHeight),
                AccessibleName = NameOf(box),
                Twin = twin,
                TwinPageIndex = closedOn.PageIndex,
                Source = new Rect(left, closedOn.Bounds.Bottom, right - left, bodyHeight),
                HeadingSource = closedOn.Bounds,
            });
        }

        // Widgets later in the list are drawn over the earlier ones and the content a reader opens must cover the page the collapsed ones show, so the
        // collapsible open ones come first.
        return _disclosures = [.. found.OrderByDescending(d => d.InitiallyOpen)];
    }

    private string? NameOf(CssBox box) => GetAccessibilityTree().NodeFor(box)?.Name is { Length: > 0 } name ? name : null;

    /// <summary>
    /// Draws the content of a <see cref="PageDisclosure"/> that a reader shows over the page: for a closed <c>details</c> its opened state (the
    /// element, its summary and what it holds, with the page's background behind them); for one that starts open
    /// (<see cref="PageDisclosure.InitiallyOpen"/>) the rest of the page as it is with the <c>details</c> closed.
    /// </summary>
    /// <param name="disclosure">a value from <see cref="GetDisclosuresAsync"/> of this document</param>
    /// <param name="canvas">a canvas the size of <see cref="PageDisclosure.ContentBounds"/> in layout units, origin at its top-left corner</param>
    /// <returns>a task that completes when it is drawn</returns>
    /// <exception cref="ArgumentNullException">an argument is null</exception>
    /// <exception cref="InvalidOperationException"><paramref name="disclosure"/> is not from this document</exception>
    public ValueTask PaintDisclosureAsync(PageDisclosure disclosure, Canvas canvas)
    {
        ArgumentNullException.ThrowIfNull(disclosure);
        return PaintFromTwinAsync(disclosure, disclosure.Source, canvas);
    }

    /// <summary>
    /// Draws the heading of a <see cref="PageDisclosure"/> that starts open as it looks once it is closed (the <c>details</c> element with only its
    /// summary), to cover the open one.
    /// </summary>
    /// <param name="disclosure">a value from <see cref="GetDisclosuresAsync"/> of this document with <see cref="PageDisclosure.InitiallyOpen"/> set</param>
    /// <param name="canvas">a canvas the size of <see cref="PageDisclosure.ToggleBounds"/> in layout units, origin at its top-left corner</param>
    /// <returns>a task that completes when it is drawn</returns>
    /// <exception cref="ArgumentNullException">an argument is null</exception>
    /// <exception cref="InvalidOperationException"><paramref name="disclosure"/> is not from this document or does not start open</exception>
    public ValueTask PaintDisclosureHeadingAsync(PageDisclosure disclosure, Canvas canvas)
    {
        ArgumentNullException.ThrowIfNull(disclosure);
        if (!disclosure.InitiallyOpen)
            throw new InvalidOperationException("Only a disclosure that starts open has a heading to draw.");

        return PaintFromTwinAsync(disclosure, disclosure.HeadingSource, canvas);
    }

    private async ValueTask PaintFromTwinAsync(PageDisclosure disclosure, Rect source, Canvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (disclosure.Twin is not { } twin || _disclosures is null || !_disclosures.Contains(disclosure))
            throw new InvalidOperationException("The disclosure is not one of this document's.");

        var ppp = _unitsPerPoint;

        // Paper is white; the page's own background, if it has one, is drawn over it.
        canvas.DrawRectangle(canvas.GetSolidBrush(PaintColor.White), 0, 0, source.Width * ppp, source.Height * ppp);
        canvas.PushTransform(Matrix3x2.CreateTranslation((float)(-source.X * ppp), (float)(-source.Y * ppp)));
        try
        {
            await twin.PaintPageRegionAsync(disclosure.TwinPageIndex, canvas, source);
        }
        finally
        {
            canvas.PopTransform();
        }
    }

    // The document again with the given attribute changes, laid out; kept to be painted from and disposed with this document.
    private async ValueTask<HtmlDocument> LoadTwinAsync(IReadOnlyDictionary<int, Dictionary<string, string?>> overrides, Canvas measuringCanvas)
    {
        var twin = await LoadWithOverridesAsync(_source!, _renderContext, _options, overrides);
        try
        {
            await twin.LayoutAsync(measuringCanvas);
        }
        catch
        {
            twin.Dispose();
            throw;
        }

        _disclosureTwins.Add(twin);
        return twin;
    }

    // The closed details that are on a page: not inside the content of another closed one, which is not laid out.
    private static void CollectClosedDetails(CssBox box, List<CssBox> into)
    {
        if (box.HtmlTag is { SourceOrdinal: >= 0 } tag && tag.Name.Equals("details", StringComparison.OrdinalIgnoreCase) && !tag.HasAttribute("open"))
        {
            into.Add(box);
            return;
        }

        foreach (var child in box.Boxes)
            CollectClosedDetails(child, into);
    }

    // The open details that hold no other details and are not inside one: the ones whose collapsed page is simple.
    private static void CollectOpenLeafDetails(CssBox box, List<CssBox> into)
    {
        if (box.HtmlTag is { SourceOrdinal: >= 0 } tag && tag.Name.Equals("details", StringComparison.OrdinalIgnoreCase))
        {
            if (tag.HasAttribute("open") && !ContainsDetails(box))
                into.Add(box);

            return;
        }

        foreach (var child in box.Boxes)
            CollectOpenLeafDetails(child, into);
    }

    private static bool ContainsDetails(CssBox box)
    {
        foreach (var child in box.Boxes)
        {
            if ((child.HtmlTag is { } tag && tag.Name.Equals("details", StringComparison.OrdinalIgnoreCase)) || ContainsDetails(child))
                return true;
        }

        return false;
    }

    private readonly record struct Placed(int PageIndex, Rect Bounds, bool OnePage);

    // Where each details element is, by its place in the source: the page of its first box and the box in points from that page's top-left corner.
    private static Dictionary<int, Placed> PlaceBoxes(HtmlDocument document)
    {
        var placed = new Dictionary<int, Placed>();
        var container = document._container;
        if (container.Root is not { } root || container.FragmentTree?.Fragmentainers is not { } fragmentainers)
            return placed;

        var ppp = document._unitsPerPoint;
        var (slotToPage, _) = PageAnchorResolver.BuildSlotToPageMap(fragmentainers);

        void Walk(CssBox box)
        {
            if (box.HtmlTag is { SourceOrdinal: >= 0 } tag && tag.Name.Equals("details", StringComparison.OrdinalIgnoreCase)
                && box.DerivedStyle.ActualDisplay != PeachBrackets.Css.Keywords.None)
            {
                var pixelRect = CommonUtils.GetFirstValueOrDefault(box.Rectangles, box.Bounds);
                var slot = Math.Max(container.PageIndexOf(pixelRect.Top), 0);
                if (slotToPage.TryGetValue(slot, out var pageIndex) && pixelRect.Width > 0 && pixelRect.Height > 0)
                {
                    // Read off the fragmentainer itself, as the links and the form controls are, so this cannot disagree with what was painted.
                    var geometry = fragmentainers[pageIndex].Geometry;
                    var top = geometry.ContentTopPt + ((pixelRect.Top - container.PageTopOf(slot)) / ppp);
                    var left = geometry.ContentLeftPt + ((pixelRect.Left - container.MarginLeft) / ppp);
                    var onePage = container.PageIndexOf(pixelRect.Bottom - 1) == slot;
                    placed.TryAdd(tag.SourceOrdinal, new Placed(pageIndex, new Rect(left, top, pixelRect.Width / ppp, pixelRect.Height / ppp), onePage));
                }
            }

            foreach (var child in box.Boxes)
                Walk(child);
        }

        Walk(root);
        return placed;
    }
}

/// <summary>
/// A <c>&lt;details&gt;</c> of a laid-out page that a reader of the pages can open or close (<see cref="HtmlDocument.GetDisclosuresAsync"/>).
/// Geometry is in points from the page's top-left corner with y down, as <see cref="PageLink"/>'s is.
/// </summary>
public sealed class PageDisclosure
{
    /// <summary>The zero-based index of the page the <c>details</c> is on.</summary>
    public required int PageIndex { get; init; }

    /// <summary>
    /// What a reader clicks to change it: for a closed <c>details</c>, the element's box; for one that starts open, its box as it is when closed
    /// (the summary and what is around it), at the top of the open one.
    /// </summary>
    public required Rect ToggleBounds { get; init; }

    /// <summary>
    /// Where the content a reader shows covers the page. For a closed <c>details</c>: the same corner as <see cref="ToggleBounds"/>, as wide, and as
    /// tall as the opened <c>details</c> is (down to the bottom of the page). For one that starts open: the rest of the page's content area below
    /// <see cref="ToggleBounds"/>.
    /// </summary>
    public required Rect ContentBounds { get; init; }

    /// <summary>What the <c>details</c> is called to someone who cannot see it, or null when it has no name.</summary>
    public string? AccessibleName { get; init; }

    /// <summary>
    /// Whether the <c>details</c> starts open: it is drawn open on the page, and <see cref="ContentBounds"/> and the heading
    /// (<see cref="HtmlDocument.PaintDisclosureHeadingAsync"/>) are what a reader shows over it to collapse it, the page as the closed layout has it.
    /// </summary>
    public bool InitiallyOpen { get; init; }

    internal HtmlDocument? Twin { get; init; }

    internal int TwinPageIndex { get; init; }

    internal Rect Source { get; init; }

    internal Rect HeadingSource { get; init; }
}
