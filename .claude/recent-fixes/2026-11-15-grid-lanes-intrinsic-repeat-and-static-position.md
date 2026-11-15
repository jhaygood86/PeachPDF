# Grid lanes: intrinsic repeats, auto-fit collapse, row lane sizing; grid: static position and `minmax(x, auto)`

PR: follows the grid WPT work in [the subgrid gaps note](2026-11-14-grid-wpt-subgrid-gaps-and-named-lines.md). css-grid local reftests 820 -> 919.

**Intrinsic `repeat(auto-fill|auto-fit, ...)` in lanes (css-grid-3 3.1.1).** `ResolveColumnTemplate` counted only definite floors, so any intrinsic repeat was one repetition. `IntrinsicRepeatCount` (Lanes.cs) expands the list `2 + (widest span - 2) / n` times, treats every item as auto-placed, sizes it with the existing `MeasureLaneIntrinsics` + `SizeColumnTracks` (rows: each item's height at its own width, laid out once through `PlaceItemInCell`), takes the largest track per entry index and counts how many fit. The sizing is *not stretched*: stretching the hypothetical tracks makes `repeat(auto-fill, auto)` in 300px count 2 where the reference counts 3. Entries that have a definite floor keep it. Vertical writing modes and an indefinite stacking size are left to the old count.

**Auto-placed spanning items contribute at every start line**, each start asking only the intrinsic lanes it covers for what the fixed ones do not give (the spec's 220px example). The even share over all lanes this replaces made `repeat(2, 50px) repeat(auto-fill, auto)` count 3 lanes where 2 is expected.

**Row lanes**: auto rows took the height of the items that *landed* in them, so a lane no item reached was 0 tall; css-grid-3 3.4 says every auto-placed item contributes to every lane. `requestedLanes` remembers each item's start before `StackLanes` rewrites it.

**auto-fit in lanes collapses after stacking**, not by the heuristic alone: the sum-of-spans heuristic kept a lane `grid-column: 1`/`3` items left empty. The columns are sized and stacked again (`BuildColumns`/`Stack` local functions) when a lane turns out empty; rows collapse after pass one. `CollapseAutoFit` stays as the first guess.

**Dense backfill for definite items**: `TryBackfill` was only for auto-placed ones; a definite item moves up within its own lanes.

**Percentage widths count as auto when sizing lanes** (`MeasureItemContribution(percentageIsAuto)`); plain grid keeps the old reading because `subgrid/standalone-axis-size-004` depends on it (it needs the automatic minimum clamped by the track's max instead, not done).

**Plain grid**: `minmax(<length>, auto)` now stretches (`InitTrack` set `stretches` only for bare `auto`), and `StretchAutoRows` ignores collapsed rows and their gutters.

**Static position**: an absolutely positioned child of a grid that is *not* its containing block is aligned in the content box, whatever area its `grid-area` names (the old code used the named area; `grid-abspos-staticpos-align-self-002` and 12 siblings are the evidence). Vertical grids now work too: `align-self` is the axis across the page (block start is the right edge under `vertical-rl`), `justify-self` the axis down it, and `StateAbsolutelyPositionedAreas` builds the containing area from the logical lines through `_frame.ToPhysical`. An orthogonal child is aligned by the grid's axes.

**Traps**: a python edit script that read files with the default newline translation wrote LF over CRLF-checked-out files; the index keeps LF (autocrlf), so diffs stay small, but check `git show --stat`. When two methods share a snippet, `str.replace(..., 1)` edits the first one: assert the match count. `diffs.py`-style filtered runs are flaky under parallel load (tests that pass alone show as 0-pixel fails); re-run a single test before chasing one.

**Not done**: rtl abspos areas (`positioned-grid-items-025`), reverse + dense backfill (`*-fill-reverse-dense-packing-*`), inline-grid-lanes with an indefinite width counting an auto-fill repeat from `min-height` through its aspect ratio (`column-auto-repeat-003/004`), the lanes subgrid and baseline clusters.
