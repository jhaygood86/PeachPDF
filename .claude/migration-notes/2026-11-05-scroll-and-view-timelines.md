# Scroll-driven animations

**Before.** `animation-timeline`, `animation-range*`, `scroll-timeline*`, `view-timeline*` and `timeline-scope` were unknown properties and ignored: every animation ran on time.

**Now.** They are properties. On a screen host an animation with `animation-timeline: scroll()` or `view()` (or a named timeline) takes its progress from how the document is scrolled, over the
`animation-range`; the host feeds the scroll with `SetViewport`, which wakes the clock for one step. A document that sets none of them looks the same as before. `animation-timeline` is not
reset by the `animation` shorthand.
