# Scroll-driven animations: what is not modelled

`animation-timeline`, `animation-range*`, `scroll-timeline*`, `view-timeline*` and `timeline-scope` work for the document's own scrolling (see
[the recent fix](../recent-fixes/2026-11-05-scroll-and-view-timelines.md)). These are left out.

- **Only the document scrolls.** The engine has no scroll position for a box: `overflow: auto | scroll` is not clipped or scrolled (only `hidden` and `clip` clip), so there is no `scrollTop`
  to follow. A timeline whose scroller would be such a box (`scroll(nearest)` inside one, `scroll(self)`, a `view()` of an element inside one, a `scroll-timeline-name` declared on one) does not exist,
  and the animation has no effect, as it would on a scroller that does not scroll. A per-element scroll offset model (clip and paint with an offset, hit testing, a host input path) would make them real.
- **The block axis is vertical and the inline axis horizontal**, as in `horizontal-tb`; `writing-mode` and `direction` do not change which way a timeline runs.
- **`animation-range` with only a named start** (`animation-range: entry`) ends at the `cover` end, and not at the 100% of the name the shorthand's rule gives it, since the shorthand stores an
  omitted end as `normal`. Write both ends.
- **The `animation` shorthand does not reset `animation-timeline` or `animation-range-*`** (it should reset them to `auto` and `normal`), so a later `animation:` does not undo an earlier
  `animation-timeline`.
- **A view timeline's subject is its untransformed border box in the layout the document last had**, with no sticky or fixed displacement; a sticky or fixed subject has the progress of its
  laid-out place, not of where it is held.
- **No events, and little control.** An animation on one of these timelines sends no `animationstart`/`iteration`/`end` and ignores seeking, the playback rate, `Finish()` and `Reverse()` of the
  control API.
- **Not part of this:** `animation-trigger` and `timeline-range` percentages in `@keyframes` (`entry 50% { ... }`), and `ScrollTimeline`/`ViewTimeline` objects for the control API.
- **The Android view does not step the document**, so scroll-driven animations do not play there either (see the live playback gaps).
