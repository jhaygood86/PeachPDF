# DOM: a `display: contents` element is in no `Boxes` list, so a tree walk cannot find it

`DomParser.FlattenDisplayContents` splices a `display: contents` element's children into its parent and takes
the element's own `CssBox` — the **shell** — out of `Boxes`. Layout, paint and the fragment tree never see it,
which is the point. But every walk over `CssBox.Boxes` misses it too, and the element is still in the
document: it has an `id`, may be an `<a href>`, may carry `bookmark-level`, `string-set` or a tagged-PDF
structure type, and a `<body>` shell still owns the canvas background.

The shells are reachable in exactly one place: the flat, document-ordered
`HtmlContainerInt.DisplayContentsShells`, filled by the cascade. A change that answers a question **about an
element** (as opposed to laying out a box) with a new tree walk gets a wrong answer for every element that is
a shell, silently: no exception, the element just is not there. `DomUtils.GetBoxById`/`BuildIdIndex`,
`HtmlContainerInt.GetLinks`, `ResolveCanvasBackground` and `RegisterDisplayContentsNamedStrings` each consult
the list for that reason.

Three traps that follow from it:

- **A shell has no geometry.** Ask `DomUtils.ResolveGeometryBox` where it *is*: the first laid-out lifted
  descendant (never the shell's own empty `Rectangles`/`Bounds`, which read as page 1, Y 0), else the box that
  received its children.
- **`DisplayContentsLiftedChildren` is a snapshot.** A later pass can drop a whitespace-only text box, so an
  entry whose `ParentBox` is null is not in the tree any more.
- **A lifted `::before`/`::after` is no longer its originating element's child.** Read the element through
  `CssBox.OriginatingElement`, never `ParentBox`, or `attr()` and `content()` on it read the grandparent.

New whole-tree walks are also a cost this codebase avoids: extend an existing walk or use the flat list.
