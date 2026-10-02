# A box a later column moved on whole was still drawn by an earlier column (seed 15)

A multi-column fill lays columns out one after another, and each records what it placed. When a later
column breaks *before* a box (here a table cell's paragraph that fails its orphans/widows rule in the narrow
room left beside a float), the whole box moves to the next fragmentainer, but an earlier column of the same run
had already laid out its first lines and kept them, so the words were drawn there and again on the next page.

Fix: after the attempts loop in `CssLayoutEngineColumns.LayoutRunSegment`, the boxes the carried break token
leaves for later (`BeyondThisColumn`, now also following a `TableBreakToken`'s unfinished cells) are forgotten
(`BoxGeometrySnapshot.Forget`) in the run's earlier captured instances.

Not done: seed 15 still doubles 6 words from a different cause. Corpus (300 docs): only seed 15 changes,
doubled 22 -> 6; lost words unchanged.