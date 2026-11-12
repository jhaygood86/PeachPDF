# ballentinepointe.com rendering: font format hints, nested selector lists, a growing item in an auto-height column

_Landed 2026-11-12, on top of the growing-item change in [the auto-basis fix](2026-11-12-a-growing-flex-item-starts-from-its-content-not-from-zero.md)._

Found by rendering the site headlessly at 1200px and bisecting its style sheet against headless Chrome.

1. **`format("woff2-variations")` dropped the font.** Every backend's `AddFontFromStream` compares against the plain keywords, so a variable-font hint (Fontsource writes it) fell back to a default face. `RenderContext.AddFontFamilyFromUrl` now reduces the hint (trim, lower case, strip `-variations`) once for all backends.
2. **A nested selector list was resolved as one selector.** In `.pagination { & a, span {...} }` the bare `span` was the whole-document `span`. `StylesheetComposer.ResolveNestedSelector` now splits the prelude on top-level commas (not inside strings, parentheses or brackets) and resolves each selector on its own.
3. **`body { display:flex; flex-direction:column; min-height:100dvh }` with `main { flex:1 }`.** With the growth base at the content size (the auto-basis fix) `flex:1` was already sound for `flex-grow`; what remained was `flex: 1 1 0px` (a zero basis counted 0 high in an auto-height column, footer drawn over the content) and the `min-height` case: the container's main size is clamped by its min-height (css-flexbox-1 §9.2 step 3), so the free space exists and the growing item takes it. `growIntoMinHeight` in `CssLayoutEngineFlex` does this for a non-percentage `min-height`; a longer content keeps its height and a non-growing item leaves the room unused.

Not done: a percentage `min-height` has no basis here and still sizes the column to its content. `<source media>` was checked and is honored (prefers-color-scheme and min-width both select the source).

## The dark-scheme logo was never chosen

The site's logo is `<a><picture><source media="(prefers-color-scheme: dark)" ...><img></picture></a>` and its reset has `img { display: block }`. A block `<img>` inside the inline `<picture>` inside the inline `<a>` is the block-in-inline correction's case, which leaves the content-less `<source>` boxes in front of the picture box (siblings under the `<a>`), not inside it. `ResponsiveImageSelector.EnumerateSources` only looked inside the picture, found no sources and fell to the `<img src>`. It now also takes the `<source>` boxes directly before the picture. The same markup without `display: block` was always fine, which is why a bare `<picture>` test passed. Found by bisecting the CSS down to that one rule.

## Header and footer wrapped where Chrome keeps one row

Both were a wrapping flex row (`.nav-list`, `.site-footer__legal`: `flex-wrap: wrap; gap`) that is itself an item of another flex row. Its flex base size is its max-content, the sum of its items, but each item's own hypothetical size carries a sub-pixel epsilon (`MeasureItem` adds 0.01 so the same words do not wrap again), so the row came out one epsilon per item short of them and `CollectLines` wrapped its last item for nothing. `FitSlack` lets a line overrun by the epsilons it holds. The footer paragraph also sat 8px high: `p { margin-block: 0 var(--space-4) }` is deferred for its `var()` and applied after every plain declaration, so it beat the later, more specific `.site-footer p { margin: 0 }`. A deferred logical longhand is now stamped in `LogicalDeclarationOrder` where it was written (`StampDeferredLogicalOrder`) and keeps that stamp when it is applied.

## Large reductions on the Win2D canvas

`DrawImage` used `CanvasImageInterpolation.Linear` (bilinear) at any scale, so a 4252px logo drawn 100px wide read 4 texels per output pixel and its thin strokes came out dotted. A reduction under 0.5 now uses the cubic mode, which pre-filters. Chrome showed the smooth result for the same file.
