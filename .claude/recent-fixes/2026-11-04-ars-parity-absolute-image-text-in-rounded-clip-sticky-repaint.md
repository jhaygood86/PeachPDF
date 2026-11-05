# Three things found comparing arstechnica.com with Chrome (WinUI demo)

**1. An absolutely positioned `<img>` sat lower than its box.** Tailwind's preflight gives every image `display: block; vertical-align: middle`. An absolute inline element is wrapped
in a synthetic absolute block (`DomParser.CorrectAbsolutelyPositionedInlineElements`), and the image stayed an inline in that wrapper's line, so `vertical-align: middle` lifted it
about half its height (a 16:9 card image started 118px down and was clipped, with the box under it blank). The wrapped image is now blockified with baseline alignment (CSS 2.1 9.7).
`AspectRatioCardTests` (the card in a block, in a half-width item of a reversed flex row, with the preflight rule) fails without it. Found by dumping the real page's box tree from a
throwaway engine test that fetched it live: the geometry of the card box was right, the image word's `Top` was not.

**2. Text inside a rounded `overflow: hidden` card was drawn over a black rectangle.** The GPU ClearType blend samples the render target, but Direct2D draws what is inside a geometry
clip (rounded corners) into a layer composited at the pop, so the card's own fill is not in the target yet and the shader blended against the page behind the card. `Win2dCanvas`
now treats text inside an open geometry clip layer as non-GPU, non-LCD (`InsideGeometryLayer`): grayscale text on the correct backdrop. Rectangular clips are axis-aligned and unaffected.
**Trap:** the Win2D unit test (`GpuTextTests`) passes without the fix on the WARP software device, so it is a guard for the picture, not proof of the bug; the demo is the evidence.

**3. Every scroll step repainted a whole 1200 x 2016 tile when the page had a sticky header.** `HtmlView.UpdateViewport` invalidated the base surface for each region a moved sticky or
fixed box reported, and the virtual control repaints whole tiles (about 24 ms). The base surface holds the content layer, which leaves those boxes out, so only the overlay needs to
redraw. Measured on the Ars page (Release `--scroll-bench`), steady drag: UI latency p95 30.8 -> 0.2 ms, painting while scrolling 4,312 ms -> 176 ms, 132 callbacks over 16 ms -> 5.
**Trap found by review:** a sticky box is in the attached layer only while it is displaced, so when it begins to stick (or is released) the content-layer surface still has (or lacks) its pixels at the natural place.
`HtmlDocument.ViewportLayersChanged` (new, public) reports that transition and `HtmlView` repaints the returned regions on the base surface only then (the benchmark above includes this: p95 0.3 ms).
`background-attachment: fixed` documents are not affected (they use the viewport mode, which never painted the base surface for them).

**Not done.** Text in rounded cards is grayscale, not ClearType, until the layer content can be sampled. Load is about 2.6 s of which layout 1.1 s, cascade 0.6 s, fetch 0.6 s.

**4. Images and web fonts are fetched side by side.** Layout awaited each image in turn (37 on the Ars page, ~500 ms inside layout) and fonts were registered one at a time.
`IResourcePrefetcher` (new, public) lets the engine hand a render context the addresses first (`HtmlContainerInt.PrefetchImagesAsync` before the first layout pass, once per
document, skipping `display: none` subtrees; `DomParser.CascadeApplyStyleFonts` for the first `url()` of each face); `HostResourceLoader.PrefetchAsync` fetches them eight at a
time into the response cache. `image.load` fell from ~500 ms to ~100 ms and layout from ~1130 to ~880 ms (the prefetch itself is ~180 ms), so load is about 2.3-2.6 s.
Only `HtmlView`'s context implements it, so PDF generation is unchanged. Trap: a relative `url()` in an inline `<style>` has no base and registering the face already
refuses it, so it is not prefetched either.

**5. Grid auto-placement ignored `order`** (found by laying the Chrome and engine grids side by side: the article list was in the right two columns, the ad column first).
`CssLayoutEngineGrid.PlaceItems` now sorts the items stably by `order` before placing them and restores source order for the result. **Not done:** painting and
hit-testing order for grid items by `order` (CSS Grid 1 section 8 says it applies there too). `:is()`/`:where()` now forgive an invalid argument (see the migration note).

**6. An inline image with `vertical-align: middle` was placed half its height too low.** Tailwind's preflight gives every `img` and `svg` `vertical-align: middle`; the arstechnica.com footer logo
(`h-14`, a bottom margin) spilled over the paragraph under it. `GrowLineToItsExtent` sized the line as if the image sat on the baseline (the `default` arm), while
`ApplyVerticalAlignment` later moved it to the middle, so the line was short by about half the image and the image ended lower than the line. The line is now sized for a `middle`
image as its margin box centred on the baseline plus half the parent's x-height (CSS 2.1 10.8.1), and `VerticalAlignDeltaOf` aligns by the margin box's midpoint, not the border box's.
Found with a throwaway engine test that fetched the page live and dumped the logo's word position next to its line; the unit test that first "reproduced" it passed because it
checked only where the next block started, not where the image was (`InlineSvgLineHeightTests` now checks both). Engine 18,135 and PeachPDF 1,615 pass.
