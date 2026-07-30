# A flex item's translation no longer moves geometry that is not the item's

_Landed 2026-07-29._

[#437](https://github.com/jhaygood86/PeachPDF/issues/437), filed as a prerequisite for #430's flex
half and for #390 stage 4's flex/grid position flip. `CssBox.OffsetTop`/`OffsetLeft` — the shared
primitive every subtree-translating mover in this codebase goes through (flex/grid item placement,
the §4.3 movers, multi-column re-banding, table row placement) — walked every descendant
unconditionally. Two kinds of geometry inside a moved subtree do not belong to the box being moved
and were moving anyway.

## The two mechanisms, and the one fix

**An out-of-flow descendant whose containing block sits outside the subtree being moved.** Per
CSS 2.1 §10.1, an absolutely (or fixed) positioned box with no positioned ancestor inside the moved
subtree was already laid out against something that is not moving — usually the initial containing
block. `OffsetTop`/`OffsetLeft` now thread a `translationRoot` (fixed for the whole recursive walk,
not re-derived per frame) and skip a descendant whose `DomUtils.GetNearestPositionedAncestor` result
is not `translationRoot` itself or one of its descendants (`DomUtils.IsSelfOrDescendantOf`, new). A
`position:relative` mover's own genuinely-contained absolute descendants are unaffected — their
containing block is inside the subtree, so the walk still reaches them backed by the same check.

**`CssProxyBox`'s frozen header/footer snapshot isn't reachable by the walk at all.** It is captured
into `BoxGeometrySnapshot` during the proxy's own `PerformLayoutImp`, at whatever position the proxy
had at that moment — for a proxy inside a flex item, that is the item's *measuring* position, before
`AssignLocations` translates the item into place. Nothing in `Boxes`/`Rectangles`/`Words` reaches the
snapshot, so a later translation moved the proxy's own `Location` but left the snapshot describing a
position the proxy no longer holds. A new `protected virtual void OnTranslated(double dx, double dy)`
hook on `CssBox`, called at the end of `OffsetTop`/`OffsetLeft` after `Location` is updated, is a
no-op everywhere except `CssProxyBox`, which overrides it to call a new `BoxGeometrySnapshot.Translate`
that shifts every captured box's `Location`/`Rectangles`/`WordOrigins` by the same delta.

## What was measured, not assumed

Both mechanisms were reproduced and pinned before the fix, then confirmed to fail against the
pre-fix code and pass against the fix (`FlexboxIntegrationTests.cs`):
`AbsoluteDescendantOfAFlexItem_DoesNotTravelWithTheItemsTranslation` (an absolute descendant of a
flex item pushed ~285pt by `justify-content: flex-end` moved with it before the fix; the same
document laid out without the displacement gave the descendant's correct, unmoved position — the two
now agree within 0.5pt), `RelativeFlexItem_AbsoluteDescendant_StillTravelsWithTheItem` (the converse
control: a `position:relative` item's own absolute descendant must still travel, and does),
`RepeatingTableHeaderProxy_InsideAFlexItem_ReflectsTheItemsFinalPosition` (a `CssProxyBox`'s captured
header row geometry was frozen at the table's pre-translation position before the fix; after, it
tracks the table's own displacement exactly).

**Table proxies are created for the very first band regardless of whether the table ever spans more
than one fragmentainer** (`CssLayoutEngineTable`'s `(_headerRepeats || !_continuesAPreviousPass)`
gate) — so mechanism 2 did not need an actual multi-page table nested in a flex item to reproduce; a
single-page table with a `<thead>`, translated by the flex engine, was enough.

## Deliberately unaffected

At today's small item-translation distances (the flex/grid engines still measure at a position close
to the final one — #400/#390 stage 2's remaining flip is what would make this large), neither
mechanism's error moves anything by a visible amount, so this change is behaviour-neutral against the
existing corpus: full net8.0 suite green (6,969 tests, 0 failures, up from 6,966 before this branch's
prior work), CLI suite green (96), `dotnet build PeachPDF.slnx -t:Rebuild` zero warnings, 100% diff
coverage on the changed lines. This is exactly the property [#437's own issue
text](https://github.com/jhaygood86/PeachPDF/issues/437) predicted and is why landing this *before*
#430's flex-item resumability work matters — that work makes translations large and structurally
different in exactly the way that would otherwise make both mechanisms visible for the first time,
the same way the #390-stage-4 flex/grid position-flip attempt found them by accident.

## Deliberately not done

`EscapesTranslationOf`'s check is scoped to `Position is Absolute or Fixed` — `IsOutOfFlow` also
covers floats, but a float's containing block is resolved through ordinary block layout, not
`DomUtils.GetNearestPositionedAncestor`, so reusing the same check for floats would ask the wrong
question rather than a stricter version of the right one. Left alone rather than folded in.

The general shape #437 itself names — "not flex/grid-specific: any mover that translates a laid-out
subtree has it" — is closed for every mover at once, since they all go through the same
`OffsetTop`/`OffsetLeft` primitive; no mover-specific follow-up is needed for mechanism 1. A future
box type that holds geometry outside `Boxes`/`Rectangles`/`Words` (the way `CssProxyBox` holds
`_snapshot`) needs its own `OnTranslated` override — see the invariant file.
