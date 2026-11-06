using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using PeachBrackets.Updates;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PeachBrackets.WinUI;

public sealed partial class HtmlView
{
    /// <summary>Identifies the <see cref="AnimationsEnabled"/> property.</summary>
    public static readonly DependencyProperty AnimationsEnabledProperty =
        DependencyProperty.Register(nameof(AnimationsEnabled), typeof(bool), typeof(HtmlView),
            new PropertyMetadata(false, (d, _) => ((HtmlView)d).OnOptionsChanged()));

    /// <summary>
    /// Whether the document plays its CSS animations, transitions and animated images (GIF, WebP). Off by default. A <c>transform</c> or <c>opacity</c>
    /// that animates is kept as a texture on the graphics device and only drawn again each frame, so it follows the display's refresh rate without repainting
    /// what is inside; other properties that cannot change layout (colours, shadows, filters) repaint the region they cover. A property that moves or resizes
    /// boxes (<c>width</c>, <c>margin</c>, <c>top</c>, <c>font-size</c>, flex and grid sizing) animates by laying the document out again each frame, so its cost
    /// follows the size of the document. Nothing plays when the system asks for reduced motion (Windows' "Animation effects" setting is off), and the document's
    /// <c>prefers-reduced-motion</c> reports it. Changing it loads the document again.
    /// </summary>
    public bool AnimationsEnabled
    {
        get => (bool)GetValue(AnimationsEnabledProperty);
        set => SetValue(AnimationsEnabledProperty, value);
    }

    /// <summary>
    /// Raised, on the UI thread, with the animation and transition events (<c>animationstart</c>, <c>transitionend</c> and the rest) that happened since the last frame, in
    /// order. The animations themselves can be looked at and controlled through <see cref="HtmlDocument.GetAnimations()"/> of <see cref="Document"/>.
    /// </summary>
    public event EventHandler<HtmlAnimationEventsEventArgs>? AnimationEvents;

    // Text is positioned to an eighth of a pixel (4 steps: half a step is at most an eighth) while the document plays.
    private const int AnimatingTextPositionSteps = 4;

    private bool _animating;
    private TimeSpan? _lastRendering;

    // The display's clock keeps running while the loop is stopped, but the animations must not: the time the document is stepped to leaves out what was idle, so
    // the first frame after a restart is one frame later than the last before the stop and not seconds.
    private TimeSpan _idleTime;
    private TimeSpan? _lastRealTime;
    private bool _resuming;

    // Whether this view plays animations: the host asked, and the system does not ask for reduced motion.
    private bool PlaysAnimations => AnimationsEnabled && HtmlViewLogic.SystemAllowsMotion(SystemAnimationsEnabled());

    /// <summary>Stands in for the system's animation-effects setting when set; for tests on a machine that has them off (a build agent).</summary>
    internal static bool? SystemAnimationsOverride { get; set; }

    private static bool SystemAnimationsEnabled()
    {
        if (SystemAnimationsOverride is { } forced)
            return forced;

        try
        {
            return new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
        }
        catch (Exception)
        {
            // No UI settings to ask (a service, a test host): motion is allowed.
            return true;
        }
    }

    // Steps the document on the display's own frame clock for as long as something plays, and not at all when nothing does: the handler is only attached
    // while an animation, a transition or an animated image is running.
    private void EnsureAnimationLoop()
    {
        if (_animating || !_loaded || _document is not { HasRunningAnimations: true })
            return;

        _animating = true;
        _lastRendering = null;

        // Text that moves a little each frame (layout animation) would need a mask and a device bitmap for every position it passes through; kept to coarser
        // positions while the document plays, there are few to make, which is what keeps the garbage collector's memory-pressure collections away.
        if (_context is not null) _context.TextPositionSteps = AnimatingTextPositionSteps;
        _resuming = _lastRealTime is not null;
        CompositionTarget.Rendering += OnRendering;
    }

