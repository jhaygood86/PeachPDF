# A box's inline size comes from the page it lands on, not from its previous Location.Y

Tracker: [#320](https://github.com/jhaygood86/PeachPDF/issues/320). Part of [#515](https://github.com/jhaygood86/PeachPDF/issues/515)
(`#390`'s stage 2). Closes [#540](https://github.com/jhaygood86/PeachPDF/issues/540) (`#515.3`). **The one
sub-stage in this sequence that changes rendered output by design.**

## The load-bearing idea

[css-page-3 §5.1](https://www.w3.org/TR/css-page-3/#page-model) makes each page's own page area the
containing block for the layout that occurs between page breaks, so a box's measure comes from the page it
**lands on**. `CssBox.PlaceBlockBox` resolved the size *first* and asked the frame above for the position
second, which left `CssLayoutEngine.GetBoxWidth` nothing to read but `box.Location.Y` — a coordinate an
earlier layout generation wrote, i.e. page 0's measure on the first one. Only
`HtmlContainerInt.PerformLayout`'s reflow loop could iterate that back to the truth, by feeding each
generation's positions into the next, and it is capped at three iterations.

The order is simply reversed, because the dependency only ever ran one way: **nothing in the frame's
block-flow arithmetic reads the child's inline size.** Checked line by line before touching anything —
`CollapsedMarginBefore`, `prevSibling.StaticBottom`, `ForcedBreakTopFor`, §5.2's `BlockConstraint.EndingAt`
crossing test, the keep-with-next pull, `StepPastSlotsOnTheWrongSide` — none of them do. The three lines
that *do* read a size all live past the point the position is written (`child.ActualMarginLeft`, which
`margin: auto` centres against the used width; a percentage relative offset; `CssLayoutEngine.FloatBox`'s
displacement scan), which is what makes the split fall at one clean seam.

So `PlaceBlockChild` becomes `ResolveBlockChildOffset` (decide, side effects and all — break requests,
blank-slot reservations, `StepOverTo`, the keep-with-next run pull) plus `CommitBlockChildOffset` (write
`Location`, float, relative/absolute/fixed, register the used page name), with a `BlockChildOffset` record
carrying `Left`/`Top`/`PositionedInBlockFlow` between them. `PlaceBlockBox` runs resolve → size → commit;
`PlaceBlockChild` stays as the two-in-a-row composition for `PlaceAsBlockChild`'s callers (`CssBoxHr`,
which has already sized itself), so every doc comment in the repo naming `PlaceBlockChild` still names
something real. `ResolveOwnInlineSize`/`GetBoxWidth` take the block-start coordinate as a parameter.

**`BlockConstraint` was deliberately not the vehicle**, despite `#540`'s own wording. A constraint names a
*fragmentainer*, which inside a multi-column container is a column; the per-page measure is a question about
the **page grid** (`PageContentRightOf` → `PageIndexOf`). Passing the raw document-space coordinate is also
what makes the read bit-identical to the one it replaces — the value handed over is exactly the value
`Location.Y` is about to be set to, so no boundary convention changes hands (the trap `#539` documented at
length).

## What was found by running it, not by reading it

**The whole existing suite passes unchanged (7115), and that is the correct result, not a warning sign.**
Every existing per-page fixture asserts *converged* geometry, and the reflow loop already converged for
them; what this changes is how many generations that takes and what happens to a document that needs more
than the cap. Measured directly with `HtmlContainerInt.LayoutGeneration` (3 is the floor for a per-page-width
document: one layout, one loop iteration that finds the assignment unchanged, one settled final layout):

| fixture | generations before → after | blocks wrapped for the wrong page, before → after |
|---|---|---|
| 90 blocks, `@page :first { margin: 0 }` over 200pt margins | 4 → 3 | 0 → 0 |
| 120 blocks, alternating `:left` 260pt / `:right` 20pt over a full-bleed `:first` | 5 → 3 | **23 of 120** → 0 |
| 120 blocks, 240pt base margins over a full-bleed `:first` | 5 → 3 | **39 of 120** → 0 |

The third fixture also came out at **nine pages instead of six** before: blocks measured too wide were laid
out too short, and nothing re-wrapped them. Those last two are the new tests, and they fail on `origin/main`
with exactly that message.

**The corpus diff is one showcase, and it is a flex container that no longer overflows its own content.**
`print_catalog` (`@page :first { margin: 0 }`, 10 pages) is the only one of 77 that differs; every
`paged_media_*` showcase is byte-identical, because their fixtures are small enough that the loop already
converged. On pages 1-8 the `.price-row` moved 12pt down. Traced to the box tree rather than guessed at:
`.item-body` (a flex container) had `ActualBottom = 1174.94` while its own descendant `ul.features` ended at
`1186.94` — the container was 12pt **shorter than its content**, so `.price-row`'s `margin-top: 9mm` was
measured from a bottom edge 12pt above the real one and the row sat 12pt into the overflow. After the change
`.item-body` ends at `1186.94`, exactly where its content does, and the 9mm gap is a real 9mm. Confirmed
visually at 150dpi with **both** PDFium and MuPDF, which agree.

**The bounded re-resolution round fires, but nothing observable depends on it — worth writing down before
someone deletes it.** Commit can still move the box (`FloatBox`'s displacement scan, and `clear`), so a
round re-measures at where it landed and re-commits from the same resolved offset; instrumented across the
full suite it is entered **zero** times, and a purpose-built fixture (a `clear: both` block pushed past a
float onto a narrower page) enters it exactly once per layout generation and converges — `guard` never
exceeds 0. But disabling the loop entirely leaves that fixture's final geometry *identical*: a displacement
big enough to change the measure has by definition crossed a page boundary, and in a fragmenting layout that
is also a break decision, so the box is placed again at the resumed target and measured correctly there.
What the round buys is that the *first* placement is self-consistent on its own terms — the descendants laid
out inside the box immediately afterwards read its width — rather than by way of a mechanism that runs
later. Kept, with that measurement recorded at the loop itself and a test pinning the invariant.

**Re-committing is safe only because resolve is not re-entered.** `CollapsedMarginBefore` is not pure (it
consumes `_groupTopMarginOverride` — see `#539`'s note) and the resumed-target arm consumes
`_resumeTopOverride`; both live in `ResolveBlockChildOffset`, which the round never calls again. The round
re-runs commit only, and commit resets `Location` to the resolved offset before re-floating, so each round is
a fresh attempt rather than a cumulative slide.

## What was deliberately not done

- **The reflow loop stays.** It also settles the width→height→page-assignment feedback of the boxes *after*
  this one, and `#199`-`#201`'s constrained-block nesting, neither of which this reorder touches. Its
  convergence pressure is what dropped.
- **`BlockConstraint` was not threaded into `GetBoxWidth`.** See above: wrong grain (fragmentainer vs page
  grid), and it would have swapped a boundary convention silently.
- **The engines' own `GetBoxWidth` calls were left alone** (multicol/flex/grid measuring their own
  container). Those run after the frame above has already written the container's `Location`, so the box's
  own coordinate is the truthful answer there; the new parameter is optional precisely so they keep it.
- **No `docs/**` change.** Nothing documented becomes false: the scope limits in
  `.claude/accepted-gaps/per-page-horizontal-reflow-scope.md` (`#196`-`#202`) are about *which* boxes reflow,
  not about how many generations it takes them, and `docs/html-css-support.md`'s "resolved iteratively"
  wording still holds.
- **`#545` was not touched**, as `#540` scopes it out — it is about an escaping directional break's blank
  slot, not about width.

## Evidence

Full `net8.0` suite green (**7119 passing**, 9 skipped, 0 failed — 7115 baseline plus 4 new) and CLI suite
green (96 passing). Zero-warning `dotnet build PeachPDF.slnx -t:Rebuild`. **100% diff coverage** against
`origin/main` (38 lines, 0 missing). Corpus: **1 of 77 showcases differs** (`print_catalog`), normalized for
`/CreationDate`, `/ID`, font subset tags, annotation `/NM`/`/M` and PDFsharp's plaintext creation-date/time
header lines, and verified more correct as above. New `WidthFromTheSpaceNotTheLocationTests` (4): the
mechanism in isolation (`GetBoxWidth` answering about the offered block-start, twice, for one box that never
moves), the two convergence fixtures above (both fail on `origin/main`), and the displaced-`clear` invariant.

**Linux is not the verdict for this one.** Per
`.claude/invariants/testing-the-reflow-fixtures-are-platform-sensitive-by-design.md`, this fixture class has
caught regressions twice on `windows-latest` only, because its font metrics put the content at different page
boundaries. The new fixtures assert an invariant ("every block carries the measure of the page it is on")
rather than a coordinate, specifically so they stay meaningful there — but `windows-latest` CI must be green
before this merges.
