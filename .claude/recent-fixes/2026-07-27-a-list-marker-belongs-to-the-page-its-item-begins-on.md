# A list marker belongs to the page its item begins on

_Landed 2026-07-27._

**A list marker belongs to the fragmentainer its item begins in, and that is settled the moment the
item is placed** ([issue #444](https://github.com/jhaygood86/PeachPDF/issues/444),
[CSS 2.1 §12.5.1](https://www.w3.org/TR/CSS21/generate.html#lists)). A `<li>` whose own text broke
across a page boundary lost its bullet or number entirely — it painted on **no page at all**.
Measured on the issue's own fixture (40 items of 60 words each, A4, production's 10pt margins):
**39 of 40** markers claimed by a fragment, the missing one being `li26`, the item that straddles the
page-1/page-2 boundary.

**The defect is in layout, not in paint.** An *outside* `::marker` is excluded from its item's inline
flow (`CssLayoutEngine.FlowBox` skips it) and was positioned by the single explicit call in
`CssBox.PerformLayoutEpilogue`, "now that `Location` is final". The epilogue runs on the pass that
**completes** the box, which for an item that breaks is not the pass that starts it — so the marker
got its coordinates one pass *after* the slot those coordinates fall in had been frozen. Paint had
nothing to dispatch on: `MarkerFragmentPainter` is selected off a `BoxFragment`, and there was none.

**The load-bearing observation is that the epilogue's stated precondition was never the marker's.**
The marker is positioned against the item's own **border box** — beside its first line — so nothing
about the item's height, its content, or where it ends is an input. What it needs is the item's
`Location`, and that is written by the frame above at the *start* of `LayoutContents`
(`PlaceBlockChild`), not at the end. The call therefore moved out of the epilogue into
`CssBox.LayoutOutsideMarker`, run from `PerformLayoutImp` on the pass that places the item, before
the break record can unwind out of it. Everything that moves the item afterwards moves the marker
with it, because `OffsetTop` recurses through `Boxes`.

**The one real trap is the ordering against `AwaitPlacement`, and it is why the call sits after
`LayoutContents` rather than after placement.** A block opening its inline flow declares that this
layout has placed none of its subtree's words yet (#433's fix, in `CssLayoutEngine.CreateLineBoxes`),
and that walk reaches the marker even though the flow never visits it. Positioned *before*
`CreateLineBoxes`, the marker's word would have the declaration taken straight back and would be
claimed by nothing, exactly as before — being positioned is what clears the flag
(`CssRect.Top`'s setter), so the marker has to be positioned last. This is invisible in a diff and
would be reintroduced by anyone "simplifying" the call to sit next to `PlaceBlockBox`.

**What running it bought, twice.** First, the fixture, which had to satisfy two constraints that pull
against each other. Pinning the line geometry the way `UnreachedWordClaimTests` does — a 20pt line
against an 830pt band, to stay clear of
[#446](https://github.com/jhaygood86/PeachPDF/issues/446)'s 0.5pt window on `windows-latest` — makes
the issue's own 40×60 list report **40/40 markers claimed on the unfixed build**: with those values
no item lands across a boundary at all, and a fixture that arranges the straddle away tests nothing.
Leaving the geometry unpinned reproduces it exactly (39/40, `li26`) but makes *which* item straddles
a function of the platform's font metrics, and the whole-document claimed-exactly-once assertion is
then exposed to #446. The way out is to stop leaving the straddle to chance: the middle item is
1,200 words, so it spans several pages by itself and breaks under any measurement, and the geometry
stays pinned. The margin is production's 10pt for the reason #433 records — `LayoutHarness` defaults
to 20pt, and a fixture that picks its margin for tidiness can hide this whole family.

Second, the guard. `resume is null && RequestedBreakBeforeTop is null` — the pass that *places* the
item — is the condition, and only the first half is observably load-bearing. §5.2's margin truncation
does reach `RequestBreakBefore` for a list item (measured with a counter: it fires on the
`margin-top: 900pt` fixture), but dropping that half of the guard changed no outcome, because
positioning against the stale `Location` and then re-positioning on the pass that does place the item
self-heals through `CssBoxProperties.Location`'s own `InvalidateFrom` notification. It is kept
because writing a child's geometry against a position the frame explicitly **declined** to assign is
the same class of mistake the origin-vs-unplaced invariant is about, and the self-heal depends on the
two Y values differing.

**The showcase is where a lost marker is actually visible**, and it earns its keep here: `marker_styling`
gains a section 5 whose last item is pushed across the page boundary by a spacer. **68 of 69
showcases are byte-identical** after normalizing `/CreationDate`, `/ID`, the font subset tag and
annotation `/NM` GUIDs; `marker_styling` is the one that differs, by the nine bytes that draw "3." —
present on the fixed build, absent on the unfixed one, agreed by **both** PDFium and MuPDF. Page
count unchanged (2).

**Deliberately not done**: nothing was changed about `AwaitPlacement`'s reach. Excluding the marker
from it would also have worked, and would have allowed the call to sit next to placement, but it
makes two coordinated changes out of one and leaves a marker that a *resumed* pass never re-declares
in a subtly different state. The residual this leaves untouched is
[#318](https://github.com/jhaygood86/PeachPDF/issues/318), an absolutely-positioned inline dropping
its words, which is unrelated.

Tests: `StraddlingListMarkerTests` (5 — the claimed-exactly-once invariant over the whole document,
the same narrowed to markers *and to the slot the item begins in*, the paint calls themselves
(`TestRecordingGraphics`, every numbered marker drawn on exactly one page), the marker's geometry
being unchanged by the move, and the declined-placement guard), plus the list shape added to
`UnreachedWordClaimTests.AParagraphSplitAtAPageBoundary_ClaimsEveryWordExactlyOnce`. Moving the one
call back to the epilogue fails **3 of the 5**, naming the straddling item by id. Full net8.0 suite green
(6,722 passed / 6,731 total), CLI green (96); **100% diff coverage** (14/14 changed library lines);
0 warnings on `dotnet build PeachPDF.slnx -t:Rebuild`.

The durable half is in
[.claude/invariants/fragmentation-an-outside-marker-is-positioned-by-the-pass-that-places-its-item.md](../invariants/fragmentation-an-outside-marker-is-positioned-by-the-pass-that-places-its-item.md).
