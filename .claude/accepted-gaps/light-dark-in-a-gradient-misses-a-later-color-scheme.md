# `light-dark()` in a gradient misses a `color-scheme` set by a later rule on the same element

The `background-image` / `mask-image` setter parses gradient colour stops as the declaration is applied, with the element's used `color-scheme` *at that moment* (`ColorSchemeScope.Enter({box}.UsesDarkColorSchemeNow)` in `css-properties.json`). Within one rule `color-scheme` is applied first (`DomParser.AssignCssBlock`'s pre-pass) and an inherited scheme is already in place, so the common shapes are right. A `color-scheme` from a *later* rule or the inline style on the same element is not seen by a gradient set in an earlier rule.

Every other colour (text, background, borders, shadows, `color-mix()`) is read at use against the final scheme and is correct. Fixing it means re-parsing the image list after the cascade or keeping both arms of the stops. Tracked in issue 2274.

A second, related deviation: a `light-dark()` colour is stored as text and resolved where it is read, so a child that inherits `color: light-dark(a, b)` from a parent in a different scheme re-resolves it against its own scheme instead of keeping the arm the parent computed (CSS Color 5 §4 computes it once, on the element that declares it).
