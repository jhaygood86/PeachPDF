# A collapsed table's inline-axis grid lines must be emitted before its block-axis ones

`CssLayoutEngineTable.EmitCollapsedBorderSegments` and `EmitHeaderFooterBorderSegments` both loop over
the inline-axis (column) grid lines **first** and the block-axis (row) grid lines **second**.
`CssBox.CollapsedBorderSegments` is painted in list order, so that ordering is load-bearing, not
stylistic: it is what gives the block-axis line every joint it merely passes through.

## Why it is written that way

Where two grid lines cross, exactly one of them paints the whole joint square, and by the measured
rule (`InlineLineOwnsJoint`) the block-axis line owns nearly all of them — every joint on every line
but the table's block-start one, at equal width and style. Realizing that by *geometry* would mean
splitting each inline-axis line at every row it crosses: one segment per cell, on the common uniform
table. Emission order realizes it for free instead.

The joints that go the other way are then taken by emitting a **square over** the block-axis run —
`EmitBlockAxisRuns` appends one right after the run it overrides — rather than by splitting that run
around them. That choice is itself load-bearing; see below.

## What breaking it looks like

Swap the two loops (or append block-axis segments to a list the inline-axis ones are later added to)
and the symptom is not a crash or a failed geometry assertion — every rect is still in the right
place. Every interior crossing simply changes hands to the inline-axis line. With a uniform,
single-coloured border that is invisible; it shows up as soon as the two lines differ in colour, or
carry `inset`/`outset`/`groove`/`ridge`, where the joint square bands across instead of down.

`CollapsedBorderJointTests` catches it — `AtEqualWidthAndStyle_OnlyTheBlockStartLineGivesAJointAway_AndNotAtTheInlineStartEdge`
asks which line owns each of the nine joints of a 2×2 grid, and that test reads the *last covering
segment*, precisely because ownership here is decided by paint order rather than by whose rect it is.
It uses a fixture whose two axes differ in colour, because two identical borders are deliberately left
to whichever line is already painting there.

# A grid line is only ever cut for a winner whose own style leaves gaps

The companion rule, in the same two methods: a line that loses a joint is normally left **unbroken**
and painted over. It is split around the square only when the winner's style does not fill the rect it
is given — `FillsItsWholeRect`, i.e. `double`, `dotted` and `dashed`.

## Why, measured both ways

- **Splitting everything opens holes that really appear.** A multi-page collapsed table does not paint
  its body column dividers on any page but the last (pre-existing, unrelated to this rule). With the
  block-axis run split around every joint a wider divider won, every interior row line came out
  notched at the divider on every page but the last — a plain `border-collapse` table with a thick
  column rule, i.e. an ordinary document. Painting over degrades to the *wrong paint* in the worst
  case instead of to a *gap*, and a gap is the failure a reader sees as a defect.
- **Painting over does not work for a style with gaps.** `double` rows against `solid` columns, and
  the mirror: Chrome leaves the gap between a `double`'s two rules empty at a crossing, while a square
  painted over the crossing run lets that run show straight through the gap. Measured as 720 differing
  pixels against Chrome 153 before the cut-out arm existed, and 0 after.

So both arms are needed, and both axes implement the cut independently — `EmitBlockAxisRuns` and
`EmitInlineAxisRuns` each have their own `CutsOutJoint`. A change that unifies them onto one arm will
regress one of the two fixtures above, not both.

## The related trap

`EmitInlineAxisRuns` applies a `rowAxisSign` to every offset because `GetGridLineY` runs *decreasing*
with line index under `vertical-rl`. `ColumnBoundaryRect` normalizes its pair with `Math.Abs`, so a
sign error there does not produce a negative extent that something downstream would reject — it
produces a correctly-shaped rect in the wrong place. Its degenerate-run guard is signed for the same
reason.
