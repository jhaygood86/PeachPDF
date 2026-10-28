# Named highlights (`::highlight(name)` and the built-in highlight pseudos)

`HtmlDocument.SetHighlight`/`RemoveHighlight`/`ClearHighlights`/`HighlightNames` paint ranges in the rules of `::highlight(name)`,
`::spelling-error`, `::grammar-error` and `::target-text`, under the selection, with `priority`. They reuse the `::selection` pipeline:
`DomParser.ResolveHighlightStyle(container, box, pseudoName)` is `ResolveSelectionStyle` generalised (a detached cascade box per element per
pseudo-element name; `selection` keeps its own two fields on the box, every other name lives in `CssBox.HighlightStyles`),
`CssData.GetSelectionStyleRules` takes the pseudo name, and the painter's `HighlightLayersOf(word)` collects `(part, background, foreground)` layers
lowest priority first (the selection last).

**Load-bearing**
- A functional pseudo-element's selector name is its verbatim text (`highlight(found)`), so matching is the existing `Name == pseudoName` test;
  `SelectorConstructor.NormalizeFunctionalPseudoElement` trims the argument so `::highlight( name )` and `::highlight(name)` are one selector.
- `HasSelectionRules` is now "any rule ends in a highlight pseudo" (`CouldEndInHighlightPseudo`), still a document-level superset, so documents with no
  highlight rule pay nothing.
- `PaintHighlightedWord` replaces the single-selection `PaintSelectedWord`: backgrounds in layer order, then the word drawn once per stretch of one
  colour, split at every edge of a colour-changing layer, the last layer covering a stretch deciding it. With one layer this is exactly the old three clips.
- A highlight no rule styles is skipped; only the selection has default colours.

**Not done**: `text-decoration`, `text-shadow` and stroke in any highlight (see `.claude/accepted-gaps/selection-paints-only-color-and-background.md`),
`::target-text` applied by the engine, moving highlights through a text edit.

**Evidence**: `HighlightTests` (9, pixel reads on the bitmap canvas): fill by name, no-rule and removed highlights paint nothing, colour only inside,
priority and selection order, built-ins below custom, spaced selector, per-element rules, refusals, `ClearHighlights`. All suites green.
