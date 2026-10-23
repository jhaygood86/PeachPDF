# A line below a sliced row's strip is not that strip's line (#1325)

A table with a repeating `<tfoot>` whose body cell holds a 732pt `<svg>` followed by `text` (A4, 20pt margins) had `text` in
the fragment tree on both the first and the second page (heights 732 to 792pt alike). The row plus the reserved footer is
taller than a band, so `SliceARowAcrossTheBandsItOverflows` slices it into strips; the strip is the band less the footer's
room. The line holding `text` starts below the first strip's foot but inside the band the membership test is asked
about, and `ReachesIntoItsStrip` asked only about the strip's top (`landed.Top >= strip.Top`, true for anything below it), so
the first page claimed the line and the second claimed it again once displaced into its own strip.

`ReachesIntoItsStrip` now also requires `landed.Top < strip.Bottom - PageBoundaryEpsilon`: a line starting at or past a
strip's foot is a later strip's. A line that merely runs past the foot still belongs to the strip it starts in, as before.

Found with the word-claim ledger (`PEACHPDF_VERIFY_WORD_CLAIMS=1`, DEBUG): it named `EmitPass(fromSlot=0, throughSlot=1)`
freezing `text` at slot 0 and again at slot 1, which put it in the same sweep, not in `Finish()`'s replay.

The ledger run over the whole engine suite flags 15 tests, not the four older notes list; several are deliberate
multi-slot claims (sliced monoliths, a capped scroll container) that its exclusions do not cover, so "the ledger runs
clean" is not a usable acceptance test. #1210's three rowspan tests still trip it, in `Finish()`'s replay (open); the
early-break test of #1211 no longer does.

Evidence: `TableTallInlineReplacedRepeatingFooterTests.TheTextAfterATallSvg_IsInTheFragmentTreeOnce` at 732, 750 and 792pt
(failed with 2 before the change).
