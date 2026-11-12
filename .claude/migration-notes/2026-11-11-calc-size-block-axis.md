# calc-size() on height/min-height/max-height with a keyword basis now applies

Before: `height: calc-size(auto, size + 20px)` behaved as `height: auto`. Now the expression is applied to the content height
(content height plus 20px), and likewise for `min-height`/`max-height` and the `min-content`/`max-content`/`fit-content` bases.
