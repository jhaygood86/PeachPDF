# A repeating `<tfoot>` closes every page the table covers

_Issue [#493](https://github.com/jhaygood86/PeachPDF/issues/493). The mirror of
[#439](https://github.com/jhaygood86/PeachPDF/issues/439) (PR #495), at the other end of the band._

## The issue's own numbers, re-measured first

Re-measured on merged `main` (`9843134`) before touching anything, because
[the standing lesson](2026-07-28-a-repeated-header-sits-above-the-continuation-not-on-top-of-it.md) is
that a measurement in an issue is evidence about the tree it was taken on. They still held, exactly:

| | pages | `<thead>` per page | `<tfoot>` per page |
|---|---|---|---|
| `main` | 7 | `1,1,1,1,1,1,1` | `0,0,0,0,0,0,1` |
| after | **8** | `1,1,1,1,1,1,1,1` | **`1,1,1,1,1,1,1,1`** |

`LayoutHarness`, 300pt page, 20pt margin, one row whose cell holds 244 words.
`LastResortRelayouts` and `PassRewinds` are **0 both, before and after**. The extra page is the point,
not a regression: the footer now costs its own height out of every band the table spans, which is what
§6.2 asks for and what the document was previously getting for free by drawing nothing there.

## Why a third case, and not a relaxed gate

`LayoutBodyRows` wrote a footer in exactly two places, and a pass that stops mid-cell reaches neither.
Both gates are load-bearing for **different** reasons, which is why neither could simply be widened:

- the per-row break block is guarded `i > ResumeRowIndex` because re-deciding the resumed row's break
  point takes a forced break a second time — pushing the row a further page down, adding a second header
  and footer proxy, and writing this pass's `MaxBottom` over the slice bottom the earlier pass recorded;
- step 5's closing footer is gated `!cursor.Stopped` because it sits under the table's *last* row, and a
  pass that has not reached the last row would put it in the middle of the table on the page it is
  leaving — measured during #464 at y=36.5 under a row ending at 35.0.

So step 5a is a **third** footer, and the distinction that makes it one is worth keeping: step 5's footer
closes the *table*, step 5a's closes a *page*. They are different footers at different positions, which
is also why the test that pinned the old behaviour had to be restated as a **position** assertion rather
than a count — a count cannot tell them apart, and the old one (`Assert.Empty`) was satisfied only by
drawing no footer at all.

## The load-bearing half: room at the foot, and its slot rule

Drawing the footer is the easy half. The half that cannot be seen without a raster is that a continuing
cell's lines flow toward the band's bottom and would be drawn *underneath* it — #439 mirrored, with every
word still claimed by exactly one fragmentainer and nothing wrong anywhere in the fragment tree.

`FragmentainerContext.ReserveBandEnd`/`RestoreBandEnd` is the seam, consumed by
`CssRect.WouldStraddleFragmentainer` on the same channel §6.2's `box-decoration-break: clone` insets
already use (`clonedBottom` added to `Bottom` before `FallsPast`, and fed to `FitsNoFragmentainer` so
content that fits nowhere stays exempt). Three decisions in it are not symmetry with the header's
reservation and should not be tidied into it:

1. **The slot is a parameter, not `SlotIndex`.** The row loop tracks its own band and knows which one it
   is placing a row in; the context's `SlotIndex` is a cursor `StepOverTo` may already have moved.
2. **The claim runs forward from the slot it names, but still dies at a step-over.** These are two
   different questions and the first draft conflated them, which the review caught and a fixture then
   confirmed. Running *forward* is required: inline flow reaches the next band without recording a break
   at all, under the boundary tolerance, so an equality would drop the reservation exactly where it is
   needed. Dying at a **step-over** is required for the header's own reason: `StepOverTo` means a forced
   break was realized by placement, and no repeated footer is drawn on the band such a break opens.
   Holding room there is the 13pt blank strip mirrored — measured, on a `<tfoot>` table whose cell
   carries a `break-before: page`, as **one page of seven with no footer on it whose content still
   stopped level with the six that had one** (`lowest=257.4` like its neighbours; `270.6` once fixed,
   three more words on the page). So the reservation carries *two* slots: the floor it applies from, and
   the fragmentainer it was made in, which is a ceiling. This is recorded in
   [the invariant](../invariants/fragmentation-a-repeated-groups-room-is-owed-to-the-flow-the-row-cursor-cannot-position.md).
   **The lesson is the general one that file already states**: naming the fragmentainer is not the whole
   answer, because "which fragmentainer does this apply to" and "which fragmentainer was it made in" are
   different questions, and a reservation that runs forward needs both.
3. **The consumer asks with the same slot the band comes from.** `BandStartingAt(y)` *is*
   `BandOfSlot(SlotStartingAt(y))`, so asking `BandEndInsetOf(SlotStartingAt(Top))` makes the reservation
   and the band it is subtracted from name the same fragmentainer by construction. Reading the context's
   own `SlotIndex` there would have reintroduced exactly the divergence #435 warns about, and would have
   meant touching the band choice that changed 63 of 69 showcases when it was last tried.

**Scoped to every row, not to `ResumeRowIndex`.** The header's scoping does not transfer, and assuming it
did would have been the easy mistake: the header's risk is a cell whose `Location` an earlier fragment
froze, but the cursor positions a row's *top* and every cell's lines then run down toward the band's foot
whichever pass entered it. Which row stops is unknowable in advance — `EstimateRowHeight` is one line of
text per cell and blind to block content, which is why the straddle correction exists at all. The cost is
identically zero for every table without a repeating `<tfoot>`.

It is wrapped around `LayoutBodyRow` and deliberately **not** around the row loop: `TakeBreakBeforeRow`
and step 5 lay out footer/header *proxies*, and a proxy placed at `PageBottomOf(slot) - _footerHeight`
sits entirely inside the reserved strip — in scope, its own words would answer "straddles" and it would
try to break the footer group.

## The second-order effect that is a correction, not a regression

Step 5a writes `PageBreakBottoms[leaving]`, which `FragmentPainter` needs or the table's bottom border is
clipped *above* the footer just drawn under it. That entry has a second consequence: a table that
fragments only mid-cell previously wrote none at all, so `PaginatedItsOwnContentWithoutBreaking` answered
**true** for it and fed the §4.3 whole-table mover. The final pass inherits the map (it is deliberately
not cleared on a continuation), so such a table now correctly stops looking like one that never
fragmented. The write is kept **inside** the footer arm on purpose — writing it for every stopping pass
would flip that predicate for every mid-cell-continuing table, a far wider change than this one.

## What was checked and deliberately not done

- **`cursor.MaxBottom` is not extended** by step 5a. It was measured rather than assumed: the
  per-fragmentainer `FOOTWORD` assertions pass without it, because the proxy is a child box of the table
  with its own bounds and the emitter's band-overlap test reads the child's rect.
- **`WholeTableRelocationTests.ATableWithARepeatingFooterAndNoHeader_IsMovedToo`** was the highest risk —
  a `<tfoot>` table whose only row holds a 400pt div, where a cell that started stopping would mean the
  §4.3 mover never runs. Run first, before any new test was written. Green.
- **§6.2's two conditions** (`break-inside: avoid`, the quarter-page cap) are still unapplied —
  [#494](https://github.com/jhaygood86/PeachPDF/issues/494), and it gets more expensive with this change
  for the same reason #439 made it more expensive: the footer's room is now genuinely reserved.
- **[#509](https://github.com/jhaygood86/PeachPDF/issues/509)** was scoped alongside this and left open,
  with a finding added to
  [its gap file](../accepted-gaps/table-a-repeated-header-is-only-carried-onto-a-page-a-break-falls-on.md):
  what remains of it after this change is a band crossed by a *monolithic* overflowing row, where §6.2's
  "leave room" is unsatisfiable, so closing it as filed swaps a missing header for a header drawn over a
  620pt block.
- **A monolithic block inside a cell** whose bottom lands in the footer's strip is still drawn under the
  footer: `CssBox`'s §4.3 mover does not consult the reservation. Not what the issue measured.
- **Multicol.** Inside a column the reservation is subtracted from the *column's* band while the footer is
  placed at the *page* bottom. Conservative (flow stops earlier), and the multicol showcases are
  byte-identical, so it is left as is.

## Evidence

- Full suite `--framework net8.0`: **6894 passed, 0 failed, 9 skipped** (6885/0 on `main` before the nine
  new theory cases and unit facts).
- Both new `TableCellBreakTokenTests` theories were confirmed **failing on `main`** with the fix stashed:
  `Expected: 7 / Actual: 1` for the claim count, and no footer word at all on any page but the last.
- Showcase corpus: **exactly one of 71 changed**, `paged_media_table_row_continuation`, and only because
  it gained a `<tfoot>` in this change. The other five that differ byte-wise differ only in per-run
  annotation `/NM` UUIDs. Page count 4 → 4.
- **Rasterized every page with both PDFium and MuPDF** and read them, because that is the only thing that
  can see this defect class. The two agree: header at the top and footer at the foot of all four pages,
  the continuation's last line clear of the footer's top border, and the table's bottom border under the
  footer rather than through it.
