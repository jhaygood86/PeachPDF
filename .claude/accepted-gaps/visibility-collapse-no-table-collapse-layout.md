# `visibility: collapse` renders identically to `hidden` (no table row/column collapse layout)

Tracking issue: [#639](https://github.com/jhaygood86/PeachPDF/issues/639).

Per [CSS 2.1 §17.6.1](https://www.w3.org/TR/CSS21/tables.html#dynamic-effects), `visibility: collapse`
on a table row/row-group/column/column-group should remove it from the table's geometry entirely
(other rows/columns shift to fill the gap) - behavior distinct from `visibility: hidden`, which
reserves the element's layout space and merely omits painting it.

PeachPDF has no table row/column collapse layout implementation. As of the typed-storage conversion
that reused the pre-existing `Map.Visibilities` keyword map (which already included `collapse` for use
elsewhere in the CSS-OM pipeline - see `.claude/migration-notes/2026-08-04-visibility-collapse-now-accepted.md`),
`collapse` is validated and stored like any other `visibility` value, but every downstream layout/paint
check only distinguishes `Visibility.Visible` from "anything else" - so `collapse` currently renders
exactly like `hidden` (space reserved, nothing painted) on every element, not just table rows/columns.

**Deliberately out of scope.** Fixing this means real table layout changes - removing collapsed
rows/columns from track sizing and shifting subsequent ones - not a doc-accuracy fix.
