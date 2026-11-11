# Text beside a float in a `position: relative` box flows against the float's static position (#2229)

Plan reference: workstream A of the float issues plan. #2230 (a relative float drawn at its static position) already held on this branch; its sample is now a test.

**Idea.** Relative positioning is visual only (CSS 2.1 §9.4.3). A float is drawn with the offsets of every box it lies in, while the lines of a block after that box are not, so the line flow must compare the float's *static* edges in the reader's frame. `CssBox.FrameDeltaFor(reader)` is `chain(reader) - chain(ancestors of the float)` (offsets of boxes shared by both cancel); `FlowLeftEdgeIn/RightEdgeIn/TopEdgeIn/BottomEdgeIn(reader)` add it to the existing own-offset-free `Flow*Edge`. Zero while `HtmlContainerInt.HasRelativeOffsets` is false (set the first time a nonzero offset is stored), so documents without relative boxes pay nothing.

**Converted together** (the trap recorded earlier: converting only the test left the advance drawn and stacked a line's text on itself for a large negative `left`): the left-float point lookup and its cursor advance (`GetLastLeftIntersectingFloatBox`, via `CssFloatCoordinates.Reader`), `ScanForNarrowestRightFloatBox`, every reader of `LeftFloatAt`/`RightFloatAt` in `CssLayoutEngine` (line shift, column bounds, line fit, nextY), and `FloatShapeOutline.LeftEdge/RightEdge` (horizontal only; a `shape-outside` outline is not moved vertically by a container offset).

**Found by running it.** The existing `TextAfterAnOffsetBoxHoldingAFloat_IsNeitherStackedNorPushedAway` failed (height 15 = stacked) until the cursor advance inside `GetLastLeftIntersectingFloatBox` (`left = float right edge`) and `FloatLeftEdge` were converted too. Its comment recorded the old drawn-position behavior as a known difference; it now says the text flows around the static position.

**Not done.** Floats placed among a block's own inline content use the same helpers with a zero delta unless an inline relative span lies between them and the reader. The vertical-writing float insets (`GetVerticalFloatInsets`) were not touched.

**Evidence.** Engine suite 19,386 passing on net10.0. `RelativeFloatOffsetTests` gains the #2230 and #2229 samples (the latter: first line ends at the float's left edge, not at the page edge).
