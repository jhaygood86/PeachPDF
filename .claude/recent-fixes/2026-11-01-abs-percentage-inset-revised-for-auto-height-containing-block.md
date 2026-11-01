# Absolute percentage top/bottom against an auto-height containing block (follow-up to #2002)

An absolute box is placed while its containing block's children are still being laid out, so an auto-height positioned
ancestor has only a provisional height (0 for the first child) and `top: 10%` / `bottom: 10%` landed against that. CSS Sizing 3
makes the containing block definite for the box, so the final height is the basis.

**Fix:** the same second pass the auto block margins already use (`ResolveAbsolutelyPositionedDescendantAutoBlockMargins`, run
from the containing block's epilogue with its final `ActualHeight`) now calls `RevisePercentageInsets` first. A percentage `top`
is re-resolved and the box moved by the difference from the offset it was placed with (`_placedPercentageInsetTop`, recorded
in `CommitBlockChildOffset`); a `top: auto` + percentage `bottom` box re-runs `PlaceAbsoluteBottomEdge`, extracted from the
epilogue and given the final height. Both are idempotent, so repeated passes do not stack.

**Not done:** a box whose containing block is an inline (`InlineContainingBlockOf`) keeps its single placement; an inline's
rectangle does not change after the first pass.

**Evidence:** three new cases in `PercentageInsetLayoutTests` (top, calc, bottom against a 200pt auto-height block); they
failed (0 / 5 / -10) before. Engine 16961/16961, `PeachPDF.Tests` green.
