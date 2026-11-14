using Avalonia.Controls;
using PeachBrackets.Updates;

namespace PeachBrackets.Avalonia;

public sealed partial class HtmlView
{
    private bool _animating;
    private bool _stepping;
    private bool _framePending;
    private readonly AnimationLogic.FrameClock _frameClock = new();

    internal int FrameCount { get; private set; }

    /// <summary>Whether the loop that steps the document is running. For tests.</summary>
    internal bool IsAnimationLoopRunning => _animating;

    // Whether this control plays animations: the host asked for them.
    private bool PlaysAnimations => AnimationsEnabled;

    // Steps the document on the display's own frame clock for as long as something plays, and not at all when nothing does: a frame is only asked for
    // while an animation, a transition or an animated image is running.
    private void EnsureAnimationLoop()
    {
        if (_animating || !_attached || _document is not { HasRunningAnimations: true })
            return;

        _animating = true;
        _frameClock.Resume();
        PostFrame();
    }

    private void PostFrame()
    {
        if (_framePending || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        _framePending = true;
        topLevel.RequestAnimationFrame(OnFrame);
    }

    // The control API (play, seek, finish...) started a clock that had stopped, from any thread: the loop that steps it starts again on the UI one.
    private void OnAnimationsWoke(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _document))
            PostToUi(EnsureAnimationLoop);
    }

    private void StopAnimationLoop()
    {
        if (!_animating)
            return;

        _animating = false;
    }

    /// <summary>Stands in for <see cref="HtmlDocument.AdvanceAnimationsAsync"/> when set, so a test can make a step that takes a while or fails.</summary>
    internal Func<HtmlDocument, TimeSpan, PeachDrawing.Core.Canvas, ValueTask<AnimationFrame>>? AnimationStepper { get; set; }

    internal void OnFrame(TimeSpan frameTime)
    {
        _framePending = false;
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

        var time = _frameClock.Step(frameTime);

        // What a frame that changes layout measures text on; the canvas is kept between frames, and handed back when the step has finished.
        var measuring = RentMeasuring(context);
        // A property that moves or resizes boxes lays the document out again, which an asynchronous document may have to wait for: that frame finishes when the
        // layout does, with the painter leaving the surface alone until the new layout is in.
        // The tiles the frame repainted are dropped in the same hold of the lock as the step, so the render thread never paints a plan that has moved a box
        // between layers over tiles that still have it (or still lack it).
        ValueTask<AnimationFrame> step;
        try
        {
            lock (_documentLock)
            {
                step = AnimationStepper is { } stepper ? stepper(document, time, measuring.Canvas) : document.AdvanceAnimationsAsync(time, measuring.Canvas);
                if (step.IsCompletedSuccessfully)
                    ApplyFrameToTiles(step.Result);
            }
        }
        catch
        {
            measuring.Dispose();
            throw;
        }

        if (step.IsCompletedSuccessfully)
        {
            measuring.Dispose();
            CompleteAnimationFrame(step.Result);
            return;
        }

        Forget(FinishAnimationFrameAsync(document, step, measuring));
    }

    private async Task FinishAnimationFrameAsync(HtmlDocument document, ValueTask<AnimationFrame> step, MeasuringLease measuring)
    {
        _stepping = true;
        try
        {
            var frame = await step;

            // A reload that came meanwhile took the document away (and started a loop of its own for the new one): this step ends without touching any of that.
            if (!ReferenceEquals(document, _document))
                return;

            lock (_documentLock)
                ApplyFrameToTiles(frame);

            CompleteAnimationFrame(frame);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // What a document that was let go of throws is not a failure of the one that replaced it.
            if (!ReferenceEquals(document, _document))
                return;

            StopAnimationLoop();
            RaiseDocumentFailed(exception);
        }
        finally
        {
            _stepping = false;
            measuring.Dispose();
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
            RaiseAnimationEvents(frame.Events);

        // Not when the loop was stopped meanwhile: a second chain of frames would start when it is started again.
        if (frame.IsRunning && _animating)
            PostFrame();
        else
            StopAnimationLoop();
    }
}
