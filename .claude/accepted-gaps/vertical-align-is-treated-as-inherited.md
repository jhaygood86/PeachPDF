# `vertical-align` is treated as inherited, but the spec says it isn't

Tracking issue: [#530](https://github.com/jhaygood86/PeachPDF/issues/530).

CSS 2.1 [§10.8.1](https://www.w3.org/TR/CSS21/visudet.html#propdef-vertical-align) (and current
[CSS Inline Layout 3](https://www.w3.org/TR/css-inline-3/#propdef-vertical-align)) define `vertical-align` as
`Inherited: no` — a descendant with no explicit `vertical-align` should resolve to the initial value
`baseline`, not to an ancestor's declared value. PeachPDF instead lists `"vertical-align"` in
`CssDefaults.InheritedProperties` and copies it unconditionally in `CssBox.InheritStyle`'s always-run
(inherited-properties) section, so a descendant silently inherits it. `src/PeachPDF/CSS/StyleProperties/Text/VerticalAlignProperty.cs`
already disagrees with this — it declares `PropertyFlags.Animatable`, not `Inherited`, for the same property.

**Pre-existing, not introduced by the `ComputedStyle`-per-area-record split** (`ComputedStyleAreas.cs`) that
surfaced it — that change grouped `vertical-align` into `TextArea` alongside the genuinely-inherited
color/text/writing-mode properties because that's where it already behaved, and `TextArea`'s whole-instance
reuse-on-inherit optimization now depends on every property in it actually being inherited, which makes this
gap harder to fix later (splitting `TextArea` rather than deleting one line from `InheritStyle`).

**Not fixed here** because `CssLayoutEngine.ApplyVerticalAlignment` (around line 1984) has non-trivial logic
that already assumes today's (non-compliant) inheriting behavior when resolving a line box's effective
vertical-align, including a `::first-line`-interaction check that compares a box's own `VerticalAlign` against
its owner box's. Making the property genuinely non-inherited requires auditing and likely adjusting that
consumer — a real layout-behavior change, out of scope for a change whose purpose was allocation/architecture,
not spec-compliance.

One concrete, currently-wrong consequence: `vertical-align: unset` resolves to the parent's computed value
instead of resetting to `baseline`, because `CssDefaults.InheritedProperties.Contains("vertical-align")` is
what `DomParser`'s `unset` cascade-keyword arm consults.
