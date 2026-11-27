using System.Linq;
using PeachBrackets.Text;
using System;
using System.Collections.Generic;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Fragments;
using PeachBrackets.Updates;
using PeachDrawing.Core;

namespace PeachBrackets;

public sealed partial class HtmlDocument
{
    /// <summary>
    /// Raised when a scroll container of a document shown on a screen is scrolled by <see cref="SetScrollPosition"/>, <see cref="ScrollBy"/> or
    /// <see cref="ScrollIntoView"/>: the element, and where its content is now (CSSOM View's <c>scrollLeft</c> and <c>scrollTop</c>).
    /// </summary>
    public event EventHandler<ScrollPositionChangedEventArgs>? ScrollPositionChanged;

    /// <summary>
    /// Whether <paramref name="element"/> is a scroll container: on a document shown on a screen, a box whose <c>overflow</c> is <c>auto</c>, <c>scroll</c> or <c>hidden</c>
    /// (CSS Overflow 3 §2). Its content is clipped to its padding box and moves as it is scrolled. A paged document has no scroll containers, and the root
    /// and the body, whose overflow belongs to the viewport, are not one either (the host scrolls the surface itself).
    /// </summary>
    /// <param name="element">the element to ask about</param>
    public bool IsScrollContainer(DocumentElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        ThrowIfDisposed();
        return _container.View.IsScrollContainer(_container, element.Box);
    }

    /// <summary>Where a scroll container is scrolled to and how far it can go, or null when <paramref name="element"/> is not a scroll container (see <see cref="IsScrollContainer"/>).</summary>
    /// <param name="element">the scroll container</param>
    public ScrollMetrics? GetScrollMetrics(DocumentElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        ThrowIfDisposed();

        if (_container.View.ScrollStateOf(_container, element.Box) is not { } state)
            return null;

        var ppp = _unitsPerPoint;
        return new ScrollMetrics(state.X / ppp, state.Y / ppp, state.MaxX / ppp, state.MaxY / ppp,
            new Rect(state.Port.X / ppp, state.Port.Y / ppp, state.Port.Width / ppp, state.Port.Height / ppp), state.ContentWidth / ppp, state.ContentHeight / ppp, state.MinX / ppp, state.MinY / ppp);
    }

    /// <summary>
    /// Scrolls a scroll container to (<paramref name="x"/>, <paramref name="y"/>) points from the start of its content, kept within what it can scroll (CSSOM View
    /// <c>scrollTo</c>). Nothing is relaid out: the regions returned are the scrollport to paint again.
    /// </summary>
    /// <param name="element">the scroll container</param>
    /// <param name="x">how far to scroll to the left; <see cref="double.NaN"/> leaves that axis where it is</param>
    /// <param name="y">how far to scroll up; <see cref="double.NaN"/> leaves that axis where it is</param>
    /// <returns>the regions that look different, which is none when the element is not a scroll container or was already there</returns>
    /// <param name="behavior">how it gets there: by default as the container's <c>scroll-behavior</c> says (<c>smooth</c> glides, and the result is empty until the glide is stepped by
    /// <see cref="AdvanceAnimations(TimeSpan)"/>), and settled on a snap position when the container has <c>scroll-snap-type</c></param>
    public IReadOnlyList<DirtyRegion> SetScrollPosition(DocumentElement element, double x, double y, ScrollBehavior behavior = ScrollBehavior.Auto)
    {
        ArgumentNullException.ThrowIfNull(element);
        ThrowIfDisposed();

        var ppp = _unitsPerPoint;
        if (_container.View.Navigate(_container, element.Box, double.IsNaN(x) ? double.NaN : x * ppp, double.IsNaN(y) ? double.NaN : y * ppp, snap: true, (int)behavior) is not { } port)
            return [];

        return AfterScroll(element, port);
    }

    /// <summary>
    /// Settles a scroll container that has stopped moving on the nearest snap position (CSS Scroll Snap 1 §6): what a host calls when a dragged thumb is let go or a finger is lifted,
    /// which move the content without snapping while they are held. Nothing moves when the container does not snap.
    /// </summary>
    /// <param name="element">the scroll container</param>
    /// <param name="behavior">how it gets there; by default as the container's <c>scroll-behavior</c> says</param>
    public IReadOnlyList<DirtyRegion> SettleScroll(DocumentElement element, ScrollBehavior behavior = ScrollBehavior.Auto)
    {
        ArgumentNullException.ThrowIfNull(element);
        ThrowIfDisposed();
        return _container.View.Settle(_container, element.Box, (int)behavior) is { } port ? AfterScroll(element, port) : [];
    }

    /// <summary>
    /// Scrolls a container by a distance in points as a finger or a dragged thumb moves it: at once and without snapping, which wait for <see cref="SettleScroll"/> when it is let go.
    /// </summary>
    public IReadOnlyList<DirtyRegion> DragScrollBy(DocumentElement element, double dx, double dy)
    {
        ArgumentNullException.ThrowIfNull(element);
        ThrowIfDisposed();
        var offset = _container.View.ScrollOffsetOf(element.Box);
        var ppp = _unitsPerPoint;
        return ScrollExactly(element, (offset.X / ppp) + dx, (offset.Y / ppp) + dy);
    }

