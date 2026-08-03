# `:has()` leading-combinator forms now restrict to the stated relationship

**Landed:** 2026-08-03 — Support :has() leading combinators (:has(> S), :has(+ S), :has(~ S))
**Doc section:** docs/html-css-support.md § [Pseudo-classes](../../docs/html-css-support.md) (`:has(S)` row)
**Verified against v0.9.8:** the `v0.9.8` tag's docs read "Only the default descendant relationship is
supported — CSS4 leading-combinator forms (`:has(> S)`, `:has(+ S)`, `:has(~ S)`) are not supported and
are silently discarded by the parser" — confirmed genuine behavior change since 0.9.8, in scope for the
next release notes.

`:has(> S)`, `:has(+ S)`, and `:has(~ S)` used to behave identically to plain `:has(S)` — the leading
combinator was silently discarded during parsing, so all three matched *any* descendant at any depth.
They now correctly restrict to what CSS Selectors 4 says: `:has(> S)` matches only when a direct child
matches `S`, `:has(+ S)` only when the immediate next sibling matches `S`, and `:has(~ S)` when any later
sibling matches `S`. A comma-separated argument can mix forms per alternative (e.g. `:has(> .a, + .b)`).
A document that wrote `:has(> S)`/`:has(+ S)`/`:has(~ S)` but relied on the old (spec-incorrect) broader
match — i.e. it actually needed *any* descendant to match, not just a direct child/sibling — will now
match fewer elements; switch such a rule to plain `:has(S)` to keep the old (any-depth-descendant)
behavior.
