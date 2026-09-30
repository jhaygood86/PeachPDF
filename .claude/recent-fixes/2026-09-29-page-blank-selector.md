# `@page :blank` (issue #1079)

`:blank` matches a page a directional break inserted, resolved from the same decision that inserted
it: `HtmlContainerInt.IsReservedBlankSlot(slot)`, never "this page has no content" (which would be wrong
per css-page-3 §5.1 and inherit the #148 page-number drift).

- `PageRuleResolver.GetOrderedApplicableRules` takes `isBlank`; `:blank` joins `:first` in one
  declaration-ordered `pageSpecific` list applied last (same specificity tier, above `:left`/`:right`).
- Threaded through paint (`PdfGenerator`), the running-element margin pass (`HtmlContainerInt`) and
  `PageGeometryTable.Compute`, keyed by the slot index. Footnote-area/counter-reset slot lookups do not
  pass it (a footnote never lands on a blank page).
- Known residual: `PageGeometryTable` may compute a slot's band before that slot's blank reservation is
  recorded, so a `:blank` margin/size override is only guaranteed for margin boxes and page style, not
  for the band geometry.
- Tests: `DirectionalPageBreakIntegrationTests` (header absent on the inserted page only, not matched
  by an ordinary forced page), `PdfGeneratorSelectPageRuleTests` (precedence).
