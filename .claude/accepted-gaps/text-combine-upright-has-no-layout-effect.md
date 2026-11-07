# text-combine-upright and text-emphasis in vertical text have no effect

`text-combine-upright` (`all`, `digits <n>`) is parsed, inherited as specified (not inherited) and stored, but does not compose characters into one upright cell: that needs vertical-text layout (word width and height inside a vertical line), which this engine does not have (see [no-vertical-writing-mode-layout](no-vertical-writing-mode-layout.md)).

`text-emphasis-*` marks are drawn for horizontal text only. In vertical writing modes no marks are drawn, and the `left`/`right` half of `text-emphasis-position` is stored without effect. Layout also reserves no room for marks (CSS Text Decoration 4 §4.4 asks a UA to enlarge the line when they would overflow), and `text-shadow` is not applied to them.

`text-decoration-skip` accepts the full grammar but only its initial `objects` has an effect.

Tracked in #2278; do not relitigate without a vertical-layout plan.
