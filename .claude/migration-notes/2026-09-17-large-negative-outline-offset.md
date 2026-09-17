# Large negative `outline-offset` values remain visible

Previously, a sufficiently negative `outline-offset` could make the outline's outside rectangle have
zero or negative width or height, causing the outline to disappear entirely.

The outside outline shape now remains at least twice the `outline-width` in each dimension, as required
by CSS Basic User Interface 4. Large negative offsets still pull the outline into the border box, but
they no longer suppress it.
