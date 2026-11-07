# `isolation`, `background-blend-mode`, `color-scheme`, `light-dark()`, `print-color-adjust`

**Blend.** `isolation: isolate` is a stacking-context trigger (`DomUtils.IsStackingContextBox`) and forces `transform-style` flat. The painter only builds a group for it when something inside blends (`DomUtils.NeedsIsolatedGroup`): an isolated group with nothing blending paints the same as the plain content, and a PDF form XObject per isolated element is not free. `background-blend-mode` wraps `PaintBackgroundCore` in a `BeginLayer` and pushes the layer's `PaintBlendMode` around each image or gradient; the colour fill is the backdrop. The group is what stops the multiply reaching the page behind (a test pins a red page behind a blended yellow/cyan box).

**Colour scheme.** The used scheme is `ColorScheme.UsesDark(preferDark)`: both light and dark named -> the reader's preference; one named -> that one; `normal` -> light (so `prefers-color-scheme: dark` alone never darkens a page). Colours are text parsed with no element, so the element opens a `[ThreadStatic]` `ColorSchemeScope` around the read (`CssBox.GetActualColor`, `FragmentPainter.PaintFragment` for gradients and shadows). `Colors.GetColor` and `light-dark()` read it.

**Traps found by running it.**
- Named colours are normalised to `rgb()` by the CSS-OM at parse time, which fixed `Canvas`/`CanvasText` at their light value; the scheme-dependent system colours are now kept as keywords (`Converters.SchemeDependentSystemColors`, ahead of `PureColorConverter`).
- The CSS-OM `light-dark()` converter must check both arms are colours (`ColorPairValueConverter`); with the lenient `Any` an invalid `light-dark(red)` was accepted and drew nothing, wiping the earlier valid declaration.
- Gradients are parsed in the `background-image` setter, before a later `color-scheme` in the same rule would apply, so `AssignCssBlock` applies a plain `color-scheme` first. A later *rule* is an accepted gap (see the accepted-gaps folder).
- The default text colour is the literal `black` the cascade keeps for the initial value (an authored colour is `rgb()`), which is how a dark box is told apart from one that wrote `color: black`.

**Not done.** `print-color-adjust` has no effect on paint: a PDF is not ink-limited and both values permit drawing every colour. Form controls draw in the same colours in either scheme.

**Evidence.** PeachBrackets.Tests (911), PeachBrackets.Engine.Tests (18491), PeachPDF.Tests and the generator tests pass; `BlendAndColorSchemeTests` pixel-checks each behaviour.
