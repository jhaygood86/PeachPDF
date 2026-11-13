# Per-word and per-box allocation added since v0.9.26

A readiness check against v0.9.26 (interleaved `TestHarness --benchmark`, best of 3) found main allocating a median 3.2% more per
showcase (p90 +10%) with flat total time. These byte-identical changes took the median to about 3% *below* main:

- `PaintTextEmphasis` tokenized the string `"none"` (the initial value) for every painted word: it now returns on `none` first.
- `TextEmphasisGrammar.ParseSkip` split and lowercased the stored `text-decoration-skip` per decorated fragment: `objects`/`auto`/`none` are answered directly.
- `SelectorChildrenView.For` built a view, a list and an array per structural-selector test on a narrow parent: with no anonymous wrapper it returns the child list.
- `ExtentBesideFloatsWithBottom` made a transient line box per table/columns pass: it returns the extent when the document has no floats.
- `PendingVarMap` allocated three dictionaries per box and wrote one per plain declaration: they are lazy, and `NotePlain` is a no-op while nothing is deferred.
- `GatherMatchedRules` built a `HashSet` per box: short match lists are de-duplicated by a scan.
- The word loop's `() => GrowLineToItsExtent(word)` captured the loop variable, so the closure was allocated every iteration: it is built by a helper only where used.

Verified by comparing every page's content stream across all 245 showcases against main (identical), and the Engine and PeachPDF suites.
Not done: `CssFloatCoordinates` reuse (an output-bearing object, small gain), per-rule revert-keyword cache (rules can be mutated), and the
multicol/footnote pass counts, which need a bisect first (`multicol` +40% since `5765912cc`/`5a2698cad`).

## The float scans no longer look through float-free subtrees

Once a document has any float, `HasFloatedBoxes` cannot short-circuit, and every line's float lookup (`GetLastLeftIntersectingFloatBox`,
`FindNarrowestRightFloatBox`, the placement walk, `LowestFloatTopIn`, the vertical insets) walked every earlier sibling's whole subtree at
every ancestor level: quadratic in the paragraphs, though one float existed. A CPU profile of `multicol` put 27% in those walks.

`CssBox.FloatFree` (true = no floated box anywhere in the subtree) is set by `MarkFloatFreeSubtrees` at the start of each layout pass when the
document has a float, and the scans skip a marked subtree. False means unknown, so any box made after the mark is simply scanned. Anything put among
a box's children that is floated, or has children and is not marked, clears the mark on every box above it (`NoteAttached`, called from the
constructor, the `ParentBox` setter and `SetAllBoxes`; a repeated table header put back goes through the setter). A box that is its own parent (a
test builds one) ends that walk.

`multicol`: 544 ms (v0.9.26) to about 94 ms; 20 paragraphs 17,641 visits, 80 paragraphs 272,833 before (15.5x for 4x) and linear after
(`OneFloatAmongManyFloatFreeParagraphs_FloatScanWorkGrowsLinearlyWithTheDocument`). All 245 showcases have identical page content streams.
The one place a skipped subtree still matters is the right float's test of inline-level boxes on its own line, so the placement walk does not skip
at the sibling level for a right float.
Still behind v0.9.26 after this: `paged_media_footnotes` (about +30% time, +35% allocation) and several other paged-media showcases.
