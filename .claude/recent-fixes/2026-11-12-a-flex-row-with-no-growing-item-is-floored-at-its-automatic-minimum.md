# A flex row with no growing item is floored at its automatic minimum

**#2329.** `ResolveAutoMinimums` measured min-content only for a row that overflowed or had a growing item, so
`width:90%` beside `flex:0 1 0` holding a 150px word in a 400px row stayed 360px and 0px (Edge: 250px and 150px). Spec
sections 4.5 and 9.7: each item's hypothetical size is its base size clamped by its automatic minimum, and the line is
shrunk when those sum past the container. Chromium (`flex_item.h`, `LineFlexer`), Gecko (`SetFlexBaseSizeAndMainSize`) and
WebKit all clamp before choosing grow or shrink.

**Change.** A row (main axis on physical X) now goes through `ResolveAutoMinimums` whatever it does; `DistributeFlex` already
shrinks a line whose raised sizes no longer fit, and now also holds a raised item at its minimum when the line still fits
(`Math.Max(final, AutoMinMainSize)` in the plain path). A column is deliberately left alone: in an auto-height column every
item is already its content height, so the minimum would only cost a second layout per item.

**The risk the gap file named** (it changes every fitting row) was checked by rendering all 252 harness showcases to SVG
before and after: the 244 SVGs are byte-identical (the PDFs differ only by timestamp). Engine suite 19697 and PeachPDF.Tests 1925 pass.

**Not done.** The cost is one min-content measurement per item of every row that fits; no benchmark was run.
