# Top/bottom @page margin percentages resolve against the page height (#1931)

css-page-3 §6 resolves `margin-top`/`margin-bottom` percentages against the page *height*; every side
used the width. `PageLengthContext` gained an optional `HundredPercentHeightPt` (0 = unset, falls back
to the width so callers that build a context by hand keep working) and `ForVertical()`; both
`DomParser.ApplyPageStylesOnce` (base rule) and `PageRuleResolver.ResolvePageMargins` (per-page rules)
resolve top/bottom through it. Trap: the base-vs-per-page identity test relies on both paths using the
same basis, so any new margin path must go through `ForVertical()` too. Per-page `size` overrides still
resolve against the base sheet's height, as they already did for width. The showcase comment
claimed width-relative behaviour and was corrected.
