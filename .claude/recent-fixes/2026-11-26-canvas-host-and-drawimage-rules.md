# Canvas host size and drawImage rules (Phase 3, first part)

**Load-bearing idea.** The HTML "rules for parsing non-negative integers" are one function (`HtmlIntegerParser.TryParseNonNegative`), used by the canvas box, the script runtime and, through the public `ScriptDocument.TryParseNonNegativeInteger`, hosts: skip ASCII white space, an optional `+`, the leading digits, ignore the rest (`"100.999"`, `"100em"`, `"100%"` are 100, `"0x100"` is 0, `"1e2"` is 1), error otherwise or above 2^31-1 (the default 300x150 then). The `width`/`height` IDL setters are `unsigned long`: `ToUint32` (modulo 2^32, `"400x"` is 0), and the default when above 2^31-1.

**Traps found by running it.**
- Two places read the attribute: the runtime's `CanvasSize` and the engine's `CssBoxCanvas.Attribute`. A third, the generic `width`/`height` presentational hint in `DomParser` (dimension-value rules: `100%` became a percentage width, `getComputedStyle` echoed `1e2px`), had to skip canvas and apply the integer rules instead.
- A canvas as the source of `drawImage` with no context is a valid transparent source (it used to be a `TypeError`); one with no width or height is an `InvalidStateError`, with or without a context.
- An image that is not loaded yet, or has nothing to load, is silently not drawn; only a broken one (status 3) is an `InvalidStateError`. A closed `ImageBitmap` is an `InvalidStateError`, not a `TypeError`.
- A rectangle with a negative width or height is the same rectangle with its origin moved (`Normalized`), it does not flip the picture; passing it straight to a backend drew a mirrored image.
- An SVG `img` has no `Core.Image`: `ScriptDocument` keeps the loaded `SvgDocument` and `DrawSvgImage` renders it into the destination (clipped), as vector content.
- A page whose only content is an empty 0x0 canvas has no pages: a test helper that paints indexes `Pages[0]` and fails. The host tests load without painting.

**Verified.** html-canvas 1,667 files / 2,811 subtests (+43 files, +89 subtests); every non-manual `canvas-host` and `drawing-images-to-the-canvas` file passes except the detached-`OffscreenCanvas` one (Phase 7). Engine, PDF and PeachBrackets suites pass; zero warnings.

**Not done in this part.** Missing-argument `TypeError`s, `getContext` argument validation, the non-writable prototype, and the `ImageData` class (next in Phase 3); text state in `reset()` (Phase 6); source rectangles that reach outside the image (the manual `drawimage_*` scenarios).
