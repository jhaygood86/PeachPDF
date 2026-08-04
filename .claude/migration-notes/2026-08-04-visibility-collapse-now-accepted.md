# `visibility: collapse` is now accepted

**Landed:** 2026-08-04 — Convert word-break/hyphens/clear/visibility/text-transform/text-align/white-space/overflow/float to typed storage (#598 follow-up)
**Doc section:** docs/html-css-support.md § [visibility row](../../docs/html-css-support.md)

`visibility`'s accepted keyword set was previously `visible`/`hidden` only — an authored
`visibility: collapse` failed validation and had no effect, leaving the element at its previous or
inherited visibility. Converting `visibility` to typed `CssProperty<Visibility>` storage reused the
existing `Visibility` enum and `Map.Visibilities` keyword map, which already included `collapse` (used
elsewhere in the CSS-OM parsing pipeline) — so `collapse` is now a valid, stored value.

PeachPDF does not implement CSS 2.1 §17.6.1's table row/column collapse layout (removing the row/column
from table geometry entirely), so `visibility: collapse` renders identically to `visibility: hidden`
everywhere (every downstream check only distinguishes `visible` from "anything else"). A document that
declared `visibility: collapse` expecting it to be silently ignored (falling back to the previous or
inherited value) will now see the element hidden instead.
