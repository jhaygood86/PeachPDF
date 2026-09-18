# `text-underline-position: left`/`right` is now implemented

Previously, the `left`/`right` alternative of `text-underline-position` (css-text-decor-4 §2.5's
compound grammar, `auto | [ from-font | under ] || [ left | right ]`) parsed as invalid and was
dropped, leaving the property at its previous/initial value. A declaration like
`text-underline-position: under left` had no effect on the `left` half at all.

`left`/`right` are now parsed and applied. Meaningful only under a true vertical writing mode
(`vertical-rl`/`vertical-lr`), they pin the underline to that literal physical edge instead of the
writing mode's own default under/over mapping. Per §2.5's own note, if that pins the underline where an
overline would otherwise be drawn, the overline moves to the opposite physical edge instead of
overlapping it. Under `horizontal-tb`, `left`/`right` now parse successfully (previously rejected) but
still have no visible effect, since there is no physical left/right edge distinct from a horizontal
line's own inline extent.

This also corrects a pre-existing citation error: the property's full grammar (including `from-font`)
is a [CSS Text Decoration Module Level 4](https://www.w3.org/TR/css-text-decor-4/#text-underline-position-property)
addition, not Level 3 - Level 3's own grammar (`auto | [ under || [ left | right ] ]`) has no
`from-font` keyword at all. Every citation for this property in the codebase and
`docs/html-css-support.md` now names Level 4.
