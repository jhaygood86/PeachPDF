# `font-size: Ncqw` (container-relative units) resolves to 0 for any box with text content

`font-size` accepts `cqw`/`cqh`/`cqi`/`cqb`/`cqmin`/`cqmax`, and `FontSizeResolver.Resolve`/
`DerivedStyle.ActualFont` do thread a container inline/block size basis through to
`CssValueParser.ParseLength` when one is available (`CssBox.GetContainerRelativeUnitBasis`). In
practice that basis is never available for a box that contains text: `DomParser.CorrectTextBoxes`
splits text into words (`CssBox.ParseToWords` → `AddWord` → `NeedsPerCodepointFont`) during
`DomParser.GenerateCssTree` - cascade/tree-correction, which runs entirely before any layout pass
exists - and that word-splitting reads `CssBox.ActualFont` to decide per-codepoint font matching.
`DerivedStyle.ActualFont` caches the resolved font (and therefore the resolved size) on first
access and never invalidates it, so touching it during word-splitting permanently locks in a
container basis of `(null, null)` (0), even though the real ancestor container's size becomes
known later in that same layout pass.

This is different from (and narrower than) the general
[`cq*`-with-no-ancestor-container gap](container-relative-units-resolve-to-zero-with-no-ancestor-container.md):
a real, eligible ancestor `container-type: size`/`inline-size` box exists here, and every other
`cq*` consumer (`width`, `calc()`, etc. - all read post-layout, not cached pre-layout) resolves
against it correctly. Only font-size, and only for text content, hits this pre-layout caching
order problem.

A real fix likely needs `ActualFont`'s container lookup to consult the layout convergence loop's
`ContainerQuerySizes` snapshot from the *previous* pass (the same source `@container` rule
matching already reads) instead of live, not-yet-laid-out box geometry, since a pass's own
word-splitting runs before that pass's own layout but after the *previous* pass's layout finished.
That's a materially larger change than the parameter-threading the rest of `cq*` support needed, so
it's tracked separately. Tracked as
[#619](https://github.com/jhaygood86/PeachPDF/issues/619).
