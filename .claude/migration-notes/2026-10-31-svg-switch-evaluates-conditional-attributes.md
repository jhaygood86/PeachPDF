# SVG `<switch>` evaluates its children's conditional attributes

**Before:** `<switch>` always rendered its first buildable child, and `systemLanguage`, `requiredExtensions` and
`requiredFeatures` were ignored everywhere.

**Now:** `<switch>` renders the first child whose conditions pass. `systemLanguage` matches English or the
document's `lang`; `requiredExtensions` passes only for the XHTML/SVG namespaces (empty fails); `requiredFeatures`
always passes. The same attributes on an element outside a `switch` now remove it when false.

**Why:** SVG 2 §5.8 conditional processing. A document that relied on `<switch>` always taking its first child, or
on `systemLanguage="de"` rendering regardless, will now render the fallback child or nothing.
