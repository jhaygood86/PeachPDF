using PeachBrackets.Html.Animation;
using PeachBrackets.Html.Dom;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PeachBrackets.Updates;

/// <summary>The kind of an <see cref="AnimationEventInfo"/>: the events CSS Animations 1 §4 and CSS Transitions 1 §6 say a browser dispatches.</summary>
public enum AnimationEventKind
{
    /// <summary>An animation's active phase began (<c>animationstart</c>): its delay is over.</summary>
    AnimationStart,

    /// <summary>An iteration of an animation ended and the next began (<c>animationiteration</c>); not sent for the last.</summary>
    AnimationIteration,

    /// <summary>An animation reached the end of its active phase (<c>animationend</c>).</summary>
    AnimationEnd,

    /// <summary>An animation stopped before it ended, because the element no longer names it or it was cancelled (<c>animationcancel</c>).</summary>
    AnimationCancel,

    /// <summary>A transition was created (<c>transitionrun</c>), possibly with a delay still to wait.</summary>
    TransitionRun,

    /// <summary>A transition's delay is over and it began to change the value (<c>transitionstart</c>).</summary>
    TransitionStart,

    /// <summary>A transition reached its end value (<c>transitionend</c>).</summary>
    TransitionEnd,

    /// <summary>A transition stopped before it ended, because the property changed again or it was cancelled (<c>transitioncancel</c>).</summary>
    TransitionCancel,
}

/// <summary>One of the events of the document's animations and transitions, reported by <see cref="AnimationFrame.Events"/> in the order they happened.</summary>
/// <param name="Kind">which event</param>
/// <param name="Element">the element the animation or transition is on</param>
/// <param name="Name">the animation's <c>animation-name</c>, or the property a transition is of (<c>opacity</c>)</param>
/// <param name="ElapsedTime">how long the animation or transition had run, not counting its delay, as the event's <c>elapsedTime</c> says</param>
public sealed record AnimationEventInfo(AnimationEventKind Kind, DocumentElement Element, string Name, TimeSpan ElapsedTime)
{
    /// <summary>Whether the event is of a transition and not of an animation.</summary>
    public bool IsTransition => Kind >= AnimationEventKind.TransitionRun;
}

/// <summary>Where a <see cref="DocumentAnimation"/> is in its life (Web Animations 1 §4.4.3).</summary>
public enum AnimationPlayState
{
    /// <summary>Cancelled, or not playing any more because it was removed: it has no effect.</summary>
    Idle,

    /// <summary>Playing.</summary>
    Running,

    /// <summary>Held where it is, by <c>animation-play-state: paused</c> or <see cref="DocumentAnimation.Pause"/>.</summary>
    Paused,

    /// <summary>Past its end (or, played backwards, back at its start).</summary>
    Finished,
}

/// <summary>
/// A CSS animation or transition that is running on an element of the document, to look at and to control as Web Animations 1 §4.4 describes an
/// <c>Animation</c>: play, pause, seek, change its speed, finish it, reverse it or cancel it. Get them from <see cref="HtmlDocument.GetAnimations()"/>.
/// The document is not thread-safe, so neither is this: use it where the document is used.
/// </summary>
/// <remarks>
/// A change made here takes effect on the next <see cref="HtmlDocument.AdvanceAnimations(TimeSpan)"/> (the document says <see cref="HtmlDocument.HasRunningAnimations"/>
/// until it has been made). The object stays the animation it was taken for when the document is loaded again (a restyle that reloads it); once the element stops naming
/// the animation, it is <see cref="AnimationPlayState.Idle"/>.
/// </remarks>
public sealed class DocumentAnimation
{
    private readonly AnimationClock _clock;
    private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal DocumentAnimation(AnimationClock clock, object source, CssBox box)
    {
        _clock = clock;
        Source = source;
        Element = new DocumentElement(box);
    }

    // The animation's track or the transition's run, which the clock replaces when the document is loaded again (this follows).
    internal object Source { get; set; }

    /// <summary>The element it is on.</summary>
    public DocumentElement Element { get; internal set; }

    private AnimationApplier.LiveTrack? Track => Source as AnimationApplier.LiveTrack;

