# `overflow: clip` on one axis beside `visible` clips both axes

_css-overflow-3 §3.2. Tracker: [#1635](https://github.com/jhaygood86/PeachPDF/issues/1635)._

`overflow-x: clip; overflow-y: visible` (or the reverse) clips on both axes, where a browser clips only the
`clip` axis. `CssBox.ClipsWithoutScrolling` is one flag, and the clip rectangle `RenderUtils.TryPushOverflowClip`
builds is the padding box on both axes.

An axis-aware clip would need that rectangle open on the other axis in the painter and in every
`DomUtils.ClipsItsOverflow` reader in the fragment emitter. The pair is rare, so it was left when `clip` and
the per-axis longhands were added ([the fix](../recent-fixes/2026-10-05-overflow-is-a-pair-of-axes-and-clip-clips.md)).

Documented in the `overflow` row of `docs/html-css-support.md`. Closing it means deleting this file and that sentence.
