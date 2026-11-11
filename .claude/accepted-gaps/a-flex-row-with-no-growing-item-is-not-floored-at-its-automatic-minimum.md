# A flex row with no growing item is not floored at its automatic minimum size

`ResolveAutoMinimums` (CssLayoutEngineFlex) measures min-content only for a line that overflows or has an item with `flex-grow` above 0, so a line of items that fit their base sizes and none of which grows is never floored: `width:90%` beside `flex:0 1 0` holding a 150px word in a 400px row is 360px and 0px, where a browser (CSS Flexbox 1 §4.5 and §9.7, the hypothetical sizes sum to 510px, so the line shrinks) gives 250px and 150px. The same code path as the fix for a growing line would close it (let a line with free space and no growing item through `ResolveAutoMinimums`, and `DistributeFlex` already shrinks a line whose raised sizes no longer fit); it was left out because it changes every row that merely fits, which the corpus sweep behind the growing-line fix did not cover.

Tracked in #2329.
