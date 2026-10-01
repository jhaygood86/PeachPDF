# A `columns: 1` container taller than a page paginates (#1538)

`CssLayoutEngineColumns.PerformLayout` has a `columnCount <= 1` fallback (also taken by every vertical-writing-mode
container, #764). It looped `LayoutBlockChild` over `Boxes` itself, ignoring the resumption record and never setting a
pending break token, so the page driver was never told anything was left: one page, everything past it dropped. Its
comment claimed pagination happened "via paint-time clipping", which has not been how fragmentation works since the
fragment tree.

**Fix:** the fallback calls `FillFragmentainerWithBlockChildren(g, resume)`, the same loop ordinary block boxes use
(it owns the resume record, keep-with-next restarts and a child's own break-before, and it skips running() children
itself), and returns when it reports a pending break. Spec basis: css-multicol-1 §2 makes `column-count: 1` a
multi-column container, and a paginated one continues on the next page.

Not done: the fallback still ignores `resume` for `ActualBottom` bookkeeping beyond "do not reset it on a resumed
pass", matching what the block path does for a continued box.

Evidence: `MulticolContentLossTests` (`columns: 1`, `column-count: 1`, spacer above, `position: relative` wrapper;
`columns: 2` and no columns kept as controls) failed with 13 words missing before, as the issue measured; full net8.0
suite (14823 tests) passes.