    private AnimationClock.TransitionRun? Run => Source as AnimationClock.TransitionRun;

    /// <summary>Whether it is a transition (<see cref="Name"/> is then the property) and not a <c>@keyframes</c> animation (its <c>animation-name</c>).</summary>
    public bool IsTransition => Run is not null;

    /// <summary>The <c>animation-name</c> of an animation, or the property a transition is of.</summary>
    public string Name => Track?.Name ?? Run?.Property ?? string.Empty;

    /// <summary>Where it is in its life.</summary>
    public AnimationPlayState PlayState
    {
        get
        {
            if (Track is { } track)
            {
                if (track.Cancelled || track.Removed) return AnimationPlayState.Idle;
                if (track.Finished && track.PlaybackRate >= 0) return AnimationPlayState.Finished;
                return track.Paused || track.ControlPaused ? AnimationPlayState.Paused : AnimationPlayState.Running;
            }

            if (Run is { } run)
            {
                if (run.Cancelled || run.Removed) return AnimationPlayState.Idle;
                if (run.Finished) return AnimationPlayState.Finished;
                return run.ControlPaused ? AnimationPlayState.Paused : AnimationPlayState.Running;
            }

            return AnimationPlayState.Idle;
        }
    }

    /// <summary>
    /// How far it has played, counting its delay (a time of the delay is before any change shows), as Web Animations 1 §4.4.4. Setting it seeks: the animation is
    /// sampled at that time on the next step, whether it is paused or not.
    /// </summary>
    public TimeSpan CurrentTime
    {
        get => TimeSpan.FromSeconds(Track?.Elapsed ?? Run?.Elapsed ?? 0);
        set
        {
            var seconds = Math.Max(0, value.TotalSeconds);
            if (Track is { } track) _clock.Seek(track, seconds);
            else if (Run is { } run) _clock.Seek(run, seconds);
        }
    }

    /// <summary>How fast time passes for it: 1 is normal, 2 twice as fast, negative plays it backwards (and it stops at its start). Not 0: pause it instead.</summary>
    /// <exception cref="ArgumentOutOfRangeException">set to 0 or to something that is not a number</exception>
    public double PlaybackRate
    {
        get => Track?.PlaybackRate ?? Run?.PlaybackRate ?? 1;
        set
        {
            if (value == 0 || double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value), "A playback rate is a number that is not 0.");

            if (Track is { } track) track.PlaybackRate = value;
            else if (Run is { } run) run.PlaybackRate = value;
            _clock.Wake();
        }
    }

    /// <summary>Changes <see cref="PlaybackRate"/> (Web Animations 1 §4.4.14: it takes effect from now, which is also what setting it does here).</summary>
    /// <param name="playbackRate">the new rate, not 0</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="playbackRate"/> is 0 or not a number</exception>
    public void UpdatePlaybackRate(double playbackRate) => PlaybackRate = playbackRate;

    /// <summary>Plays it: lets it run if it was paused here, and plays it again from the start (or, backwards, from the end) if it had finished.</summary>
    public void Play() => _clock.Play(this);

    /// <summary>Holds it where it is, until <see cref="Play"/>.</summary>
    public void Pause() => _clock.Pause(this);

    /// <summary>Jumps to its end (or, with a negative <see cref="PlaybackRate"/>, to its start) and finishes there.</summary>
    /// <exception cref="InvalidOperationException">it plays for ever (<c>animation-iteration-count: infinite</c>), so has no end to go to</exception>
    public void Finish() => _clock.Finish(this);

    /// <summary>Stops it and removes its effect: the element has the value it would have without it. <see cref="Finished"/> is cancelled.</summary>
    public void Cancel() => _clock.Cancel(this);

    /// <summary>Turns <see cref="PlaybackRate"/> around, so it plays backwards from where it is (and a finished one plays back from its end).</summary>
    public void Reverse() => _clock.Reverse(this);

    /// <summary>A task that completes when it finishes, and is cancelled if it is cancelled or removed first.</summary>
    public Task Finished => _finished.Task;

    internal void Complete(bool cancelled)
    {
        if (cancelled) _finished.TrySetCanceled();
        else _finished.TrySetResult();
    }
}
