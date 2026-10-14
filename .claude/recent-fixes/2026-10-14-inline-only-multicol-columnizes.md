# Inline-only multi-column containers columnize

**Load-bearing idea:** `CssBox.LayoutContents` sends a multi-column box to the columns engine only when it has a block-level child, and that choice rests on `DomUtils.ContainsInlinesOnly` ("every child is inline"). A container whose children are all text therefore took the inline branch and never reached `CssLayoutEngineColumns`. The fix is a box-generation step: `DomParser.CorrectInlineBoxesParent` wraps such a run in an anonymous block, the shape the engine fragments. Widening the dispatch instead is wrong. The columns engine fragments block-level content, and handed a box of bare inline content it drops words.

**Also in this change:** `CssBox`'s `suppressMonolithicBreaking` no longer excludes a box that dispatches to the columns engine. That exclusion conflated "its columns are laid out internally" with "a page boundary may cut it", so a multi-column scroll container with block-level children could split across pages.

**Found by running it:**

- Against `main`'s production code, `InlineOnlyContent_Columnizes` fails (3 of 14 words past the column boundary), and `ScrollContainerEstablishingColumnsWithBlockChildren_IsAlsoSuppressed` fails (`FragmentainerPasses` is 3, expected 1). Both pass with the change.
- `InlineOnlyContentUnderVerticalWritingMode_StaysInsideTheBox` passes on both. It is a regression guard for the vertical exclusion. It has not been run against a version without that exclusion.
- `features.html` section 14, rendered with PeachPDF before and after and checked in PyMuPDF and PDFium: before, the sentence runs across the full measure. After, it splits into two balanced columns.

**Deliberately not done:**

- Text beside a float is still not columnized. The wrapper is not applied to a run holding a float, because the float disjunct in `dispatchesToColumnsEngine` already decides that case. This is the narrowed [accepted gap](../accepted-gaps/bare-text-beside-a-float-in-a-multicol-container-is-not-divided-into-columns.md).
- Vertical writing modes keep the inline path. Measured on a 225pt x 90pt `vertical-rl; column-count: 2` box holding one run of text: `CreateVerticalLineBoxes` keeps the content inside the box (72pt x 58pt), while the columns engine stacks it into a single 511pt run that leaves the box entirely.

**Evidence:**

- Full net8.0 suite with coverage: 17,166 passed, 9 skipped, 0 failed.
- diff-cover against `main` at the 90% gate: 100% of 7 changed executable lines, all in `DomParser.cs`. The `CssBox.cs` change removes a condition and adds comments only, so it adds no executable line.
- Solution `Rebuild`: 0 warnings, 0 errors.
