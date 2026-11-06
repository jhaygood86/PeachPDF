# Animation and transition events, and the control API

**What it is.** `AnimationClock` queues `AnimationEventInfo`s (`_events`) as it works and `Advance` returns them in `AnimationFrame.Events`; `HtmlDocument.GetAnimations()` hands out
`DocumentAnimation` objects over the clock's live tracks (`AnimationApplier.LiveTrack`) and transition runs (`AnimationClock.TransitionRun`). The control calls (`Play`, `Pause`, `Finish`,
`Cancel`, `Reverse`, `CurrentTime`, `PlaybackRate`) change flags on the track or run (`ControlPaused`, `Cancelled`, `PlaybackRate`, `Elapsed`) and `Wake()` the clock; the next step samples.

**Load-bearing ideas.**
- An event is a *difference between two steps*: `EmitTrackEvents` compares the animation's elapsed time with what it has already reported (`Started`, `Iteration`, `Ended` on the track), so a step
  that jumps over several boundaries sends them all, in order, and a frame that reports nothing twice is impossible. Iterations are capped at 1000 per step so a long pause cannot flood.
- The reported state has to survive a reload, or every restyle that loads the document again would send `animationstart` again. `_carried` keeps the *old track objects* (not just their
  elapsed time) and `CarryState` copies the flags and moves the host's `DocumentAnimation` onto the new track; a track the new tree no longer names is `RetireTrack`ed, which is what sends
  `animationcancel`. The same retirement covers a track dropped by an in-place restyle (`CarryElapsed`).
- A transition's `transitionrun` is sent when `BeginTransitions` creates it, which is outside any step, so events wait in the queue for the next one, and `NeedsStep` is true while any wait.
- A seek must not replay the animation: `Seek` sets `Started`/`Iteration`/`Ended` from the new position without sending. `Finish` sets everything but `Ended`, so the next step sends the end.
- Cancelling a transition sets its elapsed time to its end and lets the ordinary completion path write the value the style has; the completion path sends `transitioncancel` instead of `transitionend`
  for a run marked `Cancelled`.

**Not done.** The Android view does not step the document (no `AdvanceAnimations` from a `Choreographer`), so it gets neither the clock nor these events. Events of a pseudo-element's animation are
sent with the pseudo-element's box and are not carried across a reload (it has no source ordinal). `Seek` skips events by design. A `DocumentAnimation` has no `effect`/`getComputedTiming`
and no `ready` task (`pending` is never true here).
