# Two more media features are evaluated, and the preference features can be set

**Before:** `inverted-colors` and `prefers-reduced-data` were not recognised media features, so any `@media` rule that named one was invalid and never applied, in either branch (`@media (inverted-colors: none)` and `@media (prefers-reduced-data: no-preference)` both skipped their blocks). `prefers-reduced-motion`, `prefers-contrast`, `forced-colors` and `prefers-reduced-transparency` were fixed at a static document's values.

**Now:** both are recognised and report their resting state (`inverted-colors: none`, `prefers-reduced-data: no-preference`), so a rule guarded by the resting value now applies, and a rule guarded by the other value still does not. `HtmlDocumentOptions.Preferences` (`UserPreferences`) lets a host report the reader's real settings for all six features; left unset, the other four report exactly what they did.

**Why:** hosts that show a document on a screen (the WinUI and Android views) need to tell style sheets about the reader's accessibility settings, and `@media (inverted-colors: none)` is a common way to guard colour-sensitive rules. A document that names neither feature renders as before.

Also new, off by default: `HtmlDocumentOptions.TextSpacing`.
