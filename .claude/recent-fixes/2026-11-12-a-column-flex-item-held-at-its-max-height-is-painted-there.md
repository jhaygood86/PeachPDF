# A flex item held at its max-height is painted at that height, not at its content

**Symptom (#2357).** A column flex item with `max-height: 100pt` over 1000pt of content had its background and border
painted 1000pt tall. The box tree was right (`ActualBottom` 100pt away from its top, the sibling at 200pt): only the
fragment's own rectangle was wrong, so a layout assertion on `Location`/`ActualBottom` passes while the PDF is wrong.

**Cause.** `FragmentEmitter` extends the decoration of a pinned flex/grid item to the content it holds
(`boundsEndAtContentOnThePageGrid`, issue #569) unless the height is authored, the item was flex-shrunk below its content
(`ShrunkByFlexBelowContent`) or it clips. A `max-height` clamp matches none of those: the item is measured with the cap
already applied, so its natural size is the clamped one and `final < NaturalMainSize` is never true.

**Fix.** A definite `max-height` also exempts the box from the extension (CSS 2.1 section 10.7; Flexbox 1 section 9.7
clamps the used size before the item is laid out, Chromium lays it out at the flexed size as a fixed block size).

**Evidence.** `FlexColumnMaxHeightTests`; the paint test (`RecordingGraphics` fill bounds 100 and 200) failed with 1000 before the
change. Engine suite and PeachPDF.Tests pass, zero warnings.

**Not done.** `ShrunkByFlexBelowContent` still means "below its content" only for a flex shrink; a more general "capped" flag
would be cleaner if a third cap appears.
