# Most CSS keyword-valued properties now accept non-canonical-case spellings

**Landed:** 2026-08-03 — Fix keyword comparison case-sensitivity inconsistency (#598)
**Doc section:** docs/html-css-support.md (no dedicated callout previously existed for this — the gap was
tracked only in `.claude/accepted-gaps/`, not documented as a limitation)

Previously, whether a keyword-valued CSS property accepted a non-canonical-case spelling (`AUTO` vs
`auto`) was inconsistent and undocumented: `width: AUTO` was accepted (case-insensitive) while
`column-width: AUTO` was rejected (case-sensitive) and silently ignored, even though both are
`<length> | auto`-shaped. The inconsistency traced back to each property's hand-written validator having
been authored independently before the CSS/SVG property registry generator migration.

Now, per CSS Values and Units §4.2 ("keyword values are ASCII case-insensitive"), keyword values are
matched case-insensitively across the board: `display`, `position`, `float`, `clear`, `overflow`,
`visibility`, `box-sizing`, `box-decoration-break`, every `border-*-style`/`column-rule-style` property,
`vertical-align`, `text-align`, `text-transform`, `white-space`, `word-break`, `hyphens`, `font-stretch`,
the flex/grid alignment properties (`flex-direction`, `flex-wrap`, `justify-content`, `align-items`,
`align-content`, `align-self`, `justify-items`, `justify-self`), `column-fill`, `column-span`, the
`auto`-accepting length properties (`margin-*`, `left`/`top`/`right`/`bottom`, their logical
`margin-block-*`/`margin-inline-*`/`inset-block-*`/`inset-inline-*` equivalents, `column-width`), and
`z-index`, `line-height`, `word-spacing`, `letter-spacing`, `font-size`, `font-weight`, `flex-basis`,
`row-gap`, `column-gap`, `column-count`.

A second, previously-invisible bug is fixed alongside the validation change: even for properties that
were *already* case-insensitive (`width`, `object-fit`, `border-collapse`, `fill-rule`, and 23 others),
a non-canonical-case value that passed validation was stored on the box using its raw, as-authored
casing rather than the canonical lowercase spelling — so `width: AUTO` could validate but then silently
fail to match the lowercase-literal comparisons layout and paint code make against it. Stored values are
now always canonicalized to their declared spelling, for every keyword property regardless of which
comparison mode it uses.

A document that relied on a non-canonical-case value like `column-width: AUTO` being *rejected* (falling
back to the previous or inherited value) will now see it accepted and applied instead. This is expected
to be a purely corrective change — no known document intentionally relies on a browser-incompatible
CSS property value being silently dropped for its casing alone.
