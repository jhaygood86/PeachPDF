# Vertical-first three/four-value `background-position`

`BackgroundPositionGrammar.ParseTriple`/`ParseQuadruple` matched only horizontal-group-first (`left 10px top`). The grammar is `[ ... ] && [ ... ]`, so the vertical group may lead. Both functions now try the mirrored shapes after the existing ones, returning `AuthoredOrder = { y, x }` so the CSS-OM serialization keeps the author's order. The grammar is shared by the CSS-OM converter and `BackgroundLayerResolver`, so the render side needed no change, and the `background` shorthand picks it up because it hands its position tokens to the same converter.

The new shapes refuse an offset after `center` (the spec's `center` takes none). The existing horizontal-first shapes still accept `center 10px bottom`, which the spec does not; that was left as it is, to keep this change to the reported gap.

Evidence: `BackgroundLayerShorthandTests` (accept in longhand and shorthand, serialization verbatim, invalid shapes still dropped) and a tile-position integration test (`top 10pt left` lands 10pt below the padding-box top).
