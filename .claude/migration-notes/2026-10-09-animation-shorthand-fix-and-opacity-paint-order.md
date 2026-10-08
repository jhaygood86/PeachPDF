# `animation`/`transition` shorthands, `opacity` paint order, `@supports (animation-name)`

Three changes a document author could notice, alongside the new opt-in `PdfGenerateConfig.AnimationProgress`
(`--animation-progress`), which changes nothing unless it is set.

- **A non-positioned element with `opacity` below 1 (or a `transform`, `filter`, blend mode) now paints with the
  positioned elements, in tree order.** Before, it painted *before* every positioned box in its stacking context, so a
  positioned parent or earlier positioned sibling's background could cover it. Typical symptom: a half-transparent `<img>`
  inside a `position: relative` header rendered as nothing. It now shows. Output changes only for pages that were
  already losing such an element behind a positioned box; a transform/opacity element that was not covered paints where it
  did. (CSS Color 3 §3.2.) Last release: `git show <tag>:src/PeachPDF/Html/Core/Paint/FragmentPainter.cs` has the
  three-bucket `PaintLayer` without the stacking-context test.
- **The `animation` and `transition` shorthands now expand correctly.** Any `<time>` value in them used to turn every
  longhand into `initial` (`animation: x 2s` set nothing; `transition: opacity 300ms` set a duration of `300ms` *and* a delay
  of `300ms`). Without `AnimationProgress` this has no visible effect today, since neither is rendered, but computed values
  read back and `@supports`/`getComputedStyle`-like consumers now see the real ones.
- **`@supports (animation-name: ...)` is now true** (it was false: the property was not rendered). `@supports (transition-*)`
  is still false. A stylesheet that used `@supports not (animation-name: x) { ... }` as a fallback no longer applies it.
