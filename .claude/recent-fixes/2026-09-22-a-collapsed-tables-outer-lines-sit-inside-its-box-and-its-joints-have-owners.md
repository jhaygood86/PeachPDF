# A collapsed table's outer lines sit inside its box, and every joint has an owner (#1257)

Two symptoms, one issue, and they turn out to need each other. A `border-collapse: collapse` table's
outermost grid lines were centred on its own border-box edges, so each outer half painted *outside*
the table; and every grid line's run stopped at the perpendicular line's **centre**, so the four
corner squares were painted by nobody. Fixing the second without the first would have filled corners
that were half outside the table.

Both are closed, and the result is **pixel-identical to Chrome 153** on five fixtures (see Evidence).

## The load-bearing idea: the joint rule is not in any spec, so it was measured

CSS 2.1 §17.6.2 resolves a border per grid-line *segment* and says **nothing** about the square two
crossing segments share. So the question "who paints the corner" has no spec answer to transcribe,
and the gap note's framing ("the corner square belongs to the grid and should be filled") is only half
an answer — *which* line fills it decides what the corner looks like, and with `inset`/`outset` the
two candidates look completely different (one bands across, the other down).

Read off Chrome 153 with four purpose-built fixtures, each giving every grid line its own colour so a
joint square names its own owner directly:

1. **Wider wins.** 30pt inline lines took every joint from 10pt block lines; 30pt block lines took
   every joint back from 10pt inline ones. So it is *not* "one axis always wins" in either direction —
   a mistake easy to make from the uniform-border case alone, where the answer looks like "rows win".
2. **Then §17.6.2's own style priority.** Equal 20pt, `double` inline lines vs `solid` block lines:
   the inline lines took *every* joint, including the one the tiebreak below would have given away.
3. **Then the logical side order `inline-start < block-start < inline-end < block-end`**, later wins.
   The joint's owning cell is §17.6.2's *own* tiebreak winner on both lines at once (the row above the
   block line, the inline-start-ward column of the inline line), so the block line is that cell's
   block-start side only at line 0 and its block-end side everywhere else. That collapses to a
   one-line test: **the inline line owns the joint iff the block line is line 0 and the inline line is
   not the inline-start-most one.**

**There is no mitre anywhere.** Not at any width, style or colour combination tried — every joint
square is a solid rectangle of one line's paint. Anyone who "fixes" this by adding a diagonal should
render it in a browser first.

**Step 3 is genuinely logical, not physical.** `dir="rtl"` mirrors it onto the other end of the table
(the inline lines at `i = 0` and `i = 1` win, the one at `i = ColumnCount` loses) — which is what says
the rule is about inline-start rather than about "left", and is why `IsLeftToRight()` appears in
`InlineLineOwnsJoint` at all. PeachPDF does not reverse column order under rtl (a separate,
pre-existing limitation), so that fixture is the one of the five that is not pixel-identical; its
*joint pattern* matches exactly and only the two outer lines' colours are swapped.

## What was found by running it rather than by reading it

- **The geometry half is almost entirely free.** `ApplyCollapsedUsedBorderWidths` giving the table the
  whole line instead of half, plus `StartXSpacing`/`StartYSpacing` giving back half instead of the
  whole, is the entire change. Every other formula is already written as
  `SumHorizontalSpacing() + TableInlineBorderStart + TableInlineBorderEnd` (or the
  `tableRight + HorizontalSpacingAt(n) + TableInlineBorderEnd` shape), and those *self-correct*: the
  spacing terms still subtract `VW/2` at each outer line while the border terms now add the whole
  `VW`, so the total lands on `columns + VW[0]/2 + VW[n]/2` — exactly the new border box — with no
  edit. This was verified by running the suite, not by assuming: `GetWidthSum`'s own independently
  derived total agreed immediately.
- **Emission order is doing half the work, deliberately.** Realizing ownership at every joint a run
  merely *passes through* would mean splitting each inline-axis line at every row it crosses — a
  segment per cell, on the common uniform table where the block-axis line wins nearly everything. So
  inline-axis lines are emitted first and block-axis lines paint over them, which gives the block-axis
  line every pass-through joint for free. **Do not reorder the two loops**; see
  [../invariants/tables-collapsed-inline-axis-lines-are-emitted-before-block-axis-ones.md](../invariants/tables-collapsed-inline-axis-lines-are-emitted-before-block-axis-ones.md).
- **Splitting a losing run around the square was the first design, and it was wrong.** It is exact,
  and it was pixel-identical to Chrome — but a split leaves a real *hole*, and a hole shows whenever
  the segment meant to fill it does not reach the page. A multi-page collapsed table does not paint
  its body column dividers on any page but the last (pre-existing, nothing to do with this change), so
  every interior row line came out notched at the divider on every page but the last, in an ordinary
  `border-collapse` table with a thick column rule. Found by rendering a 4-page table and reading the
  content stream, not by reasoning. The landed design emits a **square over** the losing run instead,
  which degrades to the wrong paint rather than to a gap.
- **Except when the winner's own style leaves gaps.** `double` cannot take a joint by being painted
  over the crossing run, because that run shows straight through the gap between its two rules — and
  Chrome leaves that gap empty. Measured at 720 differing pixels before the cut-out arm existed, 0
  after, in both directions (`double` columns against `solid` rows and the mirror), so both axes
  implement the cut independently. `dotted`/`dashed` are in the same class.
