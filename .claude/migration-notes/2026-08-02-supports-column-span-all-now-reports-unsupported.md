# `@supports (column-span: all)` now reports `false`

Previously, `@supports (column-span: all)` evaluated to `true` because `column-span`'s `@supports`
oracle used the same permissive value list as real cascade dispatch. `column-span: all` itself was
always accepted and stored by the cascade (unchanged), but `CssLayoutEngineColumns.cs` has never acted
on it — a `column-span: all` element has never actually spanned all columns. `@supports` was reporting
support for a value that has no functional effect.

`column-span`'s `@supports` check now uses a narrower list (`none` only) than its cascade-dispatch list
(`none`, `all`), the same split already used for `break-before`'s `region`/`avoid-region` keywords:
`column-span: all` still parses and is stored exactly as before; only `@supports (column-span: all)`'s
answer changes, from `true` to `false`. See
[`.claude/accepted-gaps/column-span-all-has-no-layout-effect.md`](../accepted-gaps/column-span-all-has-no-layout-effect.md).
