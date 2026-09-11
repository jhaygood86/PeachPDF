# 2026-09-11 — Charts.css labels: four defects behind one symptom

The `charts_css` showcase rendered every chart's bars correctly and **none** of its labels: no `Q1`–`Q4`
axis labels, no `$42M` bar values, no `JavaScript`/`Python` row labels, and (once those came back) the
area/line data labels sat on the chart baseline instead of at their data points. Four independent
defects, found one behind the other.

## 1. `*::before`/`*::after` synthesized a pseudo-element on anonymous text boxes

`CssData.DoesSelectorMatch`'s pseudo-element arm creates the `::before`/`::after` box as a side effect of
matching, and `DoesSelectorMatch(AllSelector)` is structural — `node is { IsRoot: false }` — so the
universal selector reached **every** box, including the anonymous box a raw text node becomes.
Charts.css's own `.charts-css *::before, .charts-css *::after { box-sizing: border-box }` therefore hung
two empty pseudo boxes off every text box in the document.

The damage was not cosmetic, and the chain is worth knowing because nothing about it is local:

- the text box now held **both** its own `Words` and child `Boxes`;
- `CssLayoutEngine.FlowBox` flows a box's own words only when it has no child boxes
  (`if (boxes.Count is 0 && box.Words.Count > 0) boxes = [box];`), so the word was never positioned by an
  inline flow again;
- `CreateLineBoxes`'s prologue still called `AwaitPlacement()` on it every pass, and only
  `CssRect.Top`'s setter clears `AwaitsTheNextFragmentainer` — so the flag survived whenever the box's
  final placement happened to need no `OffsetTop` (`AssignLocations` skips a move under 0.01);
- `FragmentEmitter.BuildDraft` skips a word that awaits the next fragmentainer, so no `TextFragment` was
  emitted and the text simply was not in the PDF.

The box laid out at the right size the whole time. Backgrounds and borders painted. Only the glyphs were
missing, which is why this read as a font or paint problem and was neither.

Fix: `if (box.HtmlTag is null) return false;` before the synthesis switch — a pseudo-element is generated
on an *element* (Selectors 4 §3.3).

**How it was found:** a `ddmin` delta-debug over the 310 top-level blocks of `charts.css`, scripted
against the CLI plus PyMuPDF word extraction. Worth repeating, with one warning: the first reduction
found a *different* real bug (`tbody { display: flex }` alone, with default `table-row` children) because
ddmin is free to drop the blocks that produce the original mechanism. Pinning the structural blocks and
re-reducing is what landed on the real trigger.

## 2. `margin-block-start: auto` on an absolutely positioned box resolved to 0

`CssBox.ActualMarginTop`/`ActualMarginBottom` return 0 for a keyword value, and nothing implemented CSS
2.1 §10.6.4's "solve the equation for the `auto` margin". Charts.css's column/area/line axis labels are
exactly that shape — `th { position: absolute; inset: 0; height: var(--labels-size);
margin-block-start: auto; margin-block-end: calc(-1 * var(--labels-size) - var(--primary-axis-width)) }`
— so every label was drawn across the *top* of its chart instead of under the primary axis.

The first implementation solved this in `CssBox.PerformLayoutImp`'s absolute-positioning tail, beside
the existing `bottom`-with-auto-`top` branch. Two follow-up cases matter: fixed boxes obey the same
equation, and an auto-height positioned containing block does not have its final height while the child
is laid out. The resolver now handles absolute and fixed boxes uniformly, records any provisional
vertical translation, and revises it by the delta after the containing block reaches its final height.

## 3. CSS 2.1 §9.2.1.1's anonymous block was created inside flex containers

`DomParser.CorrectInlineBoxesParent` wraps a run of inline siblings in an anonymous block whenever a box
mixes inline-level and block-level children. That is a **block container** rule. Applied to a flex
container it swallowed an item: the auto-sized wrapper became the flex item, so the child's own
`width`/`height` sized nothing.

Charts.css's area/line cell is precisely that shape — an inline `td::after` spacer
(`content: ""; width: 100%; height: calc(100% * var(--end))`, the box that lifts each data label to its
data point) beside a `display: flex` `.data` label — so every label collapsed onto the chart baseline.

Fix: skip the block-container wrapper inside a flex/grid container and, in its place, join only what
css-flexbox-1 §4 actually asks for — each contiguous sequence of child **text runs** — into one item.
Elements and pseudo-elements remain items in their own right.

