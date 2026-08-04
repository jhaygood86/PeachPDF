# `justify-content`/`align-content`: narrower keyword set than CSS Box Alignment's full grammar

Tracking issue: [#645](https://github.com/jhaygood86/PeachPDF/issues/645).

Per [CSS Box Alignment Module Level 3 §7](https://www.w3.org/TR/css-align-3/#content-distribution) /
§8, the full `<content-distribution>`/`<content-position>` grammar for `justify-content` includes
`start`/`left`/`right`/`stretch` in addition to `flex-start`/`flex-end`/`end`/`center`/`space-between`/
`space-around`/`space-evenly`, and `align-content` additionally includes `start`/`baseline`.

PeachPDF's cascade-time keyword acceptance (`Map.JustifyContentKeywords`/`Map.AlignContentKeywords`) is
narrower than this: `justify-content` currently rejects `start`, `left`, `right`, and `stretch`;
`align-content` currently rejects `start` and `baseline`. Neither `CssLayoutEngineFlex`
(`ComputeMainOffsets`/`DistributeCrossSpace`) nor `CssLayoutEngineGrid.PositionTracks` have dispatch
cases for these values today.

Note the legacy CSS-OM validation layer's `Map.JustifyContentOptions`/`Map.AlignContents` (used by
`JustifyContentProperty`/`AlignContentProperty`, a separate code path from `CssBox`'s cascaded style)
*do* already include the full keyword set - the cascade-facing maps are deliberately narrower, matching
what the layout engines actually dispatch.

**Deliberately out of scope.** Fixing this means adding real dispatch cases to both layout engines
(`start`/`left`/`right` need real positional handling distinct from the existing `flex-start`/
`flex-end`/`center` cases; `stretch`/`baseline` on `justify-content` and `baseline` on `align-content`
need their own layout behavior) - a real layout feature, not a doc-accuracy fix.