    /// <summary>The place and size of the element that is the <paramref name="ordinal"/>-th element of the source, in CSS pixels, or null.</summary>
    internal (double X, double Y, double Width, double Height)? ProbeBounds(int ordinal)
    {
        _ = Pages;
        if (FindByOrdinal(_container.Root, ordinal) is not { } box)
            return null;

        box = PeachBrackets.Html.Utils.DomUtils.ResolveGeometryBox(box);
        var rect = PeachBrackets.Html.Utils.CommonUtils.GetFirstValueOrDefault(box.Rectangles, box.Bounds);
        var points = PeachBrackets.Utilities.Utils.ToPoints(rect, _unitsPerPoint);
        const double pointsPerPixel = 0.75;
        return (points.X / pointsPerPixel, points.Y / pointsPerPixel, points.Width / pointsPerPixel, points.Height / pointsPerPixel);
    }

    /// <summary>
    /// The box of the <paramref name="ordinal"/>-th element as a whole (the union of what it takes on each line, when it breaks) in CSS pixels from the top left of the page, and the same with the element's
    /// own CSS transform applied (its bounding box).
    /// </summary>
    internal ((double X, double Y, double Width, double Height) Box, (double X, double Y, double Width, double Height) Transformed)? ProbeBoxes(int ordinal)
    {
        _ = Pages;
        if (FindByOrdinal(_container.Root, ordinal) is not { } found)
            return null;

        var box = PeachBrackets.Html.Utils.DomUtils.ResolveGeometryBox(found);
        Rect rect = box.Bounds;
        if (box.Rectangles.Count > 0)
        {
            double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
            foreach (var part in box.Rectangles.Values)
            {
                left = Math.Min(left, part.Left);
                top = Math.Min(top, part.Top);
                right = Math.Max(right, part.Right);
                bottom = Math.Max(bottom, part.Bottom);
            }

            rect = new Rect(left, top, right - left, bottom - top);
        }

        var moved = rect;
        if (box.HasTransform)
        {
            var matrix = PeachDrawing.Core.Matrix3x2Extensions.RebaseOrigin(box.ActualTransformMatrix, rect.X, rect.Y);
            var corners = new[] { (rect.Left, rect.Top), (rect.Right, rect.Top), (rect.Right, rect.Bottom), (rect.Left, rect.Bottom) }
                .Select(c => System.Numerics.Vector2.Transform(new System.Numerics.Vector2((float)c.Item1, (float)c.Item2), matrix)).ToArray();
            var minX = corners.Min(c => c.X);
            var minY = corners.Min(c => c.Y);
            moved = new Rect(minX, minY, corners.Max(c => c.X) - minX, corners.Max(c => c.Y) - minY);
        }

        const double pointsPerPixel = 0.75;
        (double X, double Y, double Width, double Height) InPixels(Rect r)
        {
            var points = PeachBrackets.Utilities.Utils.ToPoints(r, _unitsPerPoint);
            return (points.X / pointsPerPixel, points.Y / pointsPerPixel, points.Width / pointsPerPixel, points.Height / pointsPerPixel);
        }

        return (InPixels(rect), InPixels(moved));
    }

