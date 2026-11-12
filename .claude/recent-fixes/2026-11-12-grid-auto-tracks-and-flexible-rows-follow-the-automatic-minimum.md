# Grid auto tracks keep the automatic minimum's exemptions, and `fr` rows are expanded against a definite height

**Auto tracks (#2174, third bullet).** Only a bare `fr` column applied `AutoMinimumOf` to its single-column items; an `auto` or
`minmax(auto, X)` column floored them at min-content, so a scroll container or an item with `min-width: 0` could not shrink its track. Spec
§12.4/§12.5: the items' minimum contribution to a track whose min function is `auto` is their automatic minimum (§6.6), which a scroll
container and an authored min exempt; Chromium `CalculateIntrinsicMinimumContribution` and Gecko `ContentContribution` gate it the same way. A
`min-content`/`max-content` minimum still takes the content contribution. `HasAutoMinimum(def)` is the one predicate, used by the column
measurement and the intrinsic-width walk.

**Found by probing, bigger than the issue said.** `1fr 1fr` rows in a 100pt grid were 15pt and 15pt, not 50pt and 50pt: rows were never
`fr`-expanded, so "rows have no automatic minimum" was a symptom. `ExpandFlexibleRows` is the row twin of the column code in `SizeColumnTracks`
(`FindFlexFraction` over the rows' bases). A flexible row's base is `FlexRowBaseContribution`: the items' automatic minimum height
(`AutoMinimumHeightOf`) for a bare `fr`/`minmax(auto, fr)`, the length for `minmax(<length>, fr)`, content for `min-content`/`max-content`. Only
with a definite block size (`hasDefiniteBlock`) and not in a vertical grid; an auto-height grid keeps content-sized `fr` rows.

**Flex factor sum below one.** `FindFlexFraction` now divides by `max(sum, 1)` (§12.7.1), so `0.5fr` takes half the space, as for flex items.

**Spanning items.** An item spanning a flexible track has an automatic minimum of 0 (§6.6: none of the spanned tracks may be flexible), which is what
the simplified span distribution already did, so nothing changed there; the old gap text overstated it.

**Not done.** Replaced column items (see the accepted gap) and the indefinite-height `fr` fraction.

**Evidence.** `GridAutoMinimumTests`, `GridFlexibleRowsTests`. Engine suite 19704 and PeachPDF.Tests 1925 pass; all 252 showcases render
byte-identical SVGs (none has a definite-height grid with `fr` rows, or an auto track holding a scroll container).