**The trap, paid for once:** the first attempt at that wrapper ran on *every* text sequence, including a
lone one. That re-created the exact defect this section is about, one level down. The HTML parser already
gives every text node its own tagless box, so a single run is already the anonymous item — wrapping it
again puts an auto-sized box between the container and the text, and the wrapper's measured width becomes
the item's. With `overflow-wrap: anywhere` inherited from the chart row, Charts.css's labels then broke
mid-word: `Q`/`1`, `Ma`/`r`, on two lines. The epsilon fudges that appeared next in `MeasureItem` and
`ShrinkColumnItemToContentWidth` (`+ 0.01`, and a literal `+ 0.00`) were chasing that symptom; both are
gone, and the wrapper now fires only where it changes the item count — adjacent text nodes, which the
parser produces when a comment or a dropped element separates them.

Floated children are items too (§4: `float` has no effect on one). Collecting them is only half of it:
while the box still answered `IsFloated`, the float machinery displaced it *as well*, so a float between
two ordinary items dropped onto its own row and left a hole where flex had already placed it. `float` is
therefore coerced to `none` on an item in the cascade (`DomParser.NormalizeFlexOrGridItem`), which leaves
the item-collection filter reading plain `IsExcludedFromFlow` and keeps `Floating.Footnote` — genuinely
out of flow, and not an item — untouched.

**Deliberately not done:** blockifying inline-level flex items at cascade time, which the spec also
requires. It was tried, and it regresses replaced elements: a replaced box takes its size from the
phantom word carrying its content, and that only reaches the box through inline flow, so as a block-level
box it fills its containing block instead (CSS 2.1 §10.3.4's intrinsic width for block-level replaced
content is not implemented here). Five tests caught it —
`FlexboxIntegrationTests.Column_AlignItemsCenter_ReplacedItem_Centers` is the clearest. The flex/grid
engines already lay every item out blockified (`PerformLayoutBlockified`), so an inline item's own
`width`/`height` already apply; the box tree, not the computed value, was what needed fixing.

`DomParser.BlockifyFlexOrGridItem` is still added, but scoped to **layout-internal** displays only
(css-display-3 §2.6's table-internal set → `block`). Those have no replaced-element interaction and fix
their own text-loss bug: a `display: table-row` flex item stayed layout-internal, so no engine ever laid
it out — `<tbody style="display:flex">` of ordinary `<tr>`s rendered completely blank.

## 4. An unresolvable percentage `height` gave a flex/grid container a definite main size of 0

Both engines did `hasDefiniteHeight = IsValidLength(Height) || TryGetAspectRatioHeight(...)` and then
`GetBoxHeight(box) ?? 0`. `GetBoxHeight` returns **null** for a percentage height whose containing block
is not itself height-calculated — CSS Box Sizing 4 §5 says that behaves as automatic — so `height: 100%`
against an auto-height parent produced "definite, and it is 0". A column container then stacked every
item at its content origin: all of them drawn on top of each other.

Fix: definite means it actually resolved. Same change in `CssLayoutEngineFlex` and `CssLayoutEngineGrid`.

Not fixed, and not the same bug: a percentage height still does not resolve against a containing block
whose height is definite but not yet *calculated* at that point in the pass (a stretched flex item, say).
`justify-content` on such a container has no free space to distribute. See
[docs/html-css-support.md](../../docs/html-css-support.md)'s Flexbox section.

## Evidence

- `dotnet test --framework net8.0`: 10722 passed, 9 skipped, 0 failed.
- Diff coverage vs `main`: 115 executable changed lines in `src/PeachPDF`, 0 uncovered.
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings.
- All 113 showcases regenerated; `charts_css.pdf` rasterized through **both** PDFium and MuPDF (they
  agree) and compared against the browser rendering of the same HTML. Flexbox/grid showcases spot-checked
  for regressions.
- New tests: `UniversalPseudoElementIntegrationTests` (3), `FlexItemGenerationIntegrationTests` (11
  cases across 9 methods), and six §10.6.4 cases in `AbsolutePositioningIntegrationTests`. Every one was
  confirmed to **fail** with the corresponding source change reverted, the §10.6.4 content-sized
  containing-block case included. The pseudo-element regression asserts on `BoxFragment.Words`, because
  the box tree looked correct while only the fragment tree lost the text.
- `AbsoluteColumnLabelInAStretchedFlexItem_DoesNotBreakMidWord` guards the mid-word split, and getting it
  to reproduce took one non-obvious ingredient: **`font-family: sans-serif` on the fixture's body**.
  Without it the same markup passes either way. The break is decided a fraction of a point either side of
  the text's own width, so the measured font is part of the fixture rather than incidental styling — two
  earlier attempts at this guard asserted on simpler markup and passed with the defect in place, which is
  worse than no test. Check a guard here fails before trusting it.
- Two tests that read files from outside the test project — `charts.css` by relative path, and
  `docs/showcase/charts_css.html`, which is generated, gitignored build output — were removed rather than
  kept: the second would simply fail on a clean checkout, and both duplicate coverage the self-contained
  cases already give.
