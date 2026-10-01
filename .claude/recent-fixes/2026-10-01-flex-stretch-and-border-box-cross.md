# `stretch` sizing and border-box column flex cross size (#1541, #1542)

**#1542.** `CssLayoutEngineFlex` took a column container's cross size from `containerWidth`
(`GetBoxWidth`'s used `width`), which is the *border-box* width under `box-sizing: border-box`, so
`align-items: center` centred around a point shifted right by padding-left + border-left. It now uses
`ClientRight - ClientLeft`, the content width in both box-sizing modes.

**#1541.** `stretch` / `-webkit-fill-available` / `-moz-available` are accepted on width, height,
min-/max-width/height (both the CSS-OM converters and `css-properties.json`) and `flex-basis`
(`FlexBasisKeyword.Stretch`). Load-bearing idea: `CssBox.ResolveStretchSizes` rewrites the keyword, on first layout
use, into `calc(100% - margins - border/padding)`. Because it becomes an ordinary percentage, the spec's
"indefinite axis behaves as auto / 0 / none" rules and the page-aware percentage basis come for free, with no
stretch-specific code in the layout engines. Hooks: `PerformLayoutPrologue`, `GetBoxWidth`, `GetBoxHeight`, and flex
`MeasureItem`/`RederiveItem` (the flex hypothetical size reads `Width` before the item's own prologue). `flex-basis: stretch`
is handled directly in the flex engine as container main size minus main margins.

Traps: a block-axis margin that collapses through a parent with no border/padding is treated as zero (spec note), so a
`height: stretch` child of a plain block fills it exactly. Found while testing and left alone: `height: 100%` in a parent
with a border resolves against the border box (child 202 for a 200pt parent) - a pre-existing percentage-basis bug, not stretch-specific.
Evidence: full `PeachPDF.Tests` net8.0 Release suite green; new tests in `FlexboxIntegrationTests`.
