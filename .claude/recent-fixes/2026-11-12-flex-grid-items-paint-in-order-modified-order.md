# Flex and grid items paint in order-modified document order (#2383)

Layout already sorted flex/grid items by `order`, but `StackingOrder.Flatten` yielded fragment children in document order, so overlapping items and PDF text extraction disagreed with browsers (css-flexbox-1 §4.3, css-grid-1 §6.2).

`StackingOrder.InPaintOrder` now stably sorts a flex/grid container's children by `order` (ties keep document order; skipped entirely when every order is 0, so the common case allocates nothing). Only paint moves: the structure tree stays in document order (css-flexbox-1 §5.4), and z-index layers still override item order.

Covered by `TaggedPdfPaintOrderTests` (ordered `DrawString` log for flex and grid, ties, negative order).
