# A table with nothing in it is not collapsed in quirks mode

The Quirks Mode Standard 3.10 gives a `table` with no row group, header group, footer group or caption child, and no column group holding a column, a used `height` of 0 and a used `border-style` of `none`, in quirks mode. PeachPDF lays such a table out as it does in standards mode (its borders and padding make a box). It would have to be done in the cascade (the used values replace the author's) or by rewriting the table's style at layout time, and the cascade shares one computed style among identical siblings while the condition reads the children, so it is not a one-line change. An empty table is rare in a document anyone wants to print.

Part of [document modes](../../docs/html-css-support.md#document-modes-quirks-mode).
