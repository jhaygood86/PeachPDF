# Inline-only multi-column containers columnize

**Load-bearing idea:** `CssBox.LayoutContents` sends a multi-column box to the columns engine only when it has a block-level child, and that choice rests on `DomUtils.ContainsInlinesOnly` ("every child is inline"). A container whose children are all text therefore took the inline branch and never reached `CssLayoutEngineColumns`. The fix is a box-generation step: `DomParser.CorrectInlineBoxesParent` wraps such a run in an anonymous block, the shape the engine fragments. Widening the dispatch instead is wrong. The columns engine fragments block-level content, and handed a box of bare inline content it drops words.

**Floats:** a float joins the wrapped run, as it does in any block, so the inline flow places it beside the text. An earlier version kept floats out of the run, so the columns engine placed each float in its own column and nothing moved the text on its line beside it. The float then covered those words. [The overlap fix](2026-10-14-a-float-after-text-moves-the-text-beside-it.md) removes that for every container. A box holding only floats is not wrapped: it already reaches the engine through `CssBox`'s float disjunct, and a test pins that.

**Scroll containers:** `CssBox`'s `suppressMonolithicBreaking` keeps `main`'s exemption for a box that dispatches to the columns engine. Monolithic suppression detaches the fragmentainer, and with it detached the columns engine no longer distributes content: a fixed-height `overflow: hidden` container's columns collapse into one. Before the wrapper, an inline-only container never dispatched and was suppressed, so the exemption did not matter for it. The wrapper now sends it down the columns path, so it is no longer suppressed and splits across page boundaries exactly as a block-content multi-column scroll container already did on `main`.

**Vertical writing modes** keep the inline path. Measured on a 225pt x 90pt `vertical-rl; column-count: 2` box holding one run of text: `CreateVerticalLineBoxes` keeps the content inside the box (72pt x 58pt), while the columns engine stacks it into a single 511pt run that leaves the box entirely.

**Found by running it:**

- 21 generated block-content `overflow` containers with columns render identically to `main` (word positions and page count).
- 39 generated inline-only `overflow` containers render identically to `main` with a hand-written `<div>` inside them.
- `overflow: scroll; height: 660pt; columns: 2` with 30 lines: two columns, word positions matching Chrome.
- Not reproduced: a reported hang on the float-first shape (`float: left; clear: right`, 649px tall, inside a 300px `column-fill: auto` container). On this machine it did not hang, across 150 generated float-heavy documents, a sweep of word counts, font families, whitespace layouts, page sizes and spacer heights (including a container crossing a page boundary), on this branch and on the PR head.
- Pre-existing and reachable on `main` through a hand-written `<div>`, not introduced here:
  - A float that follows text on the same line was drawn over the words before it, in a plain block and a multi-column container alike. Fixed in [the overlap fix](2026-10-14-a-float-after-text-moves-the-text-beside-it.md).
  - In a fixed-height `overflow` multi-column container, the second column can skip words. In 5 of 39 generated inline-only documents, column 2 starts at a later word than Chrome's (for example `w076` where Chrome has `w041`).

**Deliberately not done:**

- Keeping an inline-only scroll container monolithic while it is columnized. That needs the columns engine to lay out inside a detached fragmentainer, which it does not do.
- Fixing the two pre-existing columns-engine defects above. They live in the columns engine's line and fragment handling, not in the box generation this change touches.

**Evidence:**

- Full net8.0 suite with coverage: 17,172 passed, 9 skipped, 0 failed.
- diff-cover against `main` at the 90% gate: 100% of the changed executable lines (`CssBox.cs`, `DomParser.cs`).
- Solution `Rebuild`: 0 compiler warnings. It reports 12 NU1902/NU1903 advisories for `System.Security.Cryptography.Pkcs` from `PeachPDF.csproj`, which this change does not touch.
- New tests fail on the PR head or on a mutant, and pass here: the scroll-container distribution (block children and inline-only) fails on the PR head; the middle-float test fails on the previous float exclusion, which drew words over the float; the float-only guard fails on the mutant that wraps float-only containers.
- html-to-pdf vault test project (built against this working tree): 8 of 8 passed.