    /// <summary>
    /// Draws the element that is the <paramref name="ordinal"/>-th element of the source, as it is laid out here, onto a new surface of its own size (one device pixel to a CSS pixel) and gives
    /// the picture. What overlaps the element on the page is drawn with it. Null when there is no such element or it has no area to draw.
    /// </summary>
    internal async System.Threading.Tasks.ValueTask<(PeachDrawing.Core.Image Image, double Width, double Height)?> ProbeRenderAsync(int ordinal, Func<int, int, PeachDrawing.Core.OffscreenSurface?> makeSurface, IReadOnlyDictionary<int, PeachBrackets.ScriptHosting.ScriptCanvasPainting>? canvasPaintings = null)
    {
        _ = Pages;
        if (FindByOrdinal(_container.Root, ordinal) is not { } found)
            return null;

        var box = PeachBrackets.Html.Utils.DomUtils.ResolveGeometryBox(found);
        if (_container.HideCrossOriginImages && _container.Root is { } documentRoot)
            await RenderSameOriginFramesAsync(documentRoot, found, makeSurface);

        _container.ProbeRenderRoot = found;

        // What is above the element is not in its picture: it keeps no background, border or shadow of its own while the picture is drawn.
        var suppressed = new List<(CssBox Box, bool Background, bool Border)>();
        for (var above = found.ParentBox; above is not null; above = above.ParentBox)
        {
            suppressed.Add((above, above.SuppressOwnBackgroundPaint, above.SuppressOwnBorderPaint));
            above.SuppressOwnBackgroundPaint = true;
            above.SuppressOwnBorderPaint = true;
        }
        _container.CanvasPaintings.Clear();
        if (canvasPaintings is not null)
        {
            foreach (var (key, painting) in canvasPaintings)
                _container.CanvasPaintings[key] = painting;
        }

        var rect = PeachBrackets.Html.Utils.CommonUtils.GetFirstValueOrDefault(box.Rectangles, box.Bounds);
        var layoutPerPixel = _unitsPerPoint * 0.75;
        var (width, height) = (rect.Width / layoutPerPixel, rect.Height / layoutPerPixel);
        if (!(width > 0) || !(height > 0) || width > 16384 || height > 16384)
            return null;

        var (pixelsWide, pixelsHigh) = ((int)Math.Ceiling(width - 1e-6), (int)Math.Ceiling(height - 1e-6));
        using var surface = makeSurface(Math.Max(1, pixelsWide), Math.Max(1, pixelsHigh));
        if (surface is null)
            return null;

        // The page's layout units become pixels, and the element's corner the picture's.
        var canvas = surface.Canvas;
        canvas.PushTransform(System.Numerics.Matrix3x2.CreateTranslation((float)-rect.X, (float)-rect.Y) * System.Numerics.Matrix3x2.CreateScale((float)(1 / layoutPerPixel)));
        try
        {
            var region = new Rect(rect.X / _unitsPerPoint, rect.Y / _unitsPerPoint, rect.Width / _unitsPerPoint, rect.Height / _unitsPerPoint);
            await PaintPageRegionAsync(0, canvas, region);
        }
        finally
        {
            canvas.PopTransform();
            _container.ProbeRenderRoot = null;
            _container.CanvasPaintings.Clear();
            foreach (var (above, background, border) in suppressed)
            {
                above.SuppressOwnBackgroundPaint = background;
                above.SuppressOwnBorderPaint = border;
            }
        }

        return (surface.CreateSnapshot(), width, height);
    }

