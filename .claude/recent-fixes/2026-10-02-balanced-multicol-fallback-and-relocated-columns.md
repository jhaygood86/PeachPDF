# A balanced multicol that fits no longer spills, and a relocated one is drawn once

Two defects stacked on the same document: a `columns: 2` container (default `column-fill: balance`) inside a `break-inside: avoid` block, every word drawn on two pages.

1. `CssLayoutEngineColumns` balances by trial fills: estimate, grow, then (after a full-budget fill finished the flow) an even share of that height, grown again. The attempts are capped, and the cap ended on a fill that still carried a tail to the next page, even though the full-budget fill had finished it. The refill at the page budget that rescues this was restricted to *resumed* runs; a fresh run kept the failing fill and spilled four words. The restriction is gone (css-multicol-1 balances only within what fits).
2. The avoid block then straddled the foot, and the relocation moves the boxes by translation rather than laying them out again, so the multicol's recorded column fragmentainers (`_capturedInstances`) for the page it left kept being emitted there, and the box was drawn again where it landed. `CssBox.OnBlockAxisRelocated` now clears a container's recorded columns when it moves to another page.

Traps: the tail spill is what made the symptom look like "relocation fails to invalidate"; fixing 1 alone gives the same doubled output on page 1 (the box now fits and straddles only by its margin), which is what exposed 2. A debug trace of `ClearCapturedInstances` showing only one fresh entry for the container is what said "never re-laid out".

Evidence: `MulticolBalanceFallbackTests` (both fail on main); corpora seeds 1-300 ordinary and tall unchanged (3 lost / 0 doubled, 14 lost / 2 doubled); suites green.