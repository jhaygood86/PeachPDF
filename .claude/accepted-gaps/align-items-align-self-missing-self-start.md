# `align-items`/`align-self`: `self-start` is rejected

Tracking issue: [#644](https://github.com/jhaygood86/PeachPDF/issues/644).

Per [CSS Box Alignment Module Level 3 §5](https://www.w3.org/TR/css-align-3/#self-alignment),
`align-items`/`align-self`'s `<self-position>` grammar includes `self-start` alongside `self-end`
(both physical, container-relative keywords distinct from `flex-start`/`flex-end`, which are
flow-relative).

PeachPDF's cascade-time keyword acceptance (`Map.AlignItemKeywords`/`Map.AlignSelfKeywords`) currently
omits `self-start` - an authored `align-items: self-start` is rejected and falls back to the initial
value `normal`. Neither `CssLayoutEngineFlex.ComputeCrossOffsets`/`ShrinkColumnItemToContentWidth` nor
`CssLayoutEngineGrid.AlignmentOffset` have a `self-start` case, so even if accepted at the cascade layer
it wouldn't currently produce the correct result.

Note the legacy CSS-OM validation layer's `Map.AlignItems` (used by `AlignItemsProperty`/
`AlignSelfProperty`, a separate code path from `CssBox`'s cascaded style) *does* already include
`self-start` - the cascade-facing maps are deliberately narrower, matching what the layout engines
actually dispatch.

**Deliberately out of scope.** Fixing this means adding a real dispatch case to both layout engines'
alignment switches (distinguishing `self-start` from `start`/`flex-start`, which differ under a
writing-mode/flow-relative distinction) - a real layout feature, not a doc-accuracy fix.
