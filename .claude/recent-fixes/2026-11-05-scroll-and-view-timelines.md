# Scroll and view timelines

**What it is.**
- Nine longhands and three shorthands (`animation-range`, `scroll-timeline`, `view-timeline`) stored as raw text. The grammar is one class, `TimelineGrammar` (`CSS/`), used by the CSS-OM
  converters (`GrammarValueConverter`, which validates a declaration against it and keeps the text) and by the render layer, so what the cascade accepts is exactly what is read. A value it rejects
  is an invalid declaration, and the earlier one stands.
- `AnimationApplier.BuildLiveTracks` gives each `LiveTrack` a `ResolvedTimeline` (`ResolveTimeline`: `scroll()`/`view()`/a name found on the element, its ancestors, or through an ancestor's
  `timeline-scope`) or marks it inactive when the timeline does not exist, plus the `RangeStart`/`RangeEnd` edges.
- The clock works progress out of the scroll position (`AnimationClock.UpdateScrollTrack`): `HtmlDocument` feeds it the viewport and the surface size before each step (`FeedScroll`), the
  range is the scrollable distance (scroll) or a named range of the subject's border box against the scrollport (view; the formulas for `cover`, `contain`, `entry`, `exit`, `entry-crossing`,
  `exit-crossing` are in `RangeName`, with the inset at each end), and `AnimationTimeline.ScrollProgress` turns it into an iteration progress with the fill modes and direction.
- `HtmlDocument.SetViewport` calls `AnimationClock.NotifyViewportChanged`, which makes `NeedsStep` true once (only if some animation is scroll-driven), so the host steps once per scroll and its
  frame loop idles between. `HtmlView.UpdateViewport` calls `EnsureAnimationLoop` for it.

**Load-bearing ideas.**
- A scroll-driven track has no `Elapsed`: the loop that adds time to a track skips it, it never `Finished` (it can be scrolled back, and a finished track stops being written), it does not count
  as `running`, and it sends no events. `SampleLive` reads `track.ScrollProgress`, which the clock set that step, and everything after it (frames, easing, composition, the written-value
  dedup that keeps an unmoved frame free) is the same as for time.
- A subject's rectangle comes from the fragment tree (`WholeBoxRect` through `TextRunCollector.Frame`), indexed once per layout (`_subjectRects`, keyed by the layout's fragment list) and not walked
  per frame.

**Not done.** See [the scroll-driven gaps](../accepted-gaps/css-scroll-driven-animation-gaps.md): chiefly that only the document has a scroll position.
