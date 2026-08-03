# Keyword comparison case-sensitivity is inconsistent across CSS properties

Tracking issue: [#598](https://github.com/jhaygood86/PeachPDF/issues/598).

Whether a keyword-valued CSS property accepts a non-canonical-case spelling (`AUTO` vs `auto`) varies by
property, with no consistent rule — `width: AUTO` is accepted (case-insensitive) while
`column-width: AUTO` is rejected (case-sensitive), even though both are `<length> | auto`-shaped. Per
CSS Values and Units, most keyword values should match ASCII-case-insensitively; this repo's actual
behavior predates that being enforced consistently.

The CSS/SVG property registry generator (`src/PeachPDF/css-properties.json`, see CLAUDE.md's "CSS/SVG
property registry generator" section) requires every keyword-typed entry to declare a
`keywordComparison` (`ordinal`, `ordinal-ignore-case`, or `invariant-ignore-case`). Authoring the full
property set encoded each property's *actual, historical* comparison mode as-is rather than normalizing
every property to case-insensitive — silently changing 100+ properties' case-sensitivity was out of
scope for a migration whose only goal was moving dispatch from hand-written code to generated code.

**Deliberately out of scope.** Fixing this means auditing each `keywordComparison: "ordinal"` entry in
`css-properties.json`, deciding case by case whether case-insensitivity is the spec-correct fix (almost
certainly yes, per CSS Values and Units), and updating the JSON entry plus its tests — a real fix, but
independent of the registry-generator migration that surfaced it.
