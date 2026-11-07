# shape-rendering, vector-effect, buffered-rendering, math-style, math-depth, math-shift (#2270)

All six were missing from `css-properties.json`, so a declaration was dropped.

- **SVG** values are read in `SvgTreeBuilder.ApplyCommon` through `DeclaredOrInitial` (each has a carrier-only `svg` entry, `applyIn: manual`, as the registry invariant asks). `shape-rendering` rides in `InheritedPaint`; `vector-effect` and `buffered-rendering` do not inherit. `crispEdges`/`optimizeSpeed` wrap a shape's fill and stroke in `Canvas.PushAntiAlias(false)`.
- **`non-scaling-stroke` reads `Canvas.CurrentTransform`, not `TransformScale`.** `RasterCanvas` does not override `TransformScale` (it reports (1,1)), so the first version drew a 10x-wide line under a 10x viewBox on the bitmap canvas; every canvas overrides `CurrentTransform`. The test paints through the bitmap canvas and counts ink rows.
- **Math: there is no "was it declared" bit on a style value**, so `CssBoxMathSourceNode` compares a box's `math-style`/`math-depth` with its parent's (the root, with what the UA sheet gives it: `math { math-style: compact }`, `math[display=block] { math-style: normal }`). Consequence: a value equal to the inherited one does not override an explicit `displaystyle`/`scriptlevel`, and `add(1)` is applied once, where it changes, not again on the children that inherit the text.
- **A `<math>` kept as `MathSourceElement`s still gets its root's CSS**: `MathSourceElement` forwards the root's two values to `CssBoxMathSourceNode`. Elements below the root declare nothing while the tree stays source (any `math-*` rule makes the rule "consequential" and materializes it into boxes).
- **`DisplayStyle` is recorded on math nodes but `MathLayoutEngine` never reads it**, so `math-style` changes the build context and nothing visible yet; same for `displaystyle` before. `math-shift` is parsed only. Tracked in #2280; see `.claude/accepted-gaps/mathml-math-shift-and-display-style-do-not-reach-layout.md`.
- `buffered-rendering` is a caching hint: no engine acts on it, so it is parsed and cascaded only.

Evidence: `SvgAndMathPropertyTests` (PeachBrackets.Tests, pixels), `MathCssPropertyTests` (Engine.Tests, builder context), regenerated `CssomLonghandBaseline.txt`.
