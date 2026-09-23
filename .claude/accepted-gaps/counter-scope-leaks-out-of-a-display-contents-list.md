# A counter instantiated inside a `display: contents` element leaks into the siblings around it

[CSS Lists 3 §4.4.1](https://www.w3.org/TR/css-lists-3/#counter-inheritance) inherits counters through the
*element* tree: a counter instantiated by an element is in scope for its following siblings and their
descendants. `CssCounterEngine` walks `ParentBox` and the previous sibling in the *box* tree, and
`display: contents` splices an element's children into its parent — so a lifted child's counter shares one
sibling chain with everything around the element.

```html
<ol style="display: contents"><li>a<li>b</ol> <p>x</p> <ol style="display: contents"><li>c</li></ol>
```

`c` numbers as 3; the element tree says 1. The element's *own* `counter-*` is correctly ignored (§4.5), so this
only bites a counter incremented by a lifted child — in practice an `<li>` of a `display: contents` list, whose
implicit `list-item` increment is what leaks. A single contents list, or contents wrappers that are not
followed by another list, number correctly.

**Why it was left.** Fixing it means giving the inheritance walk an element-tree view of a lifted box (treat a
previous sibling with a different `DisplayContentsAncestors` chain as not a sibling, and continue through the
innermost shell, which inherits from its own previous sibling). That is a change to the counter engine's
scoping model, not to `display: contents`, and needs its own tests.

Tracked as [issue #1297](https://github.com/jhaygood86/PeachPDF/issues/1297).
