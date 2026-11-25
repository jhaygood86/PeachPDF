# The img element model: spec srcset/sizes, queued update, initial loads before load

**What.** `Internal/ImageSources.cs` has the HTML algorithms: `ParseSrcset` (descriptor error rules), a small CSS tokenizer for `sizes` (comments, escapes, nested blocks, functions), lengths in every unit and `calc()`/`min()`/`max()`/`clamp()`,
and Media Queries 4 conditions with true/false/unknown (unknown counts as false; `not unknown` is unknown), then `Select` (candidates plus `src` as a 1x entry when none is, widths become densities against the source size, duplicates dropped, smallest
density >= the screen's, else the largest). `ScriptRuntime.Images.cs` runs it as "update the image data": relevant mutations (`HandleImageMutation`) queue the img on a microtask; `currentSrc` is the selected address from then on; the page's own images are loaded
before the window's `load` (their events delivered immediately), so a handler a test sets on `load` is not hit by a late initial load event.
**Frames.** An `iframe` document gets a viewport from its style/attributes (`FrameViewport`), which is what the `sizes` tests in iframes measure against; the images in such documents get their state on first read.
**Evidence.** html-img-canvas-elements 453 -> 1436 of 1819 subtests; 21 new unit tests (`ImageSourcesTests`).
**Not done.** `sizes="auto"` needs a layout width of an image in a document that is never laid out (frames), `sizes-2d` and `viewport-change` need a changing viewport, lazy loading and `decode()` states, `HTMLImageElement.decode()` inside iframes, image maps, `srcset` zoom/density tests.
