# Grid `fr` rows share a definite height, and auto tracks honor the automatic minimum's exemptions

**Before.** A grid with a definite `height` and `fr` rows (`grid-template-rows: 1fr 1fr`, `auto 1fr`) sized each row to its content, so the
rows did not fill the grid and the space was left unused below them. An `auto` column holding a `overflow: hidden` item, or an item with
`min-width: 0`, was still floored at the item's min-content width.

**Now.** The `fr` rows share what the other rows leave, each floored at its items' automatic minimum height (`minmax(0, 1fr)`, a scroll
container or `min-height: 0` opt out), and `0.5fr` takes half the space. An `auto` column no longer floors a scroll container or a
`min-width: 0` item at its min-content. A grid with an auto height is unchanged.

Part of #2174; the replaced column item case stays in the accepted gap.
