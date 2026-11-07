# MathML: `math-shift` and display style do not change the layout yet

`math-shift: compact` (cramped style) is parsed and cascaded but the math layout has no cramped path (no cramped superscript shift), and `MathNode.DisplayStyle` - set from `math-style` or `displaystyle` - is not read by `MathLayoutEngine`. Both are tracked in #2280. Fixing it means a cramped flag on `MathBuildContext`, the cramped OpenType MATH constants in `MathMetrics`, and the display-style rules (large operators, limits, fraction gaps) in `MathLayoutEngine`.

Also accepted: a `math-style`/`math-depth` equal to the value an element inherits does not override an explicit `displaystyle`/`scriptlevel` attribute on it (the cascade keeps no "declared" bit).
