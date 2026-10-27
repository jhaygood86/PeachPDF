# `::selection` styling and `SetSelectedRanges` (opt-in)

Phase 1 of the selection roadmap: a selection set with `HtmlDocument.SetSelectedRanges` is painted in the document's `::selection` style when
`HtmlDocumentOptions.EnableSelectionStyling` is on. Off, nothing changes: `::selection` still matches nothing.

**Load-bearing ideas.**
- The style is resolved lazily, at paint time, not in the cascade (`DomParser.ResolveSelectionStyle`). `*::selection { }` is common author CSS;
  resolving it for every box at load would put a detached box on every element of every PDF. The first-line machinery was the model, but it
  is eager because it must be known during layout - a selection never changes a measurement, so it need not be.
- `CssData.MatchesAsFirstLineSelector` became `MatchesAsPseudoSubject(selector, box, name)`, and `GetStyleRulesByOrigin` took a `pseudoName`
  (default `first-line`), so both pseudos share one gather/sort path. A **bare** `::selection` selector is a `PseudoElementSelector`, not a
  compound: the first version only handled compounds, so `.a::selection` worked and `::selection` silently did nothing (the tests caught it).
- Highlight geometry is not recomputed at paint: `TextRunCollector` records each word's character edges *relative to the word's left edge*
  (`TextWordSlice`), `SelectionModel` maps ranges to `(word → selected part)`, and the painter needs only the word's rectangle. Selection and
  `TextRun.CharacterOffsets` therefore always agree.
- The selection colour is applied only when it differs from the element's own colour (the shadow style is seeded from the element, so an
  undeclared `color` reads as the element's). Seeding from the ancestor's selection style instead made a `<strong>` inside `p::selection
  { background }` pick up the paragraph's colour and lose its own.

**Traps.** `DrawWordGlyphs` takes the style from `word.FirstLineStyle ?? box`; a selection must not substitute its shadow box there (wrong
font for an inline element), so it passes a colour override instead. Text-only repaints (`_textOnly`, over rasterised content) must not draw
the selection fill again.

**Not done.** See `.claude/accepted-gaps/selection-paints-only-color-and-background.md`. `::highlight(name)` was in the plan for this phase and
is deferred with it.

**Evidence.** `SelectionStylingTests` (8): fill, partial word, gap between words, colour only inside the selection, option defaults, per-element
rule, option off, no layout change and clearing.
