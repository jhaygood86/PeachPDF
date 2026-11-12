# A fixed-height multicol container fills columns of its height (#2238)

`CssLayoutEngineColumns.Layout` capped the column height (`heightCap`) only for a clipping container (#1818), so a
visible-overflow container with a definite `height` and `column-fill: auto` filled its first column to the whole
page budget and content hung below the container. The cap now applies to any definite height; `carry = null`
(drop what does not fit) stays for clipping containers only, so the rest still continues on the next page.

**Tried and reverted:** overflow columns to the right for visible containers. `FlexPerPageReflowIntegrationTests.
FlexContainerInsideAMultiColumnContainer_FillsItsColumnOnEveryPage` showed it moves later pages' content off the page.

**Side effect found by running the suite:** with the height really honored, the dense column-scoped footnote sweep
(`FootnoteColumnConvergenceIntegrationTests`) needs up to nine passes where the guard held at six, so
`maxFootnotePasses` is ten. Before this, those documents' "fixed height" never limited the columns.

Evidence: `MulticolColumnFillAutoTests` (the issue's four-blocks sample, the block after an exact fill, a block taller
than the column; balance with a height still balances); full Engine.Tests and PeachPDF.Tests pass.
