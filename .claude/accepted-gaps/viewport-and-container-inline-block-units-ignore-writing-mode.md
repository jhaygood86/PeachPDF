# `vi`/`vb` and `cqi`/`cqb` don't follow the root/container's `writing-mode`

Tracking issue: [#795](https://github.com/jhaygood86/PeachPDF/issues/795). Part of the real vertical
`writing-mode: vertical-rl`/`vertical-lr` layout work tracked in
[#547](https://github.com/jhaygood86/PeachPDF/issues/547); see
[.claude/accepted-gaps/no-vertical-writing-mode-layout.md](no-vertical-writing-mode-layout.md) for the
rest of that project's scope.

[CSS Values and Units 4 §6.2](https://www.w3.org/TR/css-values-4/#viewport-relative-lengths) defines
`vi`/`vb` as relative to the initial containing block's size in the inline/block axis of the *root
element's own* writing mode, and [CSS Containment 3 §6.2](https://www.w3.org/TR/css-contain-3/#container-lengths)
defines `cqi`/`cqb` the same way against a query container's own writing mode. Under
`vertical-rl`/`vertical-lr`, `vi` should track the physical height axis and `vb` the physical width axis —
the inline/block axes are rotated 90° from `horizontal-tb`.

`Length.ToPixels` (`src/PeachPDF/CSS/Values/Length.cs`) resolves both pairs as plain physical aliases
regardless of writing-mode: `Unit.Vi` always shares `Unit.Vw`'s `viewportWidthPt` branch and `Unit.Vb`
always shares `Unit.Vh`'s `viewportHeightPt` branch; `Unit.Cqi`/`Unit.Cqb` are aliased to
`containerInlineSizePt`/`containerBlockSizePt` the same way, but those parameters are themselves always
populated from physical width/height, with no writing-mode-aware inline/block resolution anywhere in the
call chain.

This is narrower than, and independent of, the rest of the real vertical-writing-mode layout work: box,
line, flex, and table layout already correctly rotate the inline/block axis via `WritingModeFrame`/
`LogicalPropertyResolver`, but no viewport- or container-unit resolution path consults either of those.
Closing this needs `Length.ToPixels`'s `Vi`/`Vb`/`Cqi`/`Cqb` branches (and the container-unit basis
lookup) to consult the relevant box's resolved `WritingMode` and swap in the block-axis basis when it's
vertical, mirroring how `LogicalPropertyResolver` already does for margin/padding/inset.

The reader-facing note is in `docs/html-css-support.md`'s CSS Viewport Units and CSS Container Queries
sections.
