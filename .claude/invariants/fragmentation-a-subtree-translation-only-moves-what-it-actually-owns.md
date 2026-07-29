# A subtree translation only moves what it actually owns

_CSS Fragmentation Level 3. Tracker: [#320](https://github.com/jhaygood86/PeachPDF/issues/320)._

`CssBox.OffsetTop`/`OffsetLeft` is the one primitive every subtree-translating mover in this codebase
goes through — flex/grid item placement, the §4.3 movers, multi-column re-banding, table row
placement. Two things a naive "walk `Boxes` and shift everything" recursion gets wrong, fixed once at
this choke point rather than per mover (see
[#437](https://github.com/jhaygood86/PeachPDF/issues/437)):

**An out-of-flow descendant's containing block may be outside the subtree being moved.** A box whose
`DomUtils.GetNearestPositionedAncestor` result is not the translation's own root (or a descendant of
it) was positioned against something that is not moving — translating it anyway double-moves it
relative to CSS 2.1 §10.1's real answer. `EscapesTranslationOf` asks this against a `translationRoot`
fixed for the whole recursive walk, not re-derived per frame, so a `position:relative` mover's own
genuinely-contained absolute descendants (whose containing block *is* inside the subtree) are still
correctly translated.

**Not every box's geometry lives in `Boxes`/`Rectangles`/`Words`.** `CssProxyBox` holds a frozen
`BoxGeometrySnapshot` of a detached source subtree (a repeating `<thead>`/`<tfoot>`) that the ordinary
walk cannot reach at all. A new box kind with geometry outside those three places needs its own
`CssBox.OnTranslated(dx, dy)` override — the hook `OffsetTop`/`OffsetLeft` call once `Location` has
been updated — or a mover translating a subtree containing it will move the box's own `Location`
while leaving whatever else it holds silently describing the position it used to be at.

**This matters most exactly where a translation is large.** At small displacements both errors are
invisible — which is why #437 went undetected until the #390-stage-4 flex/grid position-flip attempt
made translations large enough to see it. Any future work making an item's provisional-to-final
translation larger (true per-item content fragmentation, #430/#315's remaining half) is exactly the
kind of change that would have made this reachable for the first time; it is closed in advance rather
than found by that work the way #437 originally was.
