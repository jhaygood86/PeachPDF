# A box read through a captured snapshot is named by `capture`, not by its own concrete type

`FragmentEmitter.ChildrenOf`/`BuildDraft` visit some content only through a recorded
`CapturedInstance` — a snapshot of where a subtree's geometry stood when some other engine filled it,
handed over explicitly (`RecordCapturedInstance`/`RecordRepeatingGroupInstance`) rather than discovered
by inspecting the box's own live position. A multi-column column and a repeating `<thead>`/`<tfoot>`'s
detached source subtree are both this shape. **Neither is told apart from ordinary content, or from each
other, by testing what kind of `CssBox` it is** — `capture: CapturedInstance?` (null vs non-null,
and `capture.DetachedSourceRoot` null vs non-null) is the only thing that may gate behavior. Any future
mechanism shaped like these two — content visited through a captured snapshot rather than live geometry —
must fit into `CapturedInstance` (adding a field the way `DetachedSourceRoot` was added, if it needs one)
rather than earning its own `box is SomeSpecificType` check somewhere in the walk.

**Two dictionaries need a *coarser* key than `capture?.Self`, and conflating them broke concatenation
silently.** `_rectangles`/`_spans` (and `FragmentKey.EnclosingContext`, `capture?.Self`) need a key that is
*unique per instance* — a box nested one level inside a captured instance restarts `Instance` at 1 on every
call to `ChildrenOf`, so two unrelated occurrences (one per outer column) would otherwise collide.
`_fragmentRange`/`_fragmentsOf` (the "concatenate a box's own fragments into one §6.2 unbroken strip"
bookkeeping) need the *opposite*: a box split across two ordinary multi-column columns must share **one**
key so its two fragments concatenate, while a repeating group's source root read on two different pages
must **not** share a key (it is repeated wholesale, never divided — concatenating two pages together is
nonsense). `FragmentKey.RepeatingGroupInstance` is the field for this second, coarser question — null for
everything except a repeating-group capture, where it equals that capture's own `Self`. Reaching for
`EnclosingContext` here instead (the natural first guess, since it is *also* derived from `capture`) is
wrong in a way no single test catches in isolation: it silently stops concatenating ordinary multi-column
content, changing a measured extent rather than throwing or producing an obviously malformed tree. The
`PEACHPDF_VERIFY_FRAGMENT_PRUNING` parity oracle does not catch this either — it proves the pruned and
unpruned walks agree, not that either is measuring the right thing. Only content-level assertions
(`ABoxSplitAcrossColumns_IsMeasuredAgainstTheConcatenationOfItsFragments` and its siblings) do.
