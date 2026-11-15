using System.Threading;
using Android.Provider;
using Android.Views;
using PeachBrackets.Android.Logic;
using PeachBrackets.Updates;
using PeachDrawing.Skia;
using SkiaSharp;

namespace PeachBrackets.Android;

public sealed partial class HtmlView
{
    private bool _animationsEnabled;

    /// <summary>
    /// Whether the document plays its CSS animations, transitions and animated images (GIF, WebP). Off by default. A <c>transform</c> or <c>opacity</c> that
    /// animates is kept as a texture on the graphics hardware and only composited each frame over the page, which is not painted again, so it follows the
    /// display's refresh rate without repainting what is inside; other properties that cannot change layout (colours, shadows, filters) repaint only the tiles
    /// they cover. A property that moves or resizes boxes (<c>width</c>, <c>margin</c>, <c>top</c>, <c>font-size</c>, flex and grid sizing) animates by laying
    /// the document out again each frame, so its cost follows the size of the document. Nothing plays when the system removes animations (the animator duration
    /// scale is 0), and the document's <c>prefers-reduced-motion</c> reports it. Changing it loads the document again.
    /// </summary>
    public bool AnimationsEnabled
    {
        get => _animationsEnabled;
        set
        {
            if (_animationsEnabled == value)
                return;

            _animationsEnabled = value;
            if (_attached)
                QueueReflow();
        }
    }

    /// <summary>
    /// Raised, on the main thread, with the animation and transition events (<c>animationstart</c>, <c>transitionend</c> and the rest) that happened since the last
    /// frame, in order. The animations themselves can be looked at and controlled through <see cref="HtmlDocument.GetAnimations()"/> of the document.
    /// </summary>
    public event EventHandler<HtmlAnimationEventsEventArgs>? AnimationEvents;

    // The document is stepped on the main thread and painted on the graphics thread, one at a time.
    private readonly Lock _documentLock = new();

    private bool _animating;
    private bool _stepping;
    private readonly AnimationLogic.FrameClock _frameClock = new();
    private FrameCallback? _frameCallback;
    private SKSurface? _animationSurface;
    private SkiaComposer? _animationMeasuring;

    /// <summary>The number of animation frames this view has stepped. For tests and diagnostics.</summary>
    internal int FrameCount { get; private set; }

    // Whether this view plays animations: the host asked, and the system does not remove them.
    private bool PlaysAnimations => AnimationsEnabled && AnimationLogic.SystemAllowsMotion(AnimatorDurationScale());

    private float AnimatorDurationScale()
    {
        try
        {
            return Settings.Global.GetFloat(Context?.ContentResolver, Settings.Global.AnimatorDurationScale, 1f);
        }
        catch (Exception)
        {
            // No setting to read (a test host): motion is allowed.
            return 1f;
        }
    }

    // The choreographer calls this at the start of every display frame it was asked about.
    private sealed class FrameCallback(HtmlView view) : Java.Lang.Object, Choreographer.IFrameCallback
    {
        public void DoFrame(long frameTimeNanos) => view.OnFrame(frameTimeNanos);
    }

    // Steps the document on the display's own frame clock for as long as something plays, and not at all when nothing does: a frame is only asked for
    // while an animation, a transition or an animated image is running.
    private void EnsureAnimationLoop()
    {
        if (_animating || !_attached || WindowVisibility != ViewStates.Visible || _document is not { HasRunningAnimations: true })
            return;

        _animating = true;
        _frameClock.Resume();
        PostFrame();
    }

    // A view that is not on screen (another activity in front, the app in the background, the screen off) steps nothing; it picks up where it left off when
    // it is shown again, with the time it was hidden left out like any other idle time.
    /// <inheritdoc />
    protected override void OnWindowVisibilityChanged(ViewStates visibility)
    {
        base.OnWindowVisibilityChanged(visibility);
        if (visibility == ViewStates.Visible)
            EnsureAnimationLoop();
        else
            StopAnimationLoop();
    }

    private void PostFrame()
    {
        _frameCallback ??= new FrameCallback(this);
        Choreographer.Instance?.PostFrameCallback(_frameCallback);
    }

