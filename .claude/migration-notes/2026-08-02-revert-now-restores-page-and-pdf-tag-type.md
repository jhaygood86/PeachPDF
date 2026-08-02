# `revert`/`revert-layer` now restores `page` and `-peachpdf-pdf-tag-type`

**Landed:** 2026-08-02 — CSS/SVG property registry generator, HTML cutover
**Doc section:** docs/html-css-support.md § [CSS-wide Keywords](../../docs/html-css-support.md#css-wide-keywords)
**Verified against v0.9.8:** the `v0.9.8` tag's `CssUtils._knownPropertyNames` list omits `"page"` and
`"-peachpdf-pdf-tag-type"` even though both already had real getters/setters — confirmed genuine
behavior change since 0.9.8, in scope for the next release notes.

`page` (CSS Paged Media 3) and `-peachpdf-pdf-tag-type` (PeachPDF's tagged-PDF extension) each had a
working setter and getter, but were missing from the hand-maintained list of properties `revert`/
`revert-layer` know how to snapshot and roll back to. A declaration for either property using `revert`
or `revert-layer` silently fell through to the property's cascade-wide *initial* value instead of the
value established by the previous cascade origin — indistinguishable from a plain `initial` on those two
properties specifically. The default stylesheet's own `-peachpdf-pdf-tag-type` rules made this visible:
it sets `<div>`'s tag type to `div`, so `revert`ing an author override on a `<div>` used to reset to
`auto` (the property's initial value) instead of back to `div` (the user-agent stylesheet's value).

Both properties are now included in the snapshot set, so `revert`/`revert-layer` restore the correct
prior-origin value per CSS Cascade & Inheritance 5 §6.3, matching every other property. A document that
worked around the old behavior — for example by never using `revert` on `page` or
`-peachpdf-pdf-tag-type`, or by re-declaring the desired value explicitly instead — no longer needs the
workaround; `revert` now does what it always should have.
