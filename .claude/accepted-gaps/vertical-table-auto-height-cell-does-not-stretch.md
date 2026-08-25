# A vertical table's auto-height cell doesn't stretch to fill its row/column

_Tracked as [#836](https://github.com/jhaygood86/PeachPDF/issues/836). Discovered incidentally while
investigating [#819](https://github.com/jhaygood86/PeachPDF/issues/819) — see
[.claude/recent-fixes/2026-08-25-table-shrink-columns-dead-code.md](../recent-fixes/2026-08-25-table-shrink-columns-dead-code.md)
for the investigation that surfaced it._

In a `writing-mode: vertical-rl`/`vertical-lr` table, a cell with `height: auto` (the default) does not
stretch to fill its column's shared extent along the table's block (row) axis. This is the vertical
analog of a case that already works correctly in horizontal tables: `width: auto` on a block box means
"stretch to fill the containing block" per CSS 2.1 §10.3.3, so an ordinary horizontal table cell with no
explicit width already fills its column. `height: auto` instead means "shrink to content" per CSS 2.1
§10.6.3, which is correct for a normal block box but *not* for a table cell — [CSS 2.1
§17.5.3](https://www.w3.org/TR/CSS21/tables.html#height-layout) special-cases a table cell's height to
be the larger of its own content height and its row's assigned height, regardless of whether the cell
declared an explicit `height`.

`CssBox.CreateLineBoxes` (runs before `ApplyHeight`) sets `ActualBottom` from the cell's own content,
discarding whatever the table row loop pre-set for the cell along the block axis; `ApplyHeight`'s
subsequent `Math.Max` against an auto height (`0`) has nothing to recover the intended row/column extent
from, since there's no explicit height value being compared.

No existing vertical-table test exercised this before #819's investigation — every one of them gives
every cell an explicit `height`, sidestepping the gap rather than triggering it. The new tests #819
added for `ShrinkColumnsToFitAvailableWidth` size columns via `<col>` (not a table cell, so CSS 2.1
§17.5.3's floor doesn't apply to it) for the same reason.
