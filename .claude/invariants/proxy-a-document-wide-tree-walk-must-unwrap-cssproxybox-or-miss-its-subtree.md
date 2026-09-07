# A document-wide tree walk must unwrap `CssProxyBox` or miss its subtree entirely

`CssProxyBox` deliberately does not expose its `SourceBox` subtree through `CssBox.Boxes` — that is the
whole point of it: one detached source subtree (a repeating `<thead>`/`<tfoot>`'s row group, removed from
the live tree by `CssLayoutEngineTable.RemoveHeaderFooterFromTree`) is shown at a different position on
every page it repeats onto, via one proxy per page.

Any code that walks `CssBox.Boxes` recursively over the **whole document** (not scoped to one already-known
subtree) to answer a yes/no question about "does a box like *this* exist anywhere" is blind to a box that
exists **only** inside a repeated header/footer's source subtree, unless it explicitly unwraps
`CssProxyBox.SourceBox` the way `Fragmentation.FragmentEmitter.ChildrenOf` and
`HtmlContainerInt.ComputeFlowFlags` both do. The failure mode is not a narrowly wrong answer — when the
predicate being scanned for exists *only* inside such a subtree, the flag comes back `false`/`empty`
exactly as if nothing of the kind existed in the document at all, which for
`HasStackingHoistCandidates` meant a stacking-context box was never painted on any page, not merely
misplaced (issue #345's own investigation surfaced this as a prerequisite bug, tracked and fixed
separately).

Before adding a new document-wide `CssBox.Boxes` walk (a feature-detection flag, a "does anything need X"
short-circuit, a global count), check whether the answer could depend on content inside a repeated
header/footer, and if so unwrap `CssProxyBox` the same way. A walk that is already scoped to a specific,
already-materialized subtree (never crossing into a `CssProxyBox`) does not need this — the risk is
specific to a walk that starts at `Root` and means to see everything.
