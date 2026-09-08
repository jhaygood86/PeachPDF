# The left float scan stops at its formatting context, like the right one already did

`FindNarrowestRightFloatBox` breaks its ancestor walk at a formatting-context boundary — a float
cannot affect content outside its own (CSS 2.1 §9.5, css-display-3 §2.1). `FindIntersectingFloatBox`,
the left-side point-collision walk, did not: it ran to the document root and scanned every preceding
sibling on the way. Same rule, same shape, one side missing it.

## What running it turned up

- **It is the dominant cost of float layout, not a rounding error.** On a synthetic document of 40
  two-column grids each holding one float: 237,757 box visits across 2,880 calls, 83 per call. With
  the boundary check, 1,200 visits — the same 2,880 calls now examining under one box each. The
  right-side walk, identical but for this rule, has always been ~7 per call.
- **The starting-level exemption is load-bearing, and it is the opposite of the right-side walk's.**
  This walk's `reference` is the box being *placed*, and a float is itself a formatting-context root
  for its own contents — breaking before the first level's siblings are scanned shields a float from
  the very siblings it must be positioned against. `FloatLayoutRegressionTests`'
  `FloatRight_InNarrowerNestedBlock_AvoidsAWiderAncestorFloatRightSibling` catches it immediately.
  `FindNarrowestRightFloatBox` needs no exemption because its `reference` comes from `FlowBox`'s
  word-flow loop and is never a float.
- **No output changes.** The walk was climbing past boundaries and finding nothing to collide with,
  so this is wasted work rather than a visible defect — I could not build a document where the leak
  moved a line. Pitched and tested as what it is: a cost fix, asserted on
  `FloatScanBoxVisits`/`FloatScanCalls` rather than on geometry, and on a counter rather than elapsed
  time for the reason `FloatLayoutRegressionTests`' own class comment gives about wall-clock bounds.

## Evidence

`FloatLayoutRegressionTests.LeftFloatScan_StopsAtAFormattingContextBoundary` asserts under 10 visits
per call; without the break the same document reports 82.6. Full suite green on net8.0 (10,253),
0 new build warnings.
