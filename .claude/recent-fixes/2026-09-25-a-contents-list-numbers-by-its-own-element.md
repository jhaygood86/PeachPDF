# A `display: contents` list is numbered by the list element that owns it (#1297)

`<ol style="display:contents"><li>a<li>b</ol><p>x</p><ol style="display:contents"><li>c</li></ol>` numbered
`c` as **3**; Chrome 152 gives `1. 2. x 1.`. `display: contents` splices an element's children into its parent,
and `CssCounterEngine` inherits a counter through `ParentBox` and the previous sibling in the *box* tree, so li
`c` found li `b`'s counter through the `<p>`. CSS Lists 3 §4.4.1 scopes a counter by the *element* tree.

## The load-bearing idea, and the version that was wrong

A `list-item` counter created by a box that a `display: contents` element lifted records that element as its
`ScopeParent` (`CssCounter.ScopeParent`, set by `ScopeParentOf`); `InheritAndApplyCounter` drops an inherited
counter whose scope parent is not an element-tree ancestor of the box asking for it.

**The first version scoped every counter that way, and Chrome contradicted it.** A counter an author only ever
increments (`h2 { counter-increment: n }`, no reset) is instantiated at its first increment and stays in scope
for the rest of that element's parent - through a `<div>` and through `<div style="display:contents">` alike
(`n=1 a, n=2 b, n=3 c` for both). Scoping it for a lifted box alone made a contents wrapper number differently
from the same markup with a plain wrapper, which is exactly what `display: contents` must not do. Only
`list-item` is special: browsers number a list's items by the list that owns them, so the items of a second list
restart. The old gap note's suggestion ("a previous sibling with a different `DisplayContentsAncestors` chain is
not a sibling") would have been wrong for the same reason, and for a `counter-reset` on an outer element that is
incremented inside a wrapper (`CounterResetOutside_ContinuesAcrossAContentsWrapper` pins it).

## What stays

- A `display: contents` `ol` inside an `li` **continues** the outer list (`1. one 2. two 3. three`; Chrome agrees,
  a contents element cannot reset, §4.5), because the scope parent is the contents element, which is *inside*
  the outer `li`.
- `start`/`reversed` on a `display: contents` `<ol>` have no effect (the `ol`'s reset is unreachable), consistent
  with §4.5. Not verified against Chrome; a separate gap if it differs.
- **An anonymous box passes a counter on** (`IsAnonymous` in `IsInScope`). The wrapper the parser puts around a run of
  bare text is created *after* the `display: contents` boxes are lifted, so it carries no shell chain, and judging
  it by its box parent dropped the counter: the next item restarted at 1. Found by review, not by the ordinary
  tests - markers and `counter()` content resolve in `CorrectTextBoxes`, ahead of the passes that create such
  wrappers, so they never ask the question on the final tree. A counter first asked for later (a
  `target-counter()`) would. `ItemCounters_ResolvedAgainOnTheFinalTree_*` clears every box's counters after layout
  and resolves them again over the finished tree, which is the only way to reach it; it reads `[1, 1, 1]` without the
  exemption. The element that finally asks still does the scope check, so `text` between two lists still ends the first.
- Zero cost without `display: contents`: `DisplayContentsAncestors` is null, so `ScopeParent` is null and
  `IsInScope` returns at once.

## Evidence

`DisplayContentsIntegrationTests`: adjacent lists, lists separated by a paragraph, lists in a contents wrapper, a
contents `ol` after a contents `ul`, the nested case, a wrapped ordinary list, an outer `counter-reset` across a
wrapper, and the unreset-author-counter case against a plain-`div` control. Every expectation was read off
Chrome 152 rather than derived. The accepted-gap file was deleted and the two limitation notes in
`docs/html-css-support.md` were reworded in the same change.
