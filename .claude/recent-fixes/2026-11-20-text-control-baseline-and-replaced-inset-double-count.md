# Text controls sit on their text baseline; a replaced box's border and padding are counted once

Found by diffing every showcase against v0.10.0 before 0.10.1: `interactive_pdf_forms` grew about 1.75pt per field row (20.7pt over the page), and bisected to
`GrowLineBelowBaselineForAtomicInset` (Grid Lanes PR).

**Two defects, one symptom.**
- A form control is a replaced box (`CssRectFormField.IsImage`), aligned by its bottom margin edge (`AtomicInlineBaselineOf`). For a text entry or drop-down that
  is wrong: HTML says it is an inline-block depicting a one-line text control, so CSS 2.1 10.8.1 gives it its text line's baseline. Chrome agrees (label top 3px,
  input 21px, line 21px in a measured page). `CssBoxFormField.HasTextLineBaseline`/`TextLineBaselineOffset` now give that baseline, placed where
  `ScreenFormControlPainter` centers the text. Checkbox and radio really have no line box, so the bottom edge stays correct for them.
- Its phantom word's height already holds the border and padding (see `CssRectFormField.IsImage`), but `FlowBox` still inset the line by them again
  (`appliesAtomicInset`: pre-shift of `CurrentY` and `ApplyAtomicInlineVerticalInsets` on `MaxBottom`), and the Grid Lanes PR then added the bottom inset to the
  extent below the baseline a second time. `appliesAtomicInset` is now false for `IsImage` boxes.

**Trap: three places must agree on the baseline.** `AtomicInlineBaselineOf` (alignment), the flow-time extent in `GrowLineToItsExtent` (which used to put the
whole replaced height above the baseline) and `BaselineShiftOf`. Changing only the first made the control overlap the line below it. Both use
`SitsOnItsTextLine`, which excludes a control that owns its own line (block-level), because that case still aligns by the bottom edge through the replaced
fallback; including it moved absolutely positioned inputs up 8pt (`PeachBrackets.Tests` form paint tests caught it).

**Evidence.** A one-field page: line box 22.32pt = control height (Chrome: line = control), label and text on one baseline. In `interactive_pdf_forms` the last row now starts at 551pt (0.10.0: 602, pre-fix: 623,
Chrome: 493); the rest of the gap is the controls being taller than Chrome's, a separate sizing difference.
Re-rendering all showcases changed `interactive_pdf_forms`, `outline`, `document_mode_standards` and `document_mode_quirks` only, each by a tighter line around a
control. Engine (20371), PeachPDF (2221) and PeachBrackets (1340) suites pass on net10.0 Release.

**Not done:** `<input type=button/submit/reset>` and `<button>` should also sit on their text; they are not `CssBoxFormField` text entries here, so they were left.
