# A digit-leading hex `color-mix()` operand (e.g. `#2563eb`) now resolves

**Landed:** 2026-08-03 — Fix digit-leading hex color-mix() operand resolution
**Doc section:** docs/html-css-support.md § [Color & Typography](../../docs/html-css-support.md#color--typography) (the `color` row)
**Verified against v0.9.8:** the `v0.9.8` tag's `docs/html-css-support.md` `color` row reads "Known gap:
a **hex** color used directly as a `color-mix()` operand only resolves if it is *letter-leading* (e.g.
`#e11d48`); a digit-leading hex operand (e.g. `#2563eb`) is not recognized inside `color-mix()`..." —
confirmed genuine behavior change since 0.9.8, in scope for the next release notes.

Previously, a hex color used as a `color-mix()` operand only resolved when *letter-leading* (e.g.
`#e11d48`); a digit-leading hex operand (e.g. `#2563eb`) was silently dropped, even though the identical
hex string worked fine as a plain `color`/`background-color` value outside `color-mix()`. Since Tailwind
CSS v4 compiles its opacity-modifier syntax (`bg-[#2563eb]/50`) to `color-mix()`, roughly half of all
possible arbitrary hex values (any with a leading digit) silently lost their opacity modifier.

A `color-mix()` operand's hex is now recognized regardless of its leading character — `color-mix(in oklab,
#2563eb 50%, transparent)` and `color-mix(in oklab, #e11d48 50%, transparent)` both resolve correctly.
