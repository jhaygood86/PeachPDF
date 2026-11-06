# The control API restarts a stopped frame loop

`AnimationClock.Wake()` (called by every `DocumentAnimation` control method) only set `IsRunning`, so a host that had stopped stepping when `HasRunningAnimations` went false (the WinUI loop
detaches `CompositionTarget.Rendering` then) never saw `Play()`/`CurrentTime`/`Cancel()` take effect. `Wake` now calls `HtmlContainerInt.AnimationWakeRequested` when it turns an idle clock on,
which `HtmlDocument` raises as the public `AnimationsWoke` event; `HtmlView.OnAnimationsWoke` marshals to the UI thread and calls `EnsureAnimationLoop`.
Load-bearing: it fires only on the idle-to-running edge (a running clock needs no wake, and a host that did would start a second loop), and it may come from any thread.
Tested in `AnimationEventAndControlTests` and in `HtmlViewAnimationTests` (WinUI UI test: no frames while idle, frames again after seeking a finished animation).