    // A browsing context is not embedded in a page, but a picture of an element shows an iframe that is of the document's own origin (and is not sandboxed away from it) as the frame's
    // page drawn at the frame's size: what is inside is then as readable to the script as the element it is in.
    private async System.Threading.Tasks.ValueTask RenderSameOriginFramesAsync(CssBox box, CssBox within, Func<int, int, PeachDrawing.Core.OffscreenSurface?> makeSurface)
    {
        if (box is CssBoxFrame { HtmlTag: { } tag } frame && IsInside(frame, within)
            && tag.TryGetAttribute("src") is { Length: > 0 } src && !tag.HasAttribute("sandbox")
            && PeachBrackets.Html.Utils.CommonUtils.ResolveAgainstDocumentBase(_container, src) is { IsAbsoluteUri: true } address
            && _container.RenderContext.BaseUri is { IsAbsoluteUri: true } documentAddress
            && address.Scheme is "http" or "https" && string.Equals(address.Scheme, documentAddress.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(address.Host, documentAddress.Host, StringComparison.OrdinalIgnoreCase) && address.Port == documentAddress.Port)
        {
            try
            {
                var response = await _container.RenderContext.GetResourceStream(address);
                var sandboxed = response?.ResponseHeaders is { } headers && headers.Any(h => h.Key.Equals("Content-Security-Policy", StringComparison.OrdinalIgnoreCase) && h.Value.Any(v => v.Contains("sandbox", StringComparison.OrdinalIgnoreCase)));
                if (response?.ResourceStream is { } stream && !sandboxed)
                {
                    string html;
                    using (var reader = new System.IO.StreamReader(stream))
                        html = await reader.ReadToEndAsync();

                    var rect = frame.Bounds;
                    var (width, height) = (rect.Width / _unitsPerPoint / 0.75, rect.Height / _unitsPerPoint / 0.75);
                    if (width >= 1 && height >= 1 && _options is { } options)
                    {
                        var frameOptions = options.CloneWithoutScripts();
                        frameOptions.PageWidthPoints = width * 0.75;
                        frameOptions.ViewportHeightPoints = height * 0.75;
                        frameOptions.HideCrossOriginImages = true;
                        using var inner = await LoadAsync("<base href=\"" + System.Net.WebUtility.HtmlEncode(address.AbsoluteUri) + "\"><style>html { min-height: 100vh }</style>" + html, _container.RenderContext, frameOptions);
                        using var measuring = makeSurface(1, 1);
                        if (measuring is not null)
                        {
                            await inner.LayoutAsync(measuring.Canvas);
                            using var surface = makeSurface(Math.Max(1, (int)Math.Ceiling(width)), Math.Max(1, (int)Math.Ceiling(height)));
                            if (surface is not null)
                            {
                                surface.Canvas.PushTransform(System.Numerics.Matrix3x2.CreateScale(1f / 0.75f));
                                // The page's own background (what the body's propagates to) is part of what the frame shows, so the page is painted whole, clipped to the frame.
                                surface.Canvas.PushClip(new Rect(0, 0, width * 0.75, height * 0.75));
                                await inner.PaintPageAsync(0, surface.Canvas);
                                surface.Canvas.PopClip();
                                surface.Canvas.PopTransform();
                                frame.ImageWord.Image = surface.CreateSnapshot();
                            }
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not PeachBrackets.Html.Utils.SynchronousOnlyViolationException)
            {
                // A frame that cannot be read is a frame that is not drawn.
            }
        }

        foreach (var child in box.Boxes)
            await RenderSameOriginFramesAsync(child, within, makeSurface);
    }

    private static bool IsInside(CssBox box, CssBox ancestor)
    {
        for (var current = box; current is not null; current = current.ParentBox)
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }

        return false;
    }

    // The text runs that lie in the element that is the ordinal-th of the source, in the order they are in the document.
    private List<TextRun> RunsIn(int ordinal)
    {
        var result = new List<TextRun>();
        if (FindByOrdinal(_container.Root, ordinal) is not { } element)
            return result;

        foreach (var run in TextRuns)
        {
            for (var owner = run.Owner; owner is not null; owner = owner.ParentBox)
            {
                if (ReferenceEquals(owner, element))
                {
                    result.Add(run);
                    break;
                }
            }
        }

        return result;
    }

    // Where a character index of a run is along it: the offset of the boundary at or after it.
    private static double XAt(TextRun run, int index)
    {
        var boundaries = run.CharacterBoundaries;
        // The offsets are by UTF-16 index, the boundaries a list of the indices that are positions (fewer, where a surrogate pair or a cluster is).
        for (var k = 0; k < boundaries.Count; k++)
        {
            if (boundaries[k] >= index && boundaries[k] < run.CharacterOffsets.Count)
                return run.CharacterOffsets[boundaries[k]];
        }

        return run.CharacterOffsets[^1];
    }

    /// <summary>The rectangles the characters from <paramref name="from"/> to <paramref name="to"/> of the text of the <paramref name="ordinal"/>-th element take, one for each stretch of a line, in CSS pixels.</summary>
    internal IReadOnlyList<(double X, double Y, double Width, double Height)> ProbeTextRects(int ordinal, int from, int to)
    {
        _ = Pages;
        var rects = new List<(double X, double Y, double Right, double Bottom)>();
        foreach (var run in RunsIn(ordinal))
        {
            if (run.CharacterOffsets.Count == 0)
                continue;

            // Each word of the run knows where its characters are in the text they came from.
            foreach (var slice in run.Slices)
            {
                var source = slice.Word.SourceStart;
                if (source < 0)
                    continue;

                var (start, end) = (Math.Max(from, source), Math.Min(to, source + slice.Length));
                if (end <= start)
                    continue;

                // The word's left edge is the run's first offset of the word less where that edge is from the word's left.
                var first = XAt(run, slice.TextStart);
                var wordLeft = first - slice.Edges[0];
                var (a, b) = (wordLeft + slice.Edges[start - source], wordLeft + slice.Edges[end - source]);
                var (left, right) = (Math.Min(a, b), Math.Max(a, b));
                var (top, bottom) = (run.Bounds.Y, run.Bounds.Y + run.Bounds.Height);

                rects.Add((left, top, right, bottom));

                // The space before the word is covered when the range includes it.
                if (slice.HasGap && from <= source - 1 && source - 1 < to)
                    rects.Add((Math.Min(wordLeft + slice.GapStart, wordLeft + slice.GapEnd), top, Math.Max(wordLeft + slice.GapStart, wordLeft + slice.GapEnd), bottom));
            }
        }

        // An empty range is a box with no width where the place is.
        if (rects.Count == 0 && from >= to)
        {
            foreach (var run in RunsIn(ordinal))
            {
                foreach (var slice in run.Slices)
                {
                    var source = slice.Word.SourceStart;
                    if (source < 0 || to < source || to > source + slice.Length)
                        continue;

                    var x = XAt(run, slice.TextStart) - slice.Edges[0] + slice.Edges[to - source];
                    rects.Add((x, run.Bounds.Y, x, run.Bounds.Y + run.Bounds.Height));
                    break;
                }

                if (rects.Count > 0)
                    break;
            }
        }

        // The stretches that touch on one line are one rectangle, left to right.
        rects.Sort((p, q) => Math.Abs(p.Y - q.Y) < 0.5 ? p.X.CompareTo(q.X) : p.Y.CompareTo(q.Y));
        var merged = new List<(double X, double Y, double Right, double Bottom)>();
        foreach (var rect in rects)
        {
            if (merged.Count > 0 && Math.Abs(merged[^1].Y - rect.Y) < 0.5 && rect.X <= merged[^1].Right + 0.5)
                merged[^1] = (merged[^1].X, merged[^1].Y, Math.Max(merged[^1].Right, rect.Right), Math.Max(merged[^1].Bottom, rect.Bottom));
            else
                merged.Add(rect);
        }

        rects = merged;

        const double pointsPerPixel = 0.75;
        return [.. rects.Select(r => (r.X / pointsPerPixel, r.Y / pointsPerPixel, (r.Right - r.X) / pointsPerPixel, (r.Bottom - r.Y) / pointsPerPixel))];
    }

    /// <summary>The place between two characters nearest a point (in CSS pixels): the ordinal of the element that holds the text, and how many characters of that text come before the place.</summary>
    internal (int Ordinal, int Offset)? ProbeCaretAt(double x, double y)
    {
        _ = Pages;
        if (Pages.Count == 0 || GetCaretPosition(0, x * 0.75, y * 0.75) is not { } position || position.RunIndex < 0 || position.RunIndex >= TextRuns.Count)
            return null;

        var run = TextRuns[position.RunIndex];
        for (var owner = run.Owner; owner is not null; owner = owner.ParentBox)
        {
            if (owner.HtmlTag is not { } tag)
                continue;

            // The word the place is in names where it is in the source text.
            var slice = run.Slices.FirstOrDefault(s => s.Word.SourceStart >= 0 && position.CharIndex >= s.TextStart && position.CharIndex <= s.TextStart + s.Length);
            return slice is null ? null : (tag.SourceOrdinal, slice.Word.SourceStart + position.CharIndex - slice.TextStart);
        }

        return null;
    }

    /// <summary>The computed value of a property of the element that is the <paramref name="ordinal"/>-th element of the source, or null.</summary>
    internal string? ProbeComputedValue(int ordinal, string property)
    {
        _ = Pages;
        return FindByOrdinal(_container.Root, ordinal) is { } box ? PeachBrackets.Html.Utils.CssUtils.GetPropertyValue(box, property) : null;
    }

    // Moves the content where a drag put it, with no snapping and no glide.
    private IReadOnlyList<DirtyRegion> ScrollExactly(DocumentElement element, double x, double y)
    {
        var ppp = _unitsPerPoint;
        return _container.View.SetScroll(_container, element.Box, double.IsNaN(x) ? double.NaN : x * ppp, double.IsNaN(y) ? double.NaN : y * ppp) is { } port
            ? AfterScroll(element, port)
            : [];
    }

    // The glides of containers with scroll-behavior: smooth, moved on to the time of a frame; the regions to paint and whether any still goes.
    private (IReadOnlyList<DirtyRegion> Regions, bool Running) StepScrollTweens(TimeSpan now)
    {
        if (!_container.View.HasTweens)
            return ([], false);

        var (moved, running) = _container.View.StepTweens(_container, now.TotalSeconds);
        var regions = new List<DirtyRegion>();
        foreach (var (scroller, port) in moved)
            regions.AddRange(AfterScroll(new DocumentElement(scroller), port));

        return (regions, running);
    }

    // A frame of the clock with the glides of the scroll containers added to it.
    private AnimationFrame WithScrollTweens(AnimationFrame frame, TimeSpan now)
    {
        var (regions, running) = StepScrollTweens(now);
        if (regions.Count == 0 && !running)
            return frame;

        var combined = new List<DirtyRegion>(frame.DirtyRegions);
        combined.AddRange(regions);
        return frame with { IsRunning = frame.IsRunning || running, DirtyRegions = combined };
    }

    /// <summary>Scrolls a scroll container by (<paramref name="dx"/>, <paramref name="dy"/>) points (CSSOM View <c>scrollBy</c>); see <see cref="SetScrollPosition"/>.</summary>
    /// <param name="element">the scroll container</param>
    /// <param name="dx">how far to scroll to the left, in points</param>
    /// <param name="dy">how far to scroll up, in points</param>
    /// <param name="behavior">how it gets there; see <see cref="SetScrollPosition"/></param>
    public IReadOnlyList<DirtyRegion> ScrollBy(DocumentElement element, double dx, double dy, ScrollBehavior behavior = ScrollBehavior.Auto)
    {
        ArgumentNullException.ThrowIfNull(element);
        ThrowIfDisposed();

        var offset = _container.View.ScrollOffsetOf(element.Box);
        var ppp = _unitsPerPoint;
        return SetScrollPosition(element, (offset.X / ppp) + dx, (offset.Y / ppp) + dy, behavior);
    }

    /// <summary>
    /// The innermost scroll container at a point of a page, for a host that sends a wheel turn or a touch drag to what is under the pointer. When a direction is
    /// given (<paramref name="dx"/>, <paramref name="dy"/>: the way the content would move, positive scrolling toward the end), a container that cannot scroll that way is
    /// passed over for the next one out, as scroll chaining does; null when none can, and the host scrolls the surface itself.
    /// </summary>
    /// <param name="pageIndex">the page</param>
    /// <param name="x">the point's distance from the left of the content area, in points</param>
    /// <param name="y">the point's distance from the top of the content area, in points</param>
    /// <param name="dx">the horizontal direction the content is to scroll in, or 0 for either</param>
    /// <param name="dy">the vertical direction, or 0 for either</param>
    public DocumentElement? GetScrollContainerAt(int pageIndex, double x, double y, double dx = 0, double dy = 0)
    {
        ThrowIfDisposed();
        if (pageIndex != 0 || !_container.View.HasScrollContainers(_container))
            return null;

        var ppp = _unitsPerPoint;
        var found = _container.View.ScrollContainerAt(_container, x * ppp, y * ppp, dx, dy);
        return found is null ? null : new DocumentElement(found);
    }

    /// <summary>
    /// Scrolls every scroll container that holds <paramref name="element"/> as little as it takes to bring it into view (CSSOM View <c>scrollIntoView</c> with
    /// <c>nearest</c> alignment), outermost first. The element's <c>scroll-margin</c> is kept clear at the edges. Nothing is relaid out.
    /// </summary>
    /// <param name="element">the element to show</param>
    /// <returns>the regions that look different</returns>
    /// <param name="behavior">how each container gets there; see <see cref="SetScrollPosition"/></param>
    /// <param name="block">where the element comes to along the block axis (vertical in horizontal text): <see cref="ScrollAlignment.Nearest"/> moves as little as it takes</param>
    /// <param name="inline">where the element comes to along the inline axis</param>
    public IReadOnlyList<DirtyRegion> ScrollIntoView(DocumentElement element, ScrollBehavior behavior = ScrollBehavior.Auto,
        ScrollAlignment block = ScrollAlignment.Nearest, ScrollAlignment inline = ScrollAlignment.Nearest)
    {
        ArgumentNullException.ThrowIfNull(element);
        ThrowIfDisposed();

        var regions = new List<DirtyRegion>();
        foreach (var (scroller, port) in _container.View.ScrollIntoView(_container, element.Box, (int)behavior, (int)block, (int)inline))
            regions.AddRange(AfterScroll(new DocumentElement(scroller), port));

        return regions;
    }

    /// <summary>
    /// Scrolls every scroll container that holds a piece of text so the first line of <paramref name="range"/> shows: what a find bar or a selection made by script calls to
    /// bring a match into view. Nothing moves when the text is in no scroll container, or is in view already (with <see cref="ScrollAlignment.Nearest"/>).
    /// </summary>
    /// <param name="range">a range of <see cref="TextRuns"/>, as <see cref="FindText"/> returns it</param>
    /// <param name="behavior">how each container gets there; see <see cref="SetScrollPosition"/></param>
    /// <param name="block">where the text comes to along the block axis</param>
    /// <param name="inline">where the text comes to along the inline axis</param>
    /// <returns>the regions that look different</returns>
    public IReadOnlyList<DirtyRegion> ScrollTextIntoView(TextRange range, ScrollBehavior behavior = ScrollBehavior.Auto,
        ScrollAlignment block = ScrollAlignment.Nearest, ScrollAlignment inline = ScrollAlignment.Nearest)
    {
        ThrowIfDisposed();
        var runs = TextRuns;
        if (range.Start.RunIndex < 0 || range.Start.RunIndex >= runs.Count || runs[range.Start.RunIndex].Owner is not { } owner)
            return [];

        var rects = GetSelectionRects(range.Start, range.End);
        if (rects.Count == 0 || _container.FragmentTree is not { Fragmentainers.Count: > 0 } tree)
            return [];

        // The text is on the one surface of a screen document. Its rectangle is where it is drawn, in points of the page; the container works in layout units before anything
        // scrolls or sticks, so it goes back through the page's geometry and the displacement of the box that holds it.
        var geometry = tree.Fragmentainers[Math.Clamp(runs[range.Start.RunIndex].PageIndex, 0, tree.Fragmentainers.Count - 1)].Geometry;
        var ppp = _unitsPerPoint;
        var (dx, dy) = _container.View.DisplacementOf(owner);
        var drawn = rects[0];
        var layout = new Rect(((drawn.X - geometry.ContentLeftPt) * ppp) + _container.MarginLeft - dx, ((drawn.Y - geometry.ContentTopPt) * ppp) + _container.MarginTop - dy,
            drawn.Width * ppp, drawn.Height * ppp);

        var regions = new List<DirtyRegion>();
        foreach (var (scroller, port) in _container.View.ScrollIntoView(_container, owner, (int)behavior, (int)block, (int)inline, layout))
            regions.AddRange(AfterScroll(new DocumentElement(scroller), port));

        return regions;
    }

    /// <summary>
    /// The scrollbar under a point of a page, if one is: the container it belongs to, the axis, which part of it the point is on, and the track and thumb along the axis
    /// in points, so a host can start a drag on the thumb (<see cref="ScrollThumbTo"/>) or page the container by a press on the track (<see cref="ScrollByPage"/>).
    /// </summary>
    /// <param name="pageIndex">the page</param>
    /// <param name="x">the point's distance from the left of the content area, in points</param>
    /// <param name="y">the point's distance from the top of the content area, in points</param>
    public ScrollbarHit? GetScrollbarAt(int pageIndex, double x, double y)
    {
        ThrowIfDisposed();
        if (pageIndex != 0 || !_container.View.HasScrollContainers(_container))
            return null;

        var ppp = _unitsPerPoint;
        if (_container.View.ScrollbarAt(_container, x * ppp, y * ppp) is not { } target)
            return null;

        var vertical = target.Vertical;
        var part = target.Part switch { < 0 => ScrollbarPart.TrackBefore, > 0 => ScrollbarPart.TrackAfter, _ => ScrollbarPart.Thumb };
        return new ScrollbarHit(new DocumentElement(target.Box), vertical ? ScrollbarAxis.Vertical : ScrollbarAxis.Horizontal, part,
            (vertical ? target.Track.Y : target.Track.X) / ppp, (vertical ? target.Track.Height : target.Track.Width) / ppp,
            (vertical ? target.Thumb.Y : target.Thumb.X) / ppp, (vertical ? target.Thumb.Height : target.Thumb.Width) / ppp);
    }

    /// <summary>
    /// Scrolls a container so that its scrollbar thumb starts <paramref name="thumbStart"/> points from the top (or left) of the page: what dragging the thumb does. The
    /// thumb is kept on its track.
    /// </summary>
    /// <param name="element">the scroll container</param>
    /// <param name="axis">the scrollbar to drag</param>
    /// <param name="thumbStart">where the thumb's leading edge is to be, in points from the top or left of the content area</param>
    /// <returns>the regions to paint again</returns>
    public IReadOnlyList<DirtyRegion> ScrollThumbTo(DocumentElement element, ScrollbarAxis axis, double thumbStart)
    {
        ArgumentNullException.ThrowIfNull(element);
        ThrowIfDisposed();

        if (_container.View.ScrollStateOf(_container, element.Box) is not { } state)
            return [];

        var ppp = _unitsPerPoint;
        var (vertical, horizontal) = Html.ScrollbarGeometry.Of(element.Box, state, ppp);
        if ((axis == ScrollbarAxis.Vertical ? vertical : horizontal) is not { } bar)
            return [];

        // Where the container is drawn: it may itself be carried by a container around it.
        var (shiftX, shiftY) = _container.View.DisplacementOf(element.Box);
        var isVertical = axis == ScrollbarAxis.Vertical;
        var trackStart = (isVertical ? bar.Track.Y + shiftY : bar.Track.X + shiftX) / ppp;
        var travel = ((isVertical ? bar.Track.Height - bar.Thumb.Height : bar.Track.Width - bar.Thumb.Width) / ppp);
        if (!(travel > 0))
            return [];

        var fraction = Math.Clamp((thumbStart - trackStart) / travel, 0, 1);
        var max = (isVertical ? state.MaxY : state.MaxX) / ppp;
        var min = (isVertical ? state.MinY : state.MinX) / ppp;
        var position = min + (fraction * (max - min));
        return isVertical ? ScrollExactly(element, double.NaN, position) : ScrollExactly(element, position, double.NaN);
    }

    /// <summary>Scrolls a container by a page along one axis (seven eighths of its scrollport, as a press on the track of a scrollbar does).</summary>
    /// <param name="element">the scroll container</param>
    /// <param name="axis">the axis</param>
    /// <param name="direction">positive toward the end of the content, negative toward the start</param>
    /// <returns>the regions to paint again</returns>
    public IReadOnlyList<DirtyRegion> ScrollByPage(DocumentElement element, ScrollbarAxis axis, int direction)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (GetScrollMetrics(element) is not { } metrics)
            return [];

        var page = (axis == ScrollbarAxis.Vertical ? metrics.Viewport.Height : metrics.Viewport.Width) * 0.875 * Math.Sign(direction);
        return axis == ScrollbarAxis.Vertical ? ScrollBy(element, 0, page) : ScrollBy(element, page, 0);
    }

    /// <summary>
    /// The scroll container that holds <paramref name="element"/> (the element itself when it is one) and can scroll the way asked, nearest first: where a key goes when
    /// the focus is in <paramref name="element"/>. Null when none can, and the host scrolls the surface.
    /// </summary>
    /// <param name="element">the element that has the focus</param>
    /// <param name="dx">the horizontal direction the content is to scroll in, or 0</param>
    /// <param name="dy">the vertical direction, or 0</param>
    public DocumentElement? GetScrollContainerFor(DocumentElement element, double dx = 0, double dy = 0)
    {
        ArgumentNullException.ThrowIfNull(element);
        ThrowIfDisposed();
        return _container.View.ScrollContainerFor(_container, element.Box, dx, dy) is { } found ? new DocumentElement(found) : null;
    }

    /// <summary>The element of a node of the accessibility tree, or null when the node is not an element.</summary>
    /// <param name="node">the node</param>
    public DocumentElement? GetElementOf(PeachBrackets.Accessibility.AccessibilityNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        ThrowIfDisposed();
        return node.Id.Length >= 2 && node.Id[0] == 'e'
            && int.TryParse(node.Id.AsSpan(1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var ordinal)
            && FindByOrdinal(_container.Root, ordinal) is { } box
                ? new DocumentElement(box)
                : null;
    }

    // Scrolling moved what a scroll container holds, so what was recorded for the old places (text runs, link rectangles) is stale.
    private IReadOnlyList<DirtyRegion> AfterScroll(DocumentElement element, Rect port)
    {
        _textRuns = null;
        _annotations = null;
        _container.AnimationClock?.NotifyViewportChanged();

        var ppp = _unitsPerPoint;
        const double bleed = 2;
        var region = new DirtyRegion(0, new Rect((port.X / ppp) - bleed, (port.Y / ppp) - bleed, (port.Width / ppp) + (2 * bleed), (port.Height / ppp) + (2 * bleed)));

        if (ScrollPositionChanged is { } handler && GetScrollMetrics(element) is { } metrics)
            handler(this, new ScrollPositionChangedEventArgs(element, metrics.X, metrics.Y));

        return [region];
    }
}

/// <summary>Where a scroll container is scrolled to and how far it can go, in points (CSS Overflow 3 §2).</summary>
/// <param name="X">how far the content is scrolled to the left (<c>scrollLeft</c>)</param>
/// <param name="Y">how far the content is scrolled up (<c>scrollTop</c>)</param>
/// <param name="MaxX">the most it can be scrolled to the left; zero when its content fits across</param>
/// <param name="MaxY">the most it can be scrolled up; zero when its content fits down</param>
/// <param name="MinX">the most it can be scrolled the other way: zero, or negative where the content starts at the right and runs on to the left (right-to-left text, vertical-rl)</param>
/// <param name="MinY">the same along y</param>
/// <param name="Viewport">the scrollport (the container's padding box), from the top-left of the content area</param>
/// <param name="ContentWidth">the width of the scrollable overflow area (<c>scrollWidth</c>)</param>
/// <param name="ContentHeight">the height of the scrollable overflow area (<c>scrollHeight</c>)</param>
public readonly record struct ScrollMetrics(double X, double Y, double MaxX, double MaxY, Rect Viewport, double ContentWidth, double ContentHeight, double MinX = 0, double MinY = 0);

/// <summary>Where an element is brought to in a scroll container's scrollport by <see cref="HtmlDocument.ScrollIntoView"/> (CSSOM View's <c>ScrollLogicalPosition</c>).</summary>
public enum ScrollAlignment
{
    /// <summary>As little as it takes to show it; nothing when it is in view already.</summary>
    Nearest,

    /// <summary>Its start edge at the start edge of the scrollport.</summary>
    Start,

    /// <summary>Its middle at the middle of the scrollport.</summary>
    Center,

    /// <summary>Its end edge at the end edge of the scrollport.</summary>
    End,
}

/// <summary>How a programmatic scroll of a scroll container gets to its position (CSSOM View's <c>ScrollBehavior</c>).</summary>
public enum ScrollBehavior
{
    /// <summary>As the container's <c>scroll-behavior</c> property says: <c>smooth</c> glides, <c>auto</c> jumps.</summary>
    Auto,

    /// <summary>At once, whatever the property says.</summary>
    Instant,

    /// <summary>Gliding, whatever the property says (unless the user asks for reduced motion).</summary>
    Smooth,
}

/// <summary>Which scrollbar of a scroll container.</summary>
public enum ScrollbarAxis
{
    /// <summary>The bar along the right edge, which scrolls the content up and down.</summary>
    Vertical,

    /// <summary>The bar along the bottom edge, which scrolls the content from side to side.</summary>
    Horizontal,
}

/// <summary>The part of a scrollbar a point is on.</summary>
public enum ScrollbarPart
{
    /// <summary>The thumb, which a drag moves.</summary>
    Thumb,

    /// <summary>The track before the thumb: a press scrolls a page toward the start.</summary>
    TrackBefore,

    /// <summary>The track after the thumb: a press scrolls a page toward the end.</summary>
    TrackAfter,
}

/// <summary>A scrollbar under a point (<see cref="HtmlDocument.GetScrollbarAt"/>); positions are along the bar's axis, in points from the top or left of the content area.</summary>
/// <param name="Element">the scroll container the bar belongs to</param>
/// <param name="Axis">which of its bars</param>
/// <param name="Part">the part the point is on</param>
/// <param name="TrackStart">where the track starts</param>
/// <param name="TrackLength">how long the track is</param>
/// <param name="ThumbStart">where the thumb starts</param>
/// <param name="ThumbLength">how long the thumb is</param>
public sealed record ScrollbarHit(DocumentElement Element, ScrollbarAxis Axis, ScrollbarPart Part, double TrackStart, double TrackLength, double ThumbStart, double ThumbLength);

/// <summary>What <see cref="HtmlDocument.ScrollPositionChanged"/> reports.</summary>
public sealed class ScrollPositionChangedEventArgs : EventArgs
{
    internal ScrollPositionChangedEventArgs(DocumentElement element, double x, double y)
    {
        Element = element;
        X = x;
        Y = y;
    }

    /// <summary>The scroll container that was scrolled.</summary>
    public DocumentElement Element { get; }

    /// <summary>How far its content is scrolled to the left now, in points.</summary>
    public double X { get; }

    /// <summary>How far its content is scrolled up now, in points.</summary>
    public double Y { get; }
}
