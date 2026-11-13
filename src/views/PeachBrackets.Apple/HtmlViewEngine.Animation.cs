using PeachBrackets;
using PeachBrackets.Updates;
using PeachDrawing.CoreGraphics;

namespace PeachBrackets.Apple;

internal sealed partial class HtmlViewEngine
{
    // Whether this view plays animations: the host asked, and the system does not ask for reduced motion.
    private bool PlaysAnimations => host.AnimationsEnabled && host.SystemAllowsMotion;

    private IDisposable? _frameClock;
    private bool _animating;
    private bool _stepping;
    private CoreGraphicsComposer? _animationMeasuring;

    // The display's clock keeps running while the loop is stopped, but the animations must not: the time the document is stepped to leaves out what was idle, so
    // the first frame after a restart is one frame later than the last before the stop and not seconds.
    private TimeSpan _idleTime;
    private TimeSpan? _lastRealTime;
    private bool _resuming;

    /// <summary>The number of animation frames stepped since the view was made. For tests and diagnostics.</summary>
    public int FrameCount { get; private set; }

    /// <summary>The options that turn playing on, and what <c>prefers-reduced-motion</c> reports.</summary>
    private void ApplyAnimationOptions(HtmlDocumentOptions options)
    {
        if (!host.AnimationsEnabled)
            return;

        options.AnimationsEnabled = PlaysAnimations;
        options.Preferences = new UserPreferences { ReducedMotion = !host.SystemAllowsMotion };
    }

    // A document that plays is stepped on the display's own clock for as long as something runs, and not at all when nothing does.
    private void AdoptAnimations(HtmlDocument document)
    {
        document.AnimationsWoke += OnAnimationsWoke;
        EnsureAnimationLoop();
    }

    // The control API (play, seek, finish...) started a clock that had stopped, from any thread: the loop that steps it starts again on the main one.
    private void OnAnimationsWoke(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, _document))
            return;

        if (NSThread.IsMain)
            EnsureAnimationLoop();
        else
            NSRunLoop.Main.BeginInvokeOnMainThread(EnsureAnimationLoop);
    }

    private void EnsureAnimationLoop()
    {
        if (_animating || !_attached || _document is not { HasRunningAnimations: true })
            return;

        _animating = true;
        _resuming = _lastRealTime is not null;
        _frameClock = host.StartFrameClock(OnFrame);
    }

    private void StopAnimationLoop()
    {
        if (!_animating)
            return;

        _animating = false;
        _frameClock?.Dispose();
        _frameClock = null;
        _animationMeasuring?.Dispose();
        _animationMeasuring = null;
    }

    private void OnFrame(TimeSpan real)
    {
        if (_document is not { } document || _context is not { } context)
        {
            StopAnimationLoop();
            return;
        }

        // A state change or an earlier frame in flight has the layout; the frame would be painted from one that is not in yet.
        if (_pumping || _stepping)
            return;

        if (_resuming && _lastRealTime is { } stoppedAt)
            _idleTime += real - stoppedAt;
        _resuming = false;
        _lastRealTime = real;
        var documentTime = real - _idleTime;

        _animationMeasuring ??= new CoreGraphicsComposer(context, 1, 1, 1, 1);

        // A property that moves or resizes boxes lays the document out again, which may take a moment: that frame finishes when the layout does.
        var step = document.AdvanceAnimationsAsync(documentTime, _animationMeasuring.Canvas);
        if (step.IsCompletedSuccessfully)
        {
            CompleteAnimationFrame(document, step.Result);
            return;
        }

        _ = FinishAnimationFrameAsync(document, step, context);
    }

    private async Task FinishAnimationFrameAsync(HtmlDocument document, ValueTask<AnimationFrame> step, HtmlViewRenderContext context)
    {
        _stepping = true;
        BeginWork();
        try
        {
            var frame = await step;
            if (ReferenceEquals(document, _document))
                CompleteAnimationFrame(document, frame);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            StopAnimationLoop();
            host.LoadFailed(exception);
        }
        finally
        {
            _stepping = false;
            EndWork(context);

            // A hover that came while the layout step ran waits for it.
            if (_pendingPointer is not null || _clearPending)
                _ = PumpStateAsync();
        }
    }

    private void CompleteAnimationFrame(HtmlDocument document, AnimationFrame frame)
    {
        FrameCount++;

        // A layout step may have given the page a new size, so the metrics are taken again and the host sizes its content; the regions are the places where
        // something is not where it was (or the page whole, when it cannot be compared). Only the tiles they touch are painted again.
        if (frame.LayoutChanged)
        {
            UpdateMetrics();
            host.ContentResized();
        }

        Invalidate(frame.DirtyRegions);

        if (frame.Events.Count > 0)
            host.AnimationEvents(frame.Events);

        if (!frame.IsRunning)
            StopAnimationLoop();
    }
}
