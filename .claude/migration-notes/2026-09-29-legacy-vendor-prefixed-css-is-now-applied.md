# Legacy `-webkit-`/`-moz-` spellings of supported properties are now applied

**Before:** a declaration written only with a legacy vendor prefix — `-webkit-box-shadow`, `-webkit-transform`,
`-moz-box-sizing`, `-webkit-flex`, `-webkit-border-radius`, `-webkit-column-count`, `display: -webkit-flex`,
`position: -webkit-sticky`, `background: -webkit-linear-gradient(...)` — was dropped at parse time, so autoprefixer-era
stylesheets that carried no unprefixed fallback lost those styles. Only `-webkit-backdrop-filter`, `word-wrap` and
`:-webkit-any()` were recognized.

**Now:** the prefixed spelling is the same property as the standard one (`VendorPropertyAliases`), the prefixed
keyword values `-webkit-flex`/`-webkit-inline-flex`/`-webkit-sticky` are the standard keywords (`VendorValueAliases`),
and the vendor gradient functions `-webkit-`/`-moz-`/`-o-`/`-ms-` `(repeating-)linear-gradient()`/`radial-gradient()`
are translated from their pre-standard syntax (`LegacyGradientSyntax`). A stylesheet that has both spellings now
cascades them as one property in source order, so a *later* prefixed declaration overrides an earlier standard one
(browsers behave the same).

Still dropped: `display: -webkit-box`, `-webkit-line-clamp`, `-webkit-box-*` (old flexbox), `-webkit-gradient()`,
prefixed intrinsic-size keywords, and prefixed forms of properties PeachPDF does not support unprefixed either
(`-webkit-text-fill-color`, `-webkit-text-stroke`, `-webkit-mask-*`, `-webkit-user-select`, `-webkit-appearance`).

Serialization of `@supports (-webkit-box-shadow: …)` now reads back as `(box-shadow: …)`, the same as `word-wrap`
reads back as `overflow-wrap`.
