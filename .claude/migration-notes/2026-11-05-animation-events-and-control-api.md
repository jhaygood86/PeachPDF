# Animation events and control

**Before.** A host could see only `AnimationFrame.IsRunning` and the dirty regions: it could not tell that an animation started, repeated or ended, or that a transition ran or was cut short, and it
could not pause, seek, slow down, finish or cancel one.

**Now.** `AnimationFrame.Events` reports `animationstart`/`iteration`/`end`/`cancel` and `transitionrun`/`start`/`end`/`cancel` as `AnimationEventInfo`s (the WinUI `HtmlView` raises
`AnimationEvents`), and `HtmlDocument.GetAnimations()` lists `DocumentAnimation` objects with `PlayState`, `CurrentTime`, `PlaybackRate`, `Play`, `Pause`, `Finish`, `Reverse`, `Cancel` and a
`Finished` task. `AnimationFrame` gained an `Events` member (default empty); nothing changes for a host that ignores it.
