# A percentage height caps a column flex item's automatic minimum, and a re-laid-out item is remembered

**#2332.** `ResolveColumnAutoMinimums` skipped a `height` ending in `%`, so a `height: 30%` item in a 240pt column kept its
150pt of content as its minimum (Edge: 72/105/63, PeachPDF: 150/56.3/33.8). A percentage is a specified size only when its
basis is definite (css-sizing-3; Chromium `ResolveMainBlockLength` turns an indefinite one into "no cap", Gecko `IsAutoBSize`),
so it now caps the minimum when `CssLayoutEngine.IsHeightDefinite(_flexBox)` and is ignored in an auto-height column.

**The trap found by running it.** The cap alone made the item's box 0pt tall while its siblings sat where a 72pt item would
put them. `ResolveColumnAutoMinimums` measures an item's content by laying it out at `height: auto`, then lays it out again at
its hypothetical size (0 for `flex: 1 1 0`). `DistributeFlex` decided whether to lay the item out at its final size by
comparing it with `NaturalMainSize`, the size the box was first *measured* at, which had stopped being what the box holds.
The cap made the final size (72) equal that natural size (72), so the re-layout was skipped and the box kept its 0pt layout.
Before, the final size (150) happened to differ, which hid it. `FlexItem.LaidOutMainSize` (set by `ResizeItem`) and
`CurrentMainSize` now carry what the box was really laid out at, and both re-layout checks use it.

**Evidence.** `FlexColumnPercentageMinimumTests` (72/105/63, the length-height control, the auto-height column). Engine suite
19693 and PeachPDF.Tests 1925 pass. The accepted-gap file for this is deleted and the docs limitation is rewritten.
