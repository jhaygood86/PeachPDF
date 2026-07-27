# A subtree holding a repeating-header table is relocated like any other

_Landed 2026-07-27._

**A subtree holding a repeating-header table is relocated like any other** ([issue #354](https://github.com/jhaygood86/PeachPDF/issues/354)'s first half, the payoff of [#353](https://github.com/jhaygood86/PeachPDF/issues/353)). #332 relocates a box by laying it out again at its destination rather than translating it, which is what stops the fragmentainer gap being carried along inside it — and `CanBeLaidOutAgain` excluded any subtree containing a table that repeats a header, because such a table did not survive a second layout. It does now, so the exclusion and `ContainsARepeatingTable` are gone, from both the epilogue's mover and `TryRestartAt`'s replay range.

**The improvement is real and was invisible to the showcases.** A `break-inside: avoid` card holding a small table with a `<thead>`, straddling a page boundary at three alignments, measured **70.5 / 80.5 / 74pt** against the same card's settled **55.5pt** — 15 to 25pt of fragmentainer gap kept as height it does not use. Laid out again it is 55.5 at every alignment. All 65 showcases are byte-identical, because none of them relocates such a subtree.

Full net8.0 suite green (6459); tests: `RepeatingTableRelayoutTests` (4), each verified load-bearing by restoring the exclusion — the three height cases fail and the header-count guard passes either way.

**Still to do in #354:** the three sites inside the engine that translate rather than raising a decision — the headerless whole-table pre-check, the repeating-`<thead>` pre-check, and the retroactive post-check — along with `PullKeepWithNextRun`, the last open-coded copy of the keep-with-next guards `EarlyBreak.Discover` owns.
