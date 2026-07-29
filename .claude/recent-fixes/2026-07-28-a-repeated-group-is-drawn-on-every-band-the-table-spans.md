# A repeated group is drawn on every band the table spans, and the row is sliced to make room for it

_[#509](https://github.com/jhaygood86/PeachPDF/issues/509). Tracker:
[#320](https://github.com/jhaygood86/PeachPDF/issues/320)._

[css-tables-3 §6.2](https://www.w3.org/TR/css-tables-3/#repeated-headers) repeats a `<thead>`/`<tfoot>`
"on each page **spanned** by a table" and says the UA "must leave room" for it. Every proxy site the
engine had was keyed to a break being **taken**, so the one band those two questions disagree about — one
a row overflows *through*, which no pass either fills or leaves — got neither the group nor the room.

## The issue's own framing was wrong, and that is the part worth keeping

The gap file argued the remaining case was **unsatisfiable**: the overflowing block is drawn once, at one
position, so a header at that band's top must land on top of it, which is [#439](https://github.com/jhaygood86/PeachPDF/issues/439)'s
defect that PR #495 removed. Closing #509 therefore looked like trading one defect for the other, and the
tracker recorded it as wanting *a decision* rather than an implementation.

Two things retired that.

**[css-break-3 §4.3](https://www.w3.org/TR/css-break-3/#possible-breaks) says the room can be made.** The
sentence carrying the `monolithic-breaking` anchor: *"if there are no possible break points below the top
of the fragmentainer, and not all the content fits, the UA may break anywhere … **In such cases, the UA
may also fragment the contents of monolithic elements by slicing the element's graphical
representation.**"* Once the content is sliced, "leave room" is satisfiable — and so **required**. The
unsatisfiability was an assumption, not a reading.

**It was already half-implemented.** Measured on `main` before touching anything: a `div{height:620pt}`
in a `<td>` already slices continuously across two pages, and a 1000px `<img>` across four. A fragment's
local rect is the box's document rect minus the fragmentainer's `LocalOriginY`, clipped to the band, so
one box spanning three bands already yields three correct strips with no machinery at all. The only thing
missing was the **offset** — each strip began at the band top instead of below the header.

So the fix is not "draw the header anyway", it is "displace the strips".

## What the other engines do, and why only one of them is evidence

- **Blink draws the header on the overflow page and lets it overlap.** `table_layout_algorithm.cc`:
  *"There's monolithic content from previous pages in the way, but we still want to place the table header
  at the block-start. In addition to this (probably) making sense, our implementation requires it. Once we
  have decided to repeat a table section, we need to be consistent about it."* It sets
  `child_block_offset = -monolithic_overflow`, taking the header out of flow, then restores — so the
  header costs that band nothing. The footer is clamped to the band foot *"on top of any overflowing
  monolithic content"*. Same stated invariant both times: a repeated section *"needs to be present in
  **every** subsequent table fragment"*.
- **Gecko is not a counter-design.** `nsTableRowGroupFrame::SplitRowGroup` accepts *"Data loss - complete
  row needed more block-size than available, on top of page"*, and its extra pages are
  `SetOverflowIncomplete` content-less pages that only repaint the previous page's ink overflow. There is
  no table frame on them to carry a header. It never *decides* not to repeat.

Blink's overlap fires only for genuinely §2-monolithic content. The showcase's `div{height:620pt}` is not
monolithic (`MonolithicContent.IsMonolithic` is replaced-elements-or-scroll-containers), so **Blink
fragments that one and its content already resumes below the header**. The gap file measured the wrong
kind of box. What we do here is Blink's answer for the fragmentable case, extended to the replaced case
that Blink gives up on — which §4.3 permits and which costs nothing extra, because the slicing already
worked for both.

## The shape

- **`BandsSpannedBy`** maps a content top and depth onto the bands below it, each opening at
  `PageTopOf(k) + RepeatedHeaderRoom` and closing at `PageBottomOf(k) - RepeatedFooterHeight` — which is
  `RoomForARowIn` read as a pair of edges rather than a length, so the two cannot disagree about what a
  repeated group costs. It carries §4.3's **progress guard**: a band that would leave the run nothing
  takes it whole instead, because a band that took nothing would never terminate.
- **`SliceARowAcrossTheBandsItOverflows`** states one displacement per band through
  `FragmentEmitter.RecordFragmentDisplacement`, and **grows `cursor.MaxBottom` by the gaps** — they are
  real depth on the page, and that bottom is what places every later row and decides how many pages exist.
- **The emitter applies it to the subtree.** `BuildDraft` carries the displacement down its recursion and
  subtracts it from `originY`, which reaches every coordinate `Localize` touches in one place. Membership
  cannot ride on that: `region.Contains` is asked of where the geometry *lands*, so it takes
  `Displaced(rect, shift)` explicitly.
- **`ConfinedTo` is not decoration.** A displaced box's rectangle still spans its whole height, so on any
  band but the first it reaches up into the header's room. Without the confinement — intersected into the
  fragment's `OverflowClip` — the strip the previous band already showed is drawn a second time under the
  header, with every word still claimed by exactly one fragmentainer.
- **Step 5b** draws the groups on those bands, reading which bands already have one **off the live
  proxies** rather than from a counter, because a continuation inherits earlier passes' proxies but not
  their bookkeeping.

## Four gates, every one of them found by a failing test rather than by reading

1. **Not on a pass that stopped.** The bands past a mid-cell continuation are not spanned yet — they are
   the ones the table will be *resumed into*, and the resuming pass opens each with its own groups.
   Measured as an **eighth footer on a seven-page table**, on a band with no slice bottom to sit at.
2. **Not on the band the table ends in**, for the footer. Step 5's closing footer is drawn under the last
   row and runs *after* step 5b, so a footer written there is the second on that page. The header has no
   twin and is drawn on every band.
3. **Only a row §4.3 has run out of moves for** — `depth > RoomForARowIn(container, slot + 1)`, the same
   quantity the straddle correction fits a row against. Without it, a table whose *first row* straddles
   was sliced while the epilogue's mover was about to relocate it whole: **a footer-only table that should
   have moved to page 2 stayed at Y=500.** This is the sharpest one, and it is a `PageBreakBottoms`
   interaction — the slice bottom written for a sliced band tells
   `CssBox.PaginatedItsOwnContentWithoutBreaking` the table fragmented, which stops the move happening at
   all. The old gap file's objection ("the same set `PageBreakBottoms` would have to grow entries for")
   was right about the hazard and wrong about the conclusion: the answer is to write those entries only
   where the row genuinely cannot move.
4. **Only where a group actually repeats.** With neither repeating there is no room to leave and every
   displacement is zero, so the early return is what keeps this invisible to every fixture that repeats
   nothing.

Note gate 3 and the `_bandsARowOverflowedInto` set do the same job from two directions, and both are
needed: the set is what step 5b iterates, and the gate is what keeps a movable row out of it.

## What the review pass found, and none of it was visible to a green suite

Four confirmed defects, on a suite of 6,957 passing tests and two agreeing rasterizations. Every one is
a case of **the displacement reaching one half of a question and not the other**.

- **A displaced fragment lost its background and borders on its last band.** `BuildDraft` asks
  `region.Contains(Displaced(...))`, but the whole-box questions resolved at *materialization* —
  `LinesOf`, `RectOf`, `BandCut` — still asked the un-displaced rectangle, and the shift was not even
  reachable there. Since the shift is always downward this bites only the last band, and only when
  `depth_last ≤ (n−2)·headerRoom + (n−1)·footerRoom`: measured with a 250pt block, the `<div>`'s grey and
  the `<td>`'s border were drawn on **neither** page. That is content lost off the edge of a
  fragmentainer — the exact outcome §4.3's slicing permission exists to prevent. The showcase's 700pt
  block has a 210pt last strip, far outside the window, which is why nothing else saw it. `Draft.Shift`
  now carries it, and `EveryBandOfASlicedRun_KeepsItsDecorations` pins the window.
- **The wrong row was sliced for a `rowspan`.** `cursor.MaxBottom` is the lowest edge *any* cell reached,
  and a spanning cell reaches it only on the row that **ends** the span — a row that does not contain it.
  So the depth was a foreign cell's, the ending row (13pt of text) was displaced, the rows actually
  holding the tall cell got no strips, and the table finished 26.4pt taller than its content. The depth
  is now taken from the row's own cells, skipping `CssSpacingBox`.
- **Growing `cursor.MaxBottom` grew the `<tr>` box**, so a `background-image` or `box-shadow` on a row
  resolved against a border box ~4% taller than its content and rendered stretched — *the very failure
  this change is named for*, one level up from where it was guarded. The cursor still carries the on-page
  bottom (it places the next row); the row's own box now keeps its content's.
- **An `overflow: hidden` ancestor outside the sliced run had its clip displaced too**, drawing content a
  repeated footer's worth past the wrapper's bottom edge. The origin now depends on whether the clipping
  ancestor is inside the run — and note that for content in a cell it usually *is*, because a `<td>`
  clips, so this needs a conditional rather than a swap.

Plus `position: fixed` inside a sliced row, which is the same shape again: `originY` correctly ignored
the shift while the membership test did not, so the box was claimed by a band a strip away from the one it
was drawn in. A fixed box's containing block is the page, so the displacement is dropped outright.

**The rule all five share:** a displacement has two halves — *where the box draws* and *which band claims
it* — and every site that asks one must ask the other. Grep for `Displaced(` before adding a membership
test near this code.

## Traps

- **A tall *empty* div does not paginate.** The first fixtures used
  `<div style='height:700pt'></div>` and the middle pages never became fragmentainers at all — a
  background-less box is not printable content, so blank-page skipping removes exactly the pages under
  test. Every fixture here gives the block a `background`.
- **The confinement's inline axis must not be the row's own width.** The first version used
  `container.PageSize.Width`, which is the *content* width — narrower than the row's own right edge — and
  clipped the cell short on every page. It is opened a page's width either side now; the confinement is a
  block-axis question and has no business cutting a cell's horizontally-overflowing content.
- **A colour mask is a bad way to measure a gradient's extent.** Checking the drawn slice with
  `blue - red > 40` cut the light end of the gradient (`#dbeafe` is 35) and read 597pt of a 620pt block as
  a missing strip; loosening it caught the cell borders instead. The reliable checks are the arithmetic
  (`344.34 + 277.16 = 621.50`, the row's exact depth) and the **seam delta between the last row of one
  page and the first of the next, which is 0 on every boundary**.

## Evidence

Full net8.0 suite green, **6955 passed** (up 11: the new `TableSpannedBandRepetitionTests`), 9 skipped.
`paged_media_table_tall_row` rasterized on every page with **PDFium and MuPDF**, which agree: the header
is on every page, the block resumes below it, and the gradient is continuous across the joins. Seam delta
measured at 0 for both a non-monolithic block (2 pages) and a replaced `<img>` (4 pages).

The tests assert the seam rather than the count deliberately. A header drawn on every page satisfies every
count while the strip underneath repeats or skips content, and the content here **has no words at all**,
so the standing per-word census cannot see it. `TheStripsMeetExactly_WithNothingRepeatedOrSkipped` states
it as the offset *into the row's own box* each band starts and ends at — the only form that catches a
wrong displacement, which moves both edges of a strip together and leaves every other assertion satisfied.
