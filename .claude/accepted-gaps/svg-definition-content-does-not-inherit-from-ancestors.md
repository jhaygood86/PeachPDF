# SVG definition content does not inherit from the definition's ancestors

Content inside `<pattern>`, `<marker>`, `<mask>` and `<clipPath>` is built from
`InheritedPaint.Initial` / `FontContext.Default` (`SvgTreeBuilder.BuildMarker`,
`BuildDefinitionChildren`, `ResolveClipPath`), so it never inherits `fill`/`stroke`/opacity/font
properties from the definition element's own ancestors — `<svg fill="#fff"><defs><pattern>` with an
unstyled `<rect>` paints black instead of white. SVG 2 (pattern/marker) and CSS Masking 1 (mask)
require ordinary inheritance from the definition element's ancestors, not from the referencing
element. Filed as [issue #1277](https://github.com/jhaygood86/PeachPDF/issues/1277).

`<clipPath>` is unaffected in practice (geometry only). `<use>` is **not** part of this gap — its
referenced content correctly inherits from the `<use>` element via `ApplyCommon`.

**Why it was left:** found while reviewing the root-`<svg>` inheritance fix
([2026-09-23 recent fix](../recent-fixes/2026-09-23-root-svg-presentation-attributes-inherit.md),
issue #1276). `ISvgSourceNode` has no parent link, so the inherited context has to be computed on the
way down in `CollectDefinitions` — but that pass builds patterns/markers/masks **eagerly**, before
`_rootFontSize` (needed for `rem`) is set and before the id registry is complete (needed for a
`clip-path: url(#…)` reference inside definition content), and percentage stroke widths depend on
the enclosing nested-`<svg>` viewport, which that pass doesn't track. Fixing it means restructuring
or deferring definition building — the same ordering trap `BuildDocument`'s clipPath comment
describes — which is out of scope for the one-line root fix.

**Workaround for authors:** set paint properties on the definition's content directly (documented as
a limitation in `docs/supported-svg-features.md`, *Clipping, Masking, Patterns*).

The same applies to font-relative lengths in that content (and in gradient coordinates): `em`/`ex`/`ch`/`cap`/`ic`/`lh` and their
`r*` forms resolve against the initial 16px font with the spec fallbacks, not the definition's ancestors' `font-size`/`font-family`,
because `_lengthBasis` is only set while a rendered element is being built, not while `CollectDefinitions` builds definitions.
