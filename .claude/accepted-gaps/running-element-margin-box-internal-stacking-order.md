# `content: element()` margin-box content ignores internal stacking order

css-gcpm-3's `content: element(<custom-ident>)` lays the selected running element out for real
(`RunningElementLayout.LayoutRunningElementFor`) and captures the result into a `BoxFragment` tree via
`MarginBoxContentFragmentBuilder` — a small, purpose-built, one-shot walk, deliberately not a reuse of
`FragmentEmitter`'s pagination-aware `Materialize`/`BuildDraft` machinery (inseparably coupled to
multi-pass span/slice bookkeeping meaningless for a subtree that never fragments, since a margin box's
content is always exactly one whole, unbroken layout).

One consequence: the one-shot builder walks children in DOM order and does not replicate
`FragmentEmitter`'s CSS 2.1 Appendix E stacking-order hoisting (`StackingOrder`/
`DomUtils.NeedsStackingHoist`). A running element containing its own internally-stacked content (a
`position: absolute`/`z-index`-stacked descendant) paints its descendants in DOM order rather than
proper stacking order inside the margin box.

Accepted for v1 since running elements are overwhelmingly simple text/image headers/footers in
practice — a stacked-content running header is not a realistic authoring pattern this library needs to
get exactly right on day one. Filed as
[issue #692](https://github.com/jhaygood86/PeachPDF/issues/692).
