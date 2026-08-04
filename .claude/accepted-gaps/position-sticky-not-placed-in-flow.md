# `position: sticky` boxes are never placed in block flow

Tracking issue: [#647](https://github.com/jhaygood86/PeachPDF/issues/647).

`docs/html-css-support.md`'s `position` row claims `sticky` is "treated as `relative` in PDF output
since there is no scroll." That isn't what the code does.

`CssBox.ResolveBlockChildOffset` only sets `PositionedInBlockFlow: true` (participates in normal block
flow) for `Position is CssConstants.Static or CssConstants.Relative` - `Sticky` isn't included there,
and none of `CommitBlockChildOffset`'s other placement branches (`Relative`/`Absolute`/`Fixed`) cover it
either. So a `position: sticky` box falls through to the generic out-of-flow fallback, which just
reports whatever `Location` the box already happens to have - in practice `(0, 0)`, not the position
normal flow would have given it. Confirmed via a direct layout repro: a sticky box after a 20pt-tall
preceding sibling stays at `Location.Y == 0` instead of `20`.

`IsPositioned`/`IsStackingContextBox`/`IsLocalOrderingScope` all correctly treat `sticky` as positioned
for z-index/stacking purposes - only actual box *placement* is missing.

Per [CSS Position Module Level 3 §6.1](https://www.w3.org/TR/css-position-3/#sticky-pos), a
sticky-positioned box that hasn't crossed its scroll threshold behaves exactly like `relative`
positioning (offset zero) - the intended fallback for a PDF renderer with no scrolling, matching the
doc's own stated intent. The code just doesn't currently do it.

**Deliberately out of scope.** Fixing this means adding a real `Sticky` branch to
`ResolveBlockChildOffset`/`CommitBlockChildOffset` that behaves like the existing `Relative` branch - a
real layout bug fix, not a storage-type change.
