# A `float-reference: column` page float in a container holding only out-of-flow children has a column (#1356)

`CssLayoutEngineColumns.LayoutOutOfFlowChildrenOnly` (a multi-column container with nothing in flow) laid the float out with no fragmentainer, so the float never recorded which column it
was in (`HtmlContainerInt.NotePageFloatColumn` reads the current fragmentainer's `ColumnKey`) and resolved against the page: a `float: top` sat at the top of the page, above the
container, where css-page-floats-3 §3.1 only permits that fallback for an anchor that is not inside a column.

The method now makes the first column the way `FillColumns` makes each one: a `FragmentainerContext` over the container's band (to the page's foot less its reservations) with its
`ColumnAreaKey`, any top strip a previous attempt asked for, and a `ColumnFragmentainerRecord`, entered around the float's layout. Only when the container holds a column-scoped
page float, is on a real page grid, and is not nested in another column (a nested one keeps its existing path). `ColumnTopFloat_InAContainerHoldingNothingElse_...` (top, Y 40, the
column band's top, not the page's 0) and its bottom twin (page foot less the float) are the tests; the eleven existing column-scope tests pass unchanged.

Release engine suite 19,398, `PeachPDF.Tests` 1,807, zero warnings.