- **Two identical borders get no square at all.** Ownership is only worth realizing when it changes
  what is painted; equal style, width and colour come out the same either way. The exception is a
  bevel, whose two faces say which line the square belongs to even when both borders match — which is
  exactly the `inset` corner the issue measured.
- **Run *ends* are the other half**, and the cases are symmetric on both axes: reach outward over a
  square this run owns, stop clear of one it has to cut out, and stay on the centre otherwise —
  including when another run of the same line continues past the boundary, the two then splitting the
  square. That last case is why interior run boundaries, where a line's resolved border changes
  mid-line, were left alone: both sides reaching outward would overlap by a whole line width.
- **A repeated group's boundary line is not the body's to retract from.** `EmitHeaderFooterBorderSegments`
  resolves that line per page, against whichever row starts or ends that page, but the body's own
  dividers are emitted once from live geometry and have no per-page answer to use. Retracting by the
  whole-table, DOM-order resolution instead opened an 18pt gap under the repeated header on every page
  but the first (a 40pt first-row border against a 4pt real boundary). `BodyJointBorderAt` hands those
  two lines `CollapsedBorder.None` so a body run stops on the centre, where it always met them.
- **`vertical-rl` needed a sign, and only on one axis.** `GetGridLineY` runs *decreasing* with line
  index there (row 0 sits at the physical-max edge), so "outward past the block-start end" is `+half`
  rather than `-half`; `GetGridLineX` has no such reversal, so the block-axis helper needs no sign at
  all. `ColumnBoundaryRect` normalizes with `Math.Abs`, which would have silently turned a
  retracted-past-itself run into a positive-extent rect *in the wrong place* rather than failing — so
  the degenerate-run guard there is signed too.
- **The repeated-header path needed its boundary resolution hoisted.** A `<thead>`'s boundary-to-body
  line is resolved fresh per page (`ResolveRepeatedGroupBoundary`), and the inline-axis dividers
  reaching that line have to compete against the border that will actually be painted there, not
  against `_collapsedBorders`' single DOM-order resolution for it. It is now computed before either
  loop runs.

## Deliberately not done

- **Interior run boundaries do not apply the joint rule.** Where one block-axis line's resolved border
  changes mid-line, the two runs still meet at the perpendicular line's centre and split its square
  between them. Chrome's behaviour there was not measured, the square is fully painted either way, and
  inventing a rule for it would be guessing.
- **`dir="rtl"` column order is still not reversed** — the joint rule mirrors correctly, the columns
  themselves do not, because `CollapsedBorderModel` (and the table engine generally) lays columns out
  physically left-to-right and consults `direction` only for §17.6.2's position tiebreak. Pre-existing,
  untouched here, and the whole reason the rtl fixture is the one of the five that is not
  pixel-identical: don't read that colour swap as a regression from this change.
- **Translucent borders still double-composite at pass-through joints.** One layer, unchanged in count
  from before this change (see the emission-order bullet). Removing it costs a segment per cell.
- **A multi-page collapsed table still paints its body column dividers only on the last page.**
  Pre-existing, reproduced identically on `main`, and the reason the losing run is painted over rather
  than split — but not fixed here, since it lives in the fragment/paint mapping rather than in
  emission. Worth its own issue.

## Evidence

- **Pixel-identical to Chrome 153** (`--headless --force-device-scale-factor=1`, PIL-diffed, empty
  bbox) on: uniform 20px solid, 30px-inline/10px-block, 10px-inline/30px-block, `double` inline vs
  `solid` block, and the issue's own `inset` bevel fixture — which now paints 7800 px of *each* bevel
  face against Chrome's 7800/7800, up from 7600/7600, the whole 400px deficit having been the four
  corner squares. The issue's other measurement also lands: the first red row of a bordered collapsed
  table after a 40px block is now y=40 in both, not y=30.
- Full suite green (13,163 passed, 9 skipped / net8.0), whole solution rebuilds with **0 warnings**,
  **100% diff coverage** (`diff-cover`: 162 measurable changed lines, 0 missing).
- Six tests that could not exist before, in `CollapsedBorderJointTests.cs`: every joint including the
  four corners is covered by *something* (the defect was "nobody painted it", which would otherwise
  read as an ownership answer), no segment falls outside the table's border box, and one test per arm
  of the rule — width both ways, style priority, and the side tiebreak.
- Four existing tests changed because they pinned the old model, not because they broke:
  `CollapsedBorderLayoutTests`' two outer-edge tests (the table's edge and the first cell's used to
  coincide; they are now half a line apart, and the table's own used border width is asserted
  directly) and its two drift tests (which now name the two outer half-lines as a term rather than
  expecting the table to equal its columns exactly). `Issue744Repro_RepeatedTheadVerticalDivider...`
  now expects the divider at the boundary's *inner* edge, since the boundary owns that joint.
  `CollapsedBorderBevel_InteriorGridLineBetweenTwoCells_ShowsBothFacesToo` counts band *positions*
  rather than draw calls, because the block-start line is now emitted in pieces.
