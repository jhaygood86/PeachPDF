# `wrap-reverse` swaps the cross axis inside a line too, and an `inline-flex` keeps its own items

_Landed 2026-07-27. Issues [#459](https://github.com/jhaygood86/PeachPDF/issues/459) and
[#462](https://github.com/jhaygood86/PeachPDF/issues/462)._

Two flex cross-axis defects filed out of [the `wrap-reverse` line-stacking fix](2026-07-27-wrap-reverse-stacks-its-lines-in-the-other-direction.md).

## #459 — the swap is two reversals, not one

§5.3 swaps the container's cross-start and cross-end *directions*, and that statement lands in two
methods. `DistributeCrossSpace` had it for the stack of lines; `ComputeCrossOffsets` still placed each
item against the unswapped edges of its own line, so `align-items: flex-start` put a short item at the
top of a row line where cross-start is now the bottom — including through the `stretch` fallback an
item with a definite cross size takes, which is how most documents reach that arm without writing
`align-items` at all.

**The load-bearing idea is that the two reversals are separate and compose.** Each flush arm names an
edge, and under `wrap-reverse` the two edges exchange: `flex-start` becomes
`line.CrossSize - itemCrossSize - crossMarginAfter` and `flex-end` becomes `crossMarginBefore`.
`center` is deliberately untouched — centring a margin box is its own mirror image, `free/2 + marginBefore`
from the top and `free/2 + marginAfter` from the bottom being the same coordinate — and a genuinely
stretched item fills its line and is on both edges at once. `baseline` keeps its group's baselines
aligned with each other and moves only which end of the line the group is flushed against, which is
§8.3 read literally: the flushing item is the one with the largest distance from its baseline to its
*cross-start* margin edge, so the pre-pass now also carries that distance measured to the other end.
Applying the line-level reflection a second time inside a line would cancel the whole thing; the
[invariant](../invariants/flex-wrap-reverse-reverses-two-things-and-they-compose.md) states it.

## #462 — the defect is in the box tree, not the flex engine

The issue reads as a flex-engine problem ("lines placed against a stale cross origin, container sized
from one of them") and predicts the fix belongs next to Phase 10b. **It is neither.** Instrumenting
the layout showed the flex engine runs exactly once, at the right origin, and produces the right
numbers — but only for the *second* item, because `DomParser.CorrectBlockInsideInline` had already
hoisted the first one out of the container and laid it out as a **sibling block**. `ContainsInlinesOnlyDeep`
descends into any box with `IsInline == true`, and an `inline-flex` box is inline-level; what it is
*not* is an ordinary inline box. It is atomic ([CSS Display 3 §2.3](https://www.w3.org/TR/css-display-3/#atomic-inline)),
its contents belong to the flex formatting context inside it, and they are not the surrounding inline
formatting context's block-in-inline problem — the same reason `CssBoxImage`/`CssBoxSvg` were already
excluded there for issue #159.

So the "stale cross origin" was the container's `Location` being moved by the line box after an engine
run that had placed only what was left, and the "height of its tallest line" was the height of its one
remaining line.

## What was found by running it rather than by reading it

**The issue's own repro does not reproduce.** Every `<span>`-item fixture measures correct, at every
variation tried (surrounding text, padding, `vertical-align`, a tall sibling, inside a table, wrapped
onto a second line, nested). The trigger is **block-level** children — `<div>` items — which is what
makes the box look like it has block-in-inline content. Nine fixtures measured right before the tenth
measured the issue's exact numbers.

**Widening the fix to the other atomic inline displays makes them worse, not better.** Naming
`inline-block`/`inline-grid`/`inline-table` as atomic too stops the split, and then nothing places them
at all: `CssLayoutEngine.FlowBox` has an atomic branch for `inline-flex` alone, and the recursion the
others fall into places nothing for a block-level child. Measured, all three go from "first child drawn
in the wrong place" to "container `height: 0` at the origin, nothing laid out". `IsAtomicInlineLevel`
therefore names `inline-flex` only; the rest is [recorded as a gap](../accepted-gaps/atomic-inline-level-boxes-other-than-inline-flex-lose-a-block-child.md)
with issue #473.

**A showcase row that puts one item per line demonstrates nothing about within-line alignment.** The
first version of the section 6 rows copied the existing fixture's `width:90px`, and `FItem` adds 12px
of padding, so at 102px outer only one item fits a 200px container — the two new rows rendered
*identically* on the fixed and unfixed builds while looking entirely convincing. 80px is what makes two
share a line. The rasterization diff is what caught it, by reporting the page as unchanged.

**A stretched item's offset must not be re-derived from its measured size.** The first version computed
the swapped edge unconditionally, and for an item that fills its line that is arithmetically
`crossMarginBefore` — except the stretch branch writes the target size through `FormatLayoutUnits` and
reads it back, so it lands 6e-5 away. That moved a rounded card border in `paged_media_monolithic_content`
and showed up as a changed showcase page whose content stream differed only in the fifth decimal of a
bezier control point.

## What was deliberately not done

`align-content: normal` still packs at flex-start where it should stretch (#461,
[gap note](../accepted-gaps/align-content-normal-packs-instead-of-stretching.md)) — the new fixtures
state `align-content: flex-start` for that reason, as the existing ones do. `first baseline`/
`last baseline` as written keywords remain unsupported; what changed is only which edge the group is
flushed to, which is what §8.3 makes direction-dependent.

## Evidence

Tests: `FlexboxIntegrationTests` +9 cases counting theory rows (5 for the within-line swap, 1 stretch,
1 `align-self`, 1 baseline; 2 theory rows + 1 fact for the inline-flex). One existing fixture,
`WrapReverse_Column_WithADefiniteWidth_KeepsIt`, pinned the old within-line behaviour and was rewritten:
its 20pt item shares a 50pt line and now sits flush right, which is the column direction's swapped
cross-start.

Verified load-bearing by neutralizing each part in turn: the within-line edge swap → **5 fail**; the
stretch branch's re-read of the item's size → **1**; the baseline flush → **1**; the atomic-inline
predicate → **3**.

Full net8.0 suite green (**6768** passed, 9 skipped), CLI green (**96**), **100% diff coverage**
(21 changed lines, 0 missing), zero-warning `dotnet build PeachPDF.slnx -t:Rebuild`.

**68 of 69 showcases identical**, comparing a build with the library changes neutralized against the
same harness source. `flexbox` differs on two pages: section 6 gains a `wrap-reverse + align-items:flex-start`
and a `wrap-reverse + align-items:flex-end` row where the short red item moves between the ends of the
line it shares, and section 10 gains a wrapping `inline-flex` of block-level items which on the unfixed
build is torn into three lines with its first row outside its own border. Both verified in PDFium and
MuPDF. `paged_media_monolithic_content` is identical once the stretched-item tolerance above is in.
