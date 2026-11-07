# A negative `line-height` is applied where a browser drops the declaration

`line-height` accepts no negative number, length or percentage (CSS Inline Layout 3), so `line-height: -0.5` is invalid and means the inherited value; here it gives negative-height lines and a block 0 tall, so the next block is drawn over it (tracking issue #2286). A negative percentage now does the same since it resolves at all.
