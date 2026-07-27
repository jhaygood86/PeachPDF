# A repeated row group's proxy is indistinguishable from its source by `Display`

_CSS Fragmentation Level 3. Tracker: [#390](https://github.com/jhaygood86/PeachPDF/issues/390)._

`CssProxyBox` inherits its source's style wholesale and copies `Display` explicitly, so a proxy
standing in for a detached repeating `<thead>` **is** `table-header-group` as far as every predicate
in the codebase is concerned — including `CssBox.IsTableRowGroupBox` and
`CssLayoutEngineTable.AssignBoxKinds`'s own `switch`. The proxies also sit in the table's `Boxes`,
which is exactly the list `AssignBoxKinds` reads the markup out of.

So **any code that classifies a table's children has to exclude `CssProxyBox` explicitly**, and the
failure is a crash rather than a drift: the first proxy is taken for `_headerBox` and the rest are
pushed onto `_bodyRows`, where positioning a row that has no cells throws
`Sequence contains no elements` out of `row.Boxes.Min(...)`. It has been reached twice from opposite
directions — by a second layout that did not remove the previous run's proxies
([#353](https://github.com/jhaygood86/PeachPDF/issues/353)), and by a resumed pass that deliberately
does not remove them, because they are the only surviving reference to the detached group every
earlier page repeats.

The one place this is currently safe without a check is anything that runs *after*
`RestoreStructureFromAnyPreviousRun` on a fresh layout, since that drops every proxy — which is
precisely the guarantee a continuation gives up.
