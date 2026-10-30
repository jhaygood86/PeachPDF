A property the SVG builder reads through the cascade (`ResolveStyledAttr`) needs an `svg` entry in `css-properties.json`, even when `SvgElement` stores nothing for it.

`SvgPropertyRegistry.GetInitialValue(name)` is the one place an SVG initial value lives, and **null means "inherited"**: a stylesheet `initial` for a property it does not know resolves to "no value", which the
builder reads as the parent's value. A property with only an `html` binding therefore behaves as if `initial` were `inherit` for SVG, silently, and only inside a parent that sets something.

Measured symptom (twice in a row): `<g visibility="hidden"><rect class="r"/></g>` with `.r { visibility: initial }` stayed hidden, and `writing-mode`, `text-orientation`, `letter-spacing` and `word-spacing`
kept the parent's value for `initial` in a rule. A second, hand-kept table of "properties the registry does not describe" is how the gap hides: it fixes the animation path and leaves the cascade path wrong.

**What to do.** Give the property an `svg` block. If `SvgElement` does not store it, make it carrier-only: `"propertyPath": null`, `"applyIn": "manual"`, and `inheritedFrom` when it is inherited (`direction`,
`writing-mode`, `letter-spacing` are the precedents). The generator puts a manual entry's `initialValue` into `InitialValues` without generating a setter. A brand-new property still also needs its CSS-OM
registration; see [the registry/CSS-OM invariant](css-properties-a-new-property-needs-both-a-registry-entry-and-a-cssom-property-registration.md). Read a declared value through
`SvgTreeBuilder.DeclaredOrInitial`, so `initial` is replaced before `TrySet` or a keyword table rejects it.

`font-size` is the known exception: it has no SVG entry, because the builder's resting size (11px) is not the CSS initial size (16px); see the fix note for #1986.
