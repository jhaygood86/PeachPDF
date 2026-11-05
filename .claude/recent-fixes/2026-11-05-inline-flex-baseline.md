# `inline-flex` and `inline-grid` are baseline-aligned

`AtomicInlineBaselineOf` returned null for anything but an inline-block, so `ApplyVerticalAlignment` never shifted an inline flex/grid container to the line's baseline (it did translate one for
`top`/`bottom`/`middle`, via `OffsetBoxWithinLine`, which was written for it). It now returns the first item's first line baseline (`FirstOwnLineBaselineOf`, the first-line twin of
`LastOwnLineBaselineOf`) or the bottom margin edge when no item has text, and `BaselineShiftOf` accepts the two displays. Only the 240-showcase comparison against `main` (no page differs)
and `InlineFlexBaselineTests` (inline-block, inline-flex with small/large padding and a fixed height, inline-grid) stand behind it; the new showcase section 6 in `baseline_alignment` shows it.
Found while writing the atomic-margin tests: the earlier note called the misalignment a known limit. Not done: an auto-width `inline-grid` is as wide as the line (see its accepted gap).
