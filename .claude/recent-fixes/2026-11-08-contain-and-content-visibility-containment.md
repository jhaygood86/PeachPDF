# `contain`, `content-visibility: auto`, and `legend` as a block

Closes the rest of the containment issue (#2242) that the fieldset/button change left: `contain: layout|paint|content|strict`, `content-visibility: auto`, and the fieldset-with-legend sample.

## The load-bearing idea

One predicate, `DomUtils.AppliedContainment(box)`, turns the two stored strings (`contain`, `content-visibility`, parsed by `ContainGrammar.Of`) into a `Containment` flag set and removes what cannot apply (no box, non-atomic inline, internal table box other than a cell). Everything else asks it through `AppliesLayoutOrPaintContainment` / `AppliesPaintContainment` / `AppliesLayoutContainment`. The properties are stored as text (like `scroll-behavior`), not as an enum, because the value is a set; the fast path for the initial values is two short string compares, which matters because `ClipsWithoutScrolling` and `EstablishesIndependentFormattingContext` are asked of every box.

What layout/paint containment does here, each at its one existing seam:

- **Independent formatting context** - `DomUtils.EstablishesIndependentFormattingContext`. That predicate already ended the float scans, margin collapse through a box and float containment, so nothing else changed for floats or margins.
- **Clip** - `CssBox.ClipsWithoutScrolling` / `ClipsOverflowHorizontally` / `Vertically`: css-contain-2 §3.4 says paint containment is `overflow: clip` on every `visible` axis, and `overflow: clip` was already modelled that way. A box that is a scroll container is left alone.
- **Containing block** - `DomUtils.FormsAbsoluteContainingBlock` replaced `IsPositioned` in the five places that find an absolute box's containing block (`GetNearestPositionedAncestor`, `ClippingContainingBlockOf`, the two abspos margin passes in `CssBox`, `AbsoluteContainingBlockOf`). A `position: fixed` descendant of a contained box is made `absolute` where the tree is built (`DomParser.NormalizeFixedInContainment`, after the flex/grid item normalisation, when every ancestor's cascade has finished): fixed placement is woven through fragmentation (repeats on every page), and an absolute box resolved against an ancestor is exactly what the spec asks.
- **Stacking context** - `DomUtils.IsStackingContextBox`.
- **No baseline** - `CssLayoutEngine.AtomicInlineBaselineOf` returns the bottom margin edge for a layout-contained inline-block/flex/grid.
- **`html`/`body` propagation (§2)** - `HtmlContainerInt.ResolveCanvasBackground` stops body's background becoming the canvas when `html` or `body` is contained (the root's own still is), and `MonolithicContent.IsViewportPropagationSource` stops body being the viewport's overflow source. Nothing propagates `writing-mode`/`direction`/`text-orientation` from `body` in this engine, so there was nothing to disable there.

`legend { display: block; padding-inline: 2px }` is the HTML UA sheet (§15.3.3) and what Blink, WebKit (in its style adjuster) and Gecko do. There is still no "rendered legend" set into the fieldset border; the legend is a block at the top of the content.

## Found by running it

- `ClipsOverflowHorizontally` is `true` for a box that clips nothing at all (it assumes "not clipping without scrolling means a scroll container"), so a test of "does not clip" must ask `ClipsItsOverflow` / `ClipsWithoutScrolling`, not the axis helpers.
- A `display: contents` shell is not in the box tree, so `FindById` returns null for it; the predicate also returns `None` for `contents`/`none`.
- An inline-block's baseline is read from the rectangle laid out so far, so a contained one with a fixed `height` is still aligned by the bottom of its content rectangle, as `overflow: hidden` already is; the baseline test compares the text position and sees only the descent.
- Cross-browser source (Blink, WebKit, Gecko) was read for the exclusions, the fixed containing block, the stacking context and the paint clip; Gecko's formatting-context code and WebKit's stacking-context handling for containment could not be read, so those two follow the spec text and Blink.

## Known trade-offs

- `NormalizeFixedInContainment` gives a `position: fixed` box inside a contained box the used position `absolute` where the tree is built (a fixed box whose containing block is not the page is placed exactly like an absolute one, and fixed placement is woven through fragmentation). Checked rather than assumed: (1) it cannot go stale, because `contain` and `content-visibility` are on `InteractionRestyler`'s structural list, so a hover or focus rule that changes them is never applied in place - the document is loaded again and the normalisation runs over the new styles (`AFixedBoxFollowsItsAncestorsContainmentWhenAHoverTurnsItOnAndOff`); no other in-place path (`StyleUpdater`, live animation) writes either property; (2) the one thing a fixed box has that an absolute one lacks, its own stacking context (css-position-3), is kept through `CssBox.FixedInContainment`, read by `DomUtils.IsStackingContextBox`. Not asserted: repeating a fixed box on every page - the engine does not draw even an ordinary fixed box's background on a second page, so a test of "repeats outside, does not inside" would test something this change did not make. Ancestors with a `transform`, `filter` or `perspective` also make a fixed box's containing block (css-transforms-1, filter-effects-1); the clip chain follows that (`ClippingContainingBlockOf`) but placement does not, which this change does not touch.
- `AppliedContainment` re-parses `contain` for a box that has one (no allocation now: a span tokenizer) every time a predicate asks; boxes with the initial values pay two short string compares. Caching it on `DerivedStyle` would remove the repeat if a profile ever shows it.
- The harness stops partway through the showcases on a machine without the bundled Thai/Khmer fonts (it threw while saving `dictionary_line_breaking`), so the before/after showcase comparison covered only the first 74; the engine (19,146) and PeachPDF (1,752) suites and the new containment showcase, rasterized with MuPDF and PDFium, carry the evidence.

## Not done, tracked

Size and style containment, `content-visibility` skipping, `contain-intrinsic-size`, `overflow-clip-margin`: see [size-and-style-containment-have-no-effect.md](../accepted-gaps/size-and-style-containment-have-no-effect.md) and [content-visibility-never-skips-its-contents.md](../accepted-gaps/content-visibility-never-skips-its-contents.md).
