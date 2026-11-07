# A `line-height: calc()` containing `lh` is compounded when its children inherit it

`<div style="line-height:30px"><div style="line-height:calc(1lh + 8px)">` is 46px tall where a browser has 38: the deferred `calc()` is re-resolved on each box against its parent's already-resolved line-height (tracking issue #2287). A bare `lh` value is converted eagerly by `ResolveLineHeightValueComputation` and is right.
