# A list marker belongs to the page its item begins on

_Landed 2026-07-27._

**A list marker belongs to the fragmentainer holding the geometry its item keeps**
([issue #444](https://github.com/jhaygood86/PeachPDF/issues/444),
[CSS 2.1 §12.5.1](https://www.w3.org/TR/CSS21/generate.html#lists)). A `<li>` whose own text broke
across a page boundary lost its bullet or number entirely — it painted on **no page at all**.
Measured on the issue's own fixture (40 items of 60 words each, A4, production's 10pt margins):
**39 of 40** markers claimed by a fragment, the missing one being `li26`, the item that straddles the
page-1/page-2 boundary.

**The defect is in layout, not in paint.** An *outside* `::marker` is excluded from its item's inline
flow (`CssLayoutEngine.FlowBox` skips it) and was positioned by the single explicit call in
`CssBox.PerformLayoutEpilogue`, "now that `Location` is final". The epilogue runs on the pass that
**completes** the box, which for an item that breaks is not the pass that starts it — so the marker
got its coordinates one pass *after* the slot those coordinates fall in had been frozen, and nothing
re-opened it (`InvalidateEmittedFragmentsFor` is a no-op for a box no frozen fragmentainer holds,
which a never-emitted marker is). Paint had nothing to dispatch on: `MarkerFragmentPainter` is
selected off a `BoxFragment`, and there was none.

**The load-bearing observation is that the epilogue's stated precondition was never the marker's.**
The marker is positioned against the item's own **border box** — beside its first line — so nothing
about the item's height, its content, or where it ends is an input. What it needs is the item's
`Location`, which the frame above writes at the *start* of `LayoutContents` (`PlaceBlockChild`). The
call moved out of the epilogue into `CssBox.LayoutOutsideMarker`, run from `PerformLayoutImp` on the
pass the new predicate `MarkerBelongsToTheFragmentainerBeingFilled` names.

**That predicate is two rules, not one, and the second was found by the review rather than by me.**
My first version was just "the pass that places the item" (`resume is null`), which is right on the
page grid and **wrong inside a column**: a box that does not finish in a column is laid out *again* at
the next column's inline position (`ResumeInTheNextFragmentainer`, gated on
`FragmentainerContext.HasOwnBand`), so only its last fragment's geometry survives. Positioning on the
first pass put the marker in the column the item had left — a bullet beside nothing in column 1, none
beside the item's own text in column 2, **on a single page with no page break in it** — and left that
column's `BoxGeometrySnapshot` holding a second origin for the same word, so it was claimed twice. The
review's 728-document `column-count: 2` sweep put the scale at **661 affected documents against 12**
for the old behaviour. So the predicate asks `HasOwnBand`, the very question that decides whether the
box will be re-placed: on the page grid the marker goes with the pass that *starts* the item, inside a
column with the pass that *completes* it. **Both halves are load-bearing and the tests separate them**
— dropping the page-grid half fails 3 of the 6, dropping the column half fails exactly the 1 that is
about columns.

**The trap that is invisible in the diff is the ordering against `AwaitPlacement`,** and it is why the
call sits after `LayoutContents` rather than next to placement. A block opening its inline flow
declares that this layout has placed none of its subtree's words yet (#433's fix, in
`CssLayoutEngine.CreateLineBoxes`), and that walk reaches the marker even though the flow never visits
it. Positioned first, the marker's word would have the declaration taken straight back and be claimed
by nothing, exactly as before — being positioned is what clears the flag (`CssRect.Top`'s setter).

**What running it bought, on the fixture.** The two constraints pull against each other. Pinning the
line geometry the way `UnreachedWordClaimTests` does — a 20pt line against an 830pt band, to stay
clear of [#446](https://github.com/jhaygood86/PeachPDF/issues/446)'s 0.5pt window — makes the issue's
own 40×60 list report **40/40 markers claimed on the unfixed build**: with those values no item lands
across a boundary at all, and a fixture that arranges the straddle away tests nothing. Leaving the
geometry unpinned reproduces it exactly (39/40, `li26`) but makes *which* item straddles a function of
the platform's font metrics — and `windows-latest` duly failed the first push with **14 words claimed
by both pages 0 and 1**, which is #446 and not this. The way out is to stop leaving the straddle to
chance rather than to unpin: the middle item is 1,200 words, so it spans several pages by itself. The
margin is production's 10pt for the reason #433 records — `LayoutHarness` defaults to 20pt, and a
fixture that picks its margin for tidiness can hide this whole family.

**One guard is deliberate and not independently demonstrable.** `RequestedBreakBeforeTop is null` — a
pass that *declined* to place the item has written no position for the marker to sit against. §5.2's
margin truncation does reach `RequestBreakBefore` for a list item (measured with a counter on a
`margin-top: 900pt` fixture), but dropping this half changed no outcome, because re-positioning on the
pass that does place the item self-heals through `Location`'s own `InvalidateFrom` notification. Kept
because writing a child's geometry against a position the frame explicitly declined to assign is the
same class of mistake the origin-vs-unplaced invariant is about, and the self-heal depends on the two
Y values differing.

**The showcase is where a lost marker is actually visible.** `marker_styling` gains a section 5 whose
last item is pushed across the page boundary by a spacer. **68 of 69 showcases are byte-identical**
after normalizing `/CreationDate`, `/ID`, the font subset tag and annotation `/NM` GUIDs;
`marker_styling` is the one that differs, by the nine bytes that draw "3." — present on the fixed
build, absent on the unfixed one, agreed by **both** PDFium and MuPDF. Page count unchanged (2).
Re-run against `main` at 356b298 after merging it in, with the same result.

**Two residuals confirmed pre-existing and left alone**, each with a gap file and a tracking issue: a
`<li>` whose content is block-level gets **no marker at all**, because `CorrectTextBoxes` re-parents
the marker into an anonymous block and the direct-children scan misses it
([#467](https://github.com/jhaygood86/PeachPDF/issues/467)); and the column behaviour above is itself
a spec deviation, since §12.5.1 wants the marker beside the item's *first* line
([#468](https://github.com/jhaygood86/PeachPDF/issues/468)). Both verified identical on `main` by
running the same fixtures on both builds.

Tests: `StraddlingListMarkerTests` (6 — the claimed-exactly-once invariant over the whole document,
the same narrowed to markers *and to the slot the item begins in*, the paint calls themselves
(`TestRecordingGraphics`, every numbered marker drawn on exactly one page), the marker's geometry being
unchanged by the move, the column-boundary case, and the declined-placement guard), plus the list shape
added to `UnreachedWordClaimTests.AParagraphSplitAtAPageBoundary_ClaimsEveryWordExactlyOnce`. Full
net8.0 suite green, CLI green (96); **100% diff coverage**; 0 warnings on
`dotnet build PeachPDF.slnx -t:Rebuild`.

The durable half is in
[.claude/invariants/fragmentation-an-outside-marker-is-positioned-by-the-pass-that-places-its-item.md](../invariants/fragmentation-an-outside-marker-is-positioned-by-the-pass-that-places-its-item.md).
