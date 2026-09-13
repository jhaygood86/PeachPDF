# A line's relationship to a forced break is stated when the break closes it, never derived from the next line

`CssLineBox.PrecedesForcedBreak` (and its mirror `FollowsForcedBreak`) are **assigned by the flow at the
moment a forced-break word closes a line** — `CssLayoutEngine.FlowBox`'s wrap branch and
`CreateVerticalLineBoxes`'s `word.IsLineBreak` branch. Nothing downstream may re-derive either by
looking at the neighbouring line.

## Why

A line's alignment is decided in `FinalizeLineBoxes`, which runs **per fragmentainer pass**. A pass
that stops at a fragmentation break discards the line in progress
(`blockBox.LineBoxes.Remove(coordinates.Line)`) and finalizes everything below it with
`blockFinished: false`. When the `<br>` is what opened that discarded line, the line that needs the
flag is finalized on **this** pass and its successor does not exist until the **next** one — so a
lookahead answers "no forced break here" and `text-align: justify` stretches a line
[css-text-3 §6.1](https://www.w3.org/TR/css-text-3/#text-align-property) says ends a paragraph.

The same constraint already shaped `FollowsForcedBreak`, which is why
`Fragmentation.InlineBreakToken.FollowsForcedBreak` exists to carry it across a pass for
`text-indent: each-line`. `PrecedesForcedBreak` needs no such carrier only because it is stated before
the pass ends.

## The two rules that follow

- **Assign, never or-in.** A line is closed exactly once, but a box tree can be laid out more than once
  (a shrink-to-fit/flex/table provisional pass, a monolithic relocation). `= word.IsLineBreak` re-derives;
  `|= …` compounds a stale bit from an earlier layout into a later one.
- **Don't reach for an index.** `FinalizeLineBoxes` already iterates by index, so "is the next line a
  forced-break line" looks cheap — but every other caller that would need the answer has only the
  `CssLineBox`, and `OwnerBox.LineBoxes.IndexOf(line)` per line makes finalization quadratic in a long
  block.

## Measured symptom if violated

`TextAlignLastTests.AForcedBreakInAPaginatedBlock_ExemptsALineAnEarlierPassAlreadyFinalized` fails: the
line the `<br>` ends is stretched to the full measure on the page before the break, while the identical
line in a block short enough not to paginate is left ragged.
