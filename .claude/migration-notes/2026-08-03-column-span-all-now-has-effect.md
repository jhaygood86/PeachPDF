# `column-span: all` now breaks the column flow, instead of being inert

**Landed:** 2026-08-03 — Implement `column-span: all` layout (#602)
**Doc section:** docs/html-css-support.md § [Multi-column Layout](../../docs/html-css-support.md#multi-column-layout)
(the `column-span` row)

Previously, `column-span: all` was parsed and accepted by the cascade
(`ColumnSpanProperty`/`ColumnSpanConverter`) but `CssLayoutEngineColumns.cs` never read it, so a
spanning element rendered exactly like an ordinary in-column box — no different from `column-span:
none`. `@supports (column-span: all)` also reported unsupported, reflecting that inertness.

Now, a `column-span: all` element that is a **direct** child of a multi-column container renders at the
container's full content width, breaking the column flow into two independently-laid-out runs — the
columns before it and the columns after it each balance (under `column-fill: balance`, the default) or
fill (`column-fill: auto`) on their own, and `column-rule` draws only within each run, never through the
spanning box itself. `@supports (column-span: all)` now reports supported.

A document that relied on the old inert behavior — e.g. one that declared `column-span: all` on an
element expecting it to keep flowing as an ordinary column item — will now see that element span the
full container width instead. `column-span: all` on a descendant that is **not** a direct child of the
multi-column container still has no effect (a narrower, tracked gap — see
`.claude/accepted-gaps/column-span-all-only-recognized-on-a-direct-child.md`).