    // The control API (play, seek, finish...) started a clock that had stopped, from any thread: the loop that steps it starts again on the main one.
    private void OnAnimationsWoke(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _document))
            _handler.Post(EnsureAnimationLoop);
    }

    private void StopAnimationLoop()
    {
        if (!_animating)
            return;

        _animating = false;
        if (_frameCallback is not null)
            Choreographer.Instance?.RemoveFrameCallback(_frameCallback);

        // A step that is still finishing measures on these: it releases them when it is done.
        if (!_stepping)
            ReleaseMeasuring();
    }

    private void ReleaseMeasuring()
    {
        _animationMeasuring?.Dispose();
        _animationMeasuring = null;
        _animationSurface?.Dispose();
        _animationSurface = null;
    }

    private void OnFrame(long frameTimeNanos)
    {
        if (!_animating)
            return;

        if (_document is not { } document || _context is not { } context || !_attached)
        {
            StopAnimationLoop();
            return;
        }

        // A restyle or a layout step in flight has dropped the layout the frame would be painted from: the next frame is the one to step.
        if (_pumping || _stepping)
        {
            PostFrame();
            return;
        }

        var time = _frameClock.Step(AnimationLogic.FromFrameNanos(frameTimeNanos));

        // What a frame that changes layout measures text on; one per run of the loop rather than one per frame.
        if (_animationMeasuring is null)
        {
            _animationSurface = SKSurface.Create(new SKImageInfo(1, 1));
            _animationMeasuring = new SkiaComposer(context, _animationSurface, 1, 1);
        }

        // A property that moves or resizes boxes lays the document out again, which an asynchronous document may have to wait for: that frame finishes when the
        // layout does, with the painter leaving the surface alone until the new layout is in.
        // The tiles the frame repainted are dropped in the same hold of the lock as the step, so the graphics thread never paints a plan that has moved a box
        // between layers over tiles that still have it (or still lack it).
        ValueTask<AnimationFrame> step;
        lock (_documentLock)
        {
            step = document.AdvanceAnimationsAsync(time, _animationMeasuring.Canvas);
            if (step.IsCompletedSuccessfully)
                ApplyFrameToTiles(step.Result);
        }

        if (step.IsCompletedSuccessfully)
        {
            CompleteAnimationFrame(step.Result);
            return;
        }

        _ = FinishAnimationFrameAsync(document, step);
    }

    private async Task FinishAnimationFrameAsync(HtmlDocument document, ValueTask<AnimationFrame> step)
    {
        _stepping = true;
        try
        {
            var frame = await step;
            if (ReferenceEquals(document, _document))
            {
                lock (_documentLock)
                    ApplyFrameToTiles(frame);

                CompleteAnimationFrame(frame);
            }
            else
            {
                StopAnimationLoop();
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            StopAnimationLoop();
            DocumentFailed?.Invoke(this, new HtmlLoadFailedEventArgs(exception));
        }
        finally
        {
            _stepping = false;

            // The loop was stopped (the window hidden) while the step finished.
            if (!_animating)
                ReleaseMeasuring();
        }
    }

    // A layout step may have given the pages a new size, so the metrics are taken again; the regions are the places where something is not where it was
    // (or the pages whole, when they cannot be compared). Only the tiles that hold them are painted again. Called with the document lock held.
    private void ApplyFrameToTiles(AnimationFrame frame)
    {
        if (frame.LayoutChanged)
            UpdateMetrics();

        if (frame.DirtyRegions.Count > 0)
            InvalidateTiles(frame.DirtyRegions);
    }

    private void CompleteAnimationFrame(AnimationFrame frame)
    {
        FrameCount++;

        // The boxes drawn apart from the page are composited again every frame they changed, over tiles that did not.
        if (frame.DirtyRegions.Count > 0 || frame.OverlayChanged)
            RequestPaint();

        if (frame.Events.Count > 0)
            AnimationEvents?.Invoke(this, new HtmlAnimationEventsEventArgs(frame.Events));

        // Not when the loop was stopped meanwhile: a second chain of frames would start when it is started again.
        if (frame.IsRunning && _animating)
            PostFrame();
        else
            StopAnimationLoop();
    }
}

/// <summary>The animation and transition events of one frame (see <see cref="HtmlView.AnimationEvents"/>).</summary>
public sealed class HtmlAnimationEventsEventArgs : EventArgs
{
    internal HtmlAnimationEventsEventArgs(IReadOnlyList<AnimationEventInfo> events) => Events = events;

    /// <summary>The events that happened since the last frame, in order.</summary>
    public IReadOnlyList<AnimationEventInfo> Events { get; }
}
