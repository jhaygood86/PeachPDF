# A box nested in a float defers to the inline flow that owns its containing block (#1304), and a formatting-context root ignores outside floats on its own lines (#1335)

## #1335: the starting-level exemption is deliberate, so the boundary goes at the line-flow call site

`DomUtils.FindIntersectingFloatBox` does not apply the formatting-context check at its *starting* level
(see [2026-09-08-the-left-float-scan-stops-at-its-formatting-context](2026-09-08-the-left-float-scan-stops-at-its-formatting-context.md)):
`FloatBoxLeft`/`FloatBoxRight` start it from the float being placed, which must still see its own preceding
siblings. Removing the exemption breaks `FloatRight_InNarrowerNestedBlock_AvoidsAWiderAncestorFloatRightSibling`.
But line flow starts it from the block whose lines are being laid out (`FlowBox(blockBox, blockBox)`), and for
an absolutely positioned box, a table cell, a grid/flex item, a float or an `overflow` box that starting block
*is* a formatting-context root whose lines outside floats never reach (CSS 2.1 §9.4.1). So
`CssLayoutEngine.LeftFloatAt` asks `LineOwnerIsIsolatedFromOuterFloats` (the reference is the line owner
and it is a float, an absolutely positioned box, a table cell or a grid/flex item) and skips the ancestor walk.
Deliberately NOT `EstablishesIndependentFormattingContext`: that is also true for an `overflow: hidden` block,
which CSS 2.1 §9.5 requires to avoid the floats beside it and which this engine does by narrowing its lines, so
that shape keeps the outer float lookup (found in review, pinned by
`OverflowHiddenBlock_BesideAPrecedingFloat_StillWrapsItsTextBesideIt`). Only the owner itself is tested:
from a descendant the walk climbs to the owner and its non-starting-level check already stops there.

Measured: the issue's reproduction put the link's right edge at 1083.75pt on a 595pt page (the report's ~1065);
now it stays inside the page. `RightFloatAt` needed nothing, since `FindNarrowestRightFloatBox` already
tests the reference at its starting level. The same change fixed `GetIntersectingInlineFloat` comparing the
specified `Float.Value` instead of `EffectiveFloatSide`, which never matched an `inside`/`outside` float.

The #1335 diagnosis was reached independently in #1337 (ChrisVanDijk), whose SVG-background regression test
and `positioned_inline` showcase panel for the cart-after-float case are carried over here. Its predicate
(any reference that establishes a formatting context) was the broader one this note rejects.

## #1304: deferral by box-tree ownership, not nesting depth

A box in a float (or block-content inline-block) is laid out during the outer flow's walk, before that flow's
inline has any fragments. Rather than reposition afterwards (wrong for `right`/`bottom`/percentages, which were
resolved against the wrong basis), `HtmlContainerInt.ActiveInlineFlows` lists the flows between the start of
`FlowBox` and the moment their lines are final, and `CssBox.LayoutBlockChild` hands an absolutely positioned
child to the flow that owns its positioned-inline containing block (`TryDeferToEnclosingInlineFlow`), which lays
it out after `FinalizeLineBoxes` with the ones directly among its content. Trap found while writing it: matching
by "the flow whose owner contains the inline" is wrong, because the float's own inner flow finishes and pops
while an *outer* flow further up still contains that owner, and the box would be handed to a flow that does not
own the inline either. The match is therefore the flow owned by the nearest non-inline ancestor of the inline.
Boxes are deduplicated by reference, as a float's content can be laid out on more than one pass.

Evidence: four new geometry tests in `AbsolutelyPositionedInInlineContentTests` fail with the hook disabled and
pass with it; full net8.0 suite green. Not done: a box on a pass that stops at a page break
([the remaining edge](../accepted-gaps/positioned-inline-containing-block-is-read-from-the-fragments-placed-so-far.md)).
