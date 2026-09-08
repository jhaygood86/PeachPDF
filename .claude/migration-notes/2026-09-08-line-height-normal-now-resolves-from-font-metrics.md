# `line-height: normal` now resolves from the used font's own metrics

Any element that doesn't declare an explicit `line-height` (i.e. relies on the initial/`normal` value)
used to get a flat `1.2 × font-size` line height for every font. `line-height: normal` now resolves per
CSS 2.1 §10.8.1 — "based on the font of the element" — from that font's own `hhea` ascent/descent/line-gap
(or the `OS/2` typo triple, when the font's `USE_TYPO_METRICS` flag is set), matching how Chromium,
Firefox and Safari compute it. This is a document-wide rendering change: any PDF generated without an
explicit `line-height` on the relevant elements will now paginate and space lines differently, closer to
how the same HTML renders in a browser. The deviation from the old flat constant depends on the font in
use — some fonts land close to the old `1.2×` value, others (fonts with generous ascent for diacritics, or
unusually tight/loose vertical metrics) differ by several percent per line, which compounds over a page of
body text.

Confirmed against `docs/getting-started.md`/`docs/html-css-support.md` at the last tagged release: neither
page documented the specific `1.2×` constant, only that `line-height` has "Full support," so this note is
the only place the old behavior was ever written down.
