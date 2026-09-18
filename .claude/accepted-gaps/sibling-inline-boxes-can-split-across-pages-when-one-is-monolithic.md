# Sibling inline boxes on one line can be split across pages when one is monolithic

Tracking issue: [#1184](https://github.com/jhaygood86/PeachPDF/issues/1184).

[css-break-3 §4.1](https://www.w3.org/TR/css-break-3/#possible-breaks) treats a line box as a monolithic
break unit — all of a line's content should land in the same fragmentainer. `Fragmentation.FragmentEmitter`'s
monolithic tie-break (`FallsPast`/`MonolithicContent.FitsNoFragmentainer`, read through `ClaimsLine`) is
judged per box, using that box's own portion of the line (`box.Rectangles[line]`), never a rectangle
aggregated across every box sharing the physical line. Two ordinary (non-replaced) sibling inline boxes on
one line, where one box's own font-size makes its own rectangle taller than any single fragmentainer, can
therefore land on different pages: the oversized box is judged monolithic and clipped to its first band
(issue #484), while its ordinary sibling is judged — and claimed — normally.

This is deliberate, not an oversight, and predates issue #1054's own fix: `ClaimsLine` is scoped to one
box's own `Rectangles[line]` by construction (see `FragmentEmitter.cs`'s remarks on it), so it was never
going to unify sibling boxes on the same line — that would need a per-physical-line aggregate, not a
per-`(box, line)` one. Confirmed via a direct A/B run against `main` before #1054's PR that this exact
repro (a `900pt` span beside a `10pt` one, positioned so the large span's own height crosses a page
boundary) splits identically either way.
