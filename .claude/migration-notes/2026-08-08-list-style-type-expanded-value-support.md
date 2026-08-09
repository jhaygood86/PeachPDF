# `list-style-type` now accepts many more predefined counter styles, plus a literal `<string>` marker

**Landed:** 2026-08-08 — Add spec-compliant support for additional `list-style-type` values
**Doc section:** docs/html-css-support.md § [Lists](../../docs/html-css-support.md#lists)
**Verified against v0.9.8:** this note was not present in the `v0.9.8` tag's docs — confirmed genuine behavior change since 0.9.8, in scope for the next release notes.

Previously, `list-style-type` (and `content: counter(name, <style>)`) only recognized a small subset of
CSS Counter Styles Level 3's predefined names - `disc`, `circle`, `square`, `decimal`,
`decimal-leading-zero`, `lower`/`upper-alpha`/`-latin`, `lower`/`upper-roman`, `lower-greek`, `armenian`,
`georgian`, `hebrew`, `hiragana`/`-iroha`, `katakana`/`-iroha`. Any other named value (`devanagari`,
`thai`, `ethiopic-numeric`, `disclosure-open`, ...) was rejected as invalid CSS, so the declaration was
dropped and the list silently fell back to its cascaded/UA-default numbering (usually `disc` or
`decimal`). A document author relying on one of those values got the wrong marker with no error.

All of MDN's documented `list-style-type` values are now accepted and render correctly, except the 10
East Asian "longhand" styles and the `symbols()` function/`@counter-style` at-rule (see the accepted-gap
notes for those). A literal `<string>` marker (`list-style-type: "→ "`) is also now supported.

Two of the *previously already-documented* values also had real bugs, fixed in the same change:
`armenian`/`hebrew` silently dropped their thousands digit for any counter value ≥ 1000 (a 3-row
internal table where the values need a 4th), and `hiragana-iroha`/`katakana-iroha` silently rendered in
plain dictionary order instead of the actual iroha character ordering (they reused the `hiragana`/
`katakana` tables outright). Separately, `hebrew`/`hiragana`/`hiragana-iroha`/`katakana`/`katakana-iroha`
were previously unreachable via a real `<style>` block or inline `style=""` attribute at all (a stricter,
independently-maintained keyword list at the CSS-OM parsing layer rejected them before they ever reached
the renderer, despite being listed as supported) - fixed by having that layer delegate to the same
keyword list the renderer uses, per this repo's "don't write two independent parsers for the same CSS
value grammar" rule.
