# An "emitted nothing" mark is read against the scope of the box that holds it, never its parent's

`CssBox.EmittedNothingAtOrBefore(slot, scopeOwner, history)` is true only if `ReferenceEquals(_emittedNothingScopeOwner, scopeOwner)`: a mark
is valid in the scope it was written under and nowhere else. `FragmentEmitter.ScopeOwnerOf(box)` decides that scope, and it is **per box**:
since the per-paragraph scoping (see `.claude/recent-fixes/2026-10-27-per-paragraph-reopening-scope.md`) a `<p>` in a `<body>` of many
paragraphs has its own scope, different from `<body>`'s.

## The rule

Whenever code asks "does this box's mark still hold?", derive the scope **from that box** (`ScopeOwnerOf(box)`) and take the history from it
(`HistoryFor(scope)`). Do not compute the scope once for a container and test all its children against it.

## The measured symptom

`OrdinaryChildrenStart` (the cursor that lets a container's child walk start past a prefix of finished children) used to compute
`ScopeOwnerOf(parent)` once and test every child against it. That only worked because every box in an HTML document shared one scope
(`<html>`). With per-paragraph scopes the comparison never matched, the cursor never advanced, and `FragmentEmitter` visited every finished
paragraph on every page: `BuildDraftCalls` for 1000 paragraphs at 820pt went 64,518 -> 117,370. Nothing failed; only
`StaleSlotReopeningCostTests` (the visit-count ratio) noticed.

## Why a mismatch is safe, and why it is still a bug

A mismatched scope makes a mark unusable, never wrong, so the failure mode of getting this wrong is slow, not incorrect output - which is
exactly why it passes every correctness test and the `PEACHPDF_VERIFY_FRAGMENT_PRUNING` oracle. Guard it with a visit-count assertion, not a
pixel or fragment comparison.