    // The control API (play, seek, finish...) started a clock that had stopped, from any thread: the loop that steps it starts again on this one.
    private void OnAnimationsWoke(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, _document))
            return;

        var queue = DispatcherQueue;
        if (queue is null || queue.HasThreadAccess)
            EnsureAnimationLoop();
        else
            queue.TryEnqueue(EnsureAnimationLoop);
    }

    private void StopAnimationLoop()
    {
        if (!_animating)
            return;

        _animating = false;
        if (_context is not null) _context.TextPositionSteps = 64;
        CompositionTarget.Rendering -= OnRendering;
        _animationMeasuring?.Dispose();
        _animationMeasuring = null;
    }

    // What a frame that changes layout measures text on; one per run of the loop rather than one per frame.
    private PeachDrawing.Win2D.Win2dComposer? _animationMeasuring;

    // A frame whose layout step has not finished: the next one waits for it.
    private bool _stepping;

    private void OnRendering(object? sender, object args)
    {
        if (_document is not { } document || !_loaded || _context is null || (args as RenderingEventArgs) is not { } rendering)
        {
            StopAnimationLoop();
            return;
        }

        // A restyle in flight has dropped the layout the frame would be painted from.
        if (_restyling || _pumping || _stepping)
            return;

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var allocatedBefore = HtmlViewDiagnostics.Recording ? GC.GetAllocatedBytesForCurrentThread() : 0;

        var real = rendering.RenderingTime;
        if (_resuming && _lastRealTime is { } stoppedAt)
            _idleTime += real - stoppedAt;
        _resuming = false;
        _lastRealTime = real;
        var documentTime = real - _idleTime;

        _animationMeasuring ??= new PeachDrawing.Win2D.Win2dComposer(_context, 1, 1);

        // A property that moves or resizes boxes lays the document out again, which an asynchronous document may have to wait for: that frame finishes
        // when the layout does, with the painter leaving the surface alone until the new layout is in (see OnRegionsInvalidated).
        var step = document.AdvanceAnimationsAsync(documentTime, _animationMeasuring.Canvas);
        if (step.IsCompletedSuccessfully)
        {
            CompleteAnimationFrame(document, step.Result, documentTime, started, allocatedBefore);
            return;
        }

        _ = FinishAnimationFrameAsync(document, step, documentTime, started, allocatedBefore);
    }

    private async Task FinishAnimationFrameAsync(HtmlDocument document, ValueTask<AnimationFrame> step, TimeSpan time, long started, long allocatedBefore)
    {
        _stepping = true;
        _restyling = true;
        try
        {
            var frame = await step;
            if (ReferenceEquals(document, _document))
                CompleteAnimationFrame(document, frame, time, started, allocatedBefore);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            StopAnimationLoop();
            DocumentFailed?.Invoke(this, new HtmlLoadFailedEventArgs(exception));
        }
        finally
        {
            _restyling = false;
            _stepping = false;
        }
    }

    private void CompleteAnimationFrame(HtmlDocument document, AnimationFrame frame, TimeSpan time, long started, long allocatedBefore)
    {
        FrameCount++;

        // A layout step may have given the pages a new size, so the metrics are taken again; the regions are the places where something is not where it was
        // (or the pages whole, when they cannot be compared). Only they are painted again.
        if (frame.LayoutChanged)
            UpdateMetrics();

        InvalidateAnimated(frame.DirtyRegions);

        // The boxes drawn apart from the page changed where they are or how they look: the layer over the scroller is drawn again, and the surface under it is not.
        if (frame.OverlayChanged)
            _overlay.Invalidate();

        if (frame.Events.Count > 0)
            AnimationEvents?.Invoke(this, new HtmlAnimationEventsEventArgs(frame.Events));

        if (HtmlViewDiagnostics.Recording)
        {
            var interval = _lastRendering is { } last ? (time - last).TotalMilliseconds : 0;
            HtmlViewDiagnostics.AddAnimationFrame(interval, HtmlViewDiagnostics.MillisecondsSince(started), frame.DirtyRegions.Count, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
        }

        _lastRendering = time;
        if (!frame.IsRunning)
            StopAnimationLoop();
    }

    // The surface cuts what is invalidated into bands and hands each over to be painted separately, so a dozen small rectangles arrive as dozens of slivers,
    // each paying for a drawing session of its own. One rectangle per page that covers them all is painted in one go and costs far less than the slivers.
    private void InvalidateAnimated(IReadOnlyList<DirtyRegion> regions)
    {
        if (regions.Count <= 1 || _viewportMode)
        {
            foreach (var region in regions)
                InvalidateRegion(region.PageIndex, region.Bounds);

            return;
        }

        var pages = new Dictionary<int, PeachDrawing.Core.Rect>();
        foreach (var region in regions)
        {
            if (!pages.TryGetValue(region.PageIndex, out var bounds))
            {
                pages[region.PageIndex] = region.Bounds;
                continue;
            }

            var left = Math.Min(bounds.Left, region.Bounds.Left);
            var top = Math.Min(bounds.Top, region.Bounds.Top);
            pages[region.PageIndex] = new PeachDrawing.Core.Rect(left, top, Math.Max(bounds.Right, region.Bounds.Right) - left, Math.Max(bounds.Bottom, region.Bounds.Bottom) - top);
        }

        foreach (var (page, bounds) in pages)
            InvalidateRegion(page, bounds);
    }

    /// <summary>The number of animation frames this view has stepped. For tests and diagnostics.</summary>
    internal int FrameCount { get; private set; }
}

/// <summary>The events <see cref="HtmlView.AnimationEvents"/> reports.</summary>
public sealed class HtmlAnimationEventsEventArgs : EventArgs
{
    internal HtmlAnimationEventsEventArgs(IReadOnlyList<AnimationEventInfo> events) => Events = events;

    /// <summary>The events, in the order they happened.</summary>
    public IReadOnlyList<AnimationEventInfo> Events { get; }
}