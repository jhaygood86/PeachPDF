# Color grammar, `currentcolor` and the conic gradient: three reasons canvas colors were wrong

**Load-bearing idea.** The 89 `fillStyle.parse` failures were not canvas bugs: the engine's shared `CssValueParser` was wrong. Two lenient layers hid each other. (1) `TryGetColor`'s quick readers (a hand-written scan for `rgb(`/`rgba(`/`#`) took anything they could loosely read and, for hex of any length other than 3 and 6, answered "invalid" without ever asking the CSS-OM token reader (which handles 4 and 8 digits). They now take only the one canonical spelling (`HasOnlyIntegerRgbComponents`, strict) and send everything else to the token reader. (2) The token reader's `Extract` ignored commas, whitespace and anything past the fourth value; `ParseRgb`/`ParseHsl` now use `ExtractFunctionGrammar`, the real grammar. Hue wraps, saturation and lightness clamp, alpha clamps (`Alpha` used `ToAlphaValue`, which returns null for a negative and fell back to opaque).

**Traps found by running it.**
- A probe that sets `fillStyle` to a known color and then to each string, and reads it back, found all of this in one run. Do that before reading code.
- `currentcolor` is resolved when the style is set, against the canvas element's computed `color` (`ScriptDocument.GetComputedValue`), and is black for a canvas not in the document (the ordinal is -1) or a host with no layout. The quick `Colors.GetColor` lookup does not know it.
- The conic gradient was a solid color. A `ConicGradientBrush`'s stop *positions* are the angles in radians (`RenderContext.GetConicGradientBrush`); the canvas passed offsets 0 to 1, so every pixel was past the second stop. The start angle is also measured from 3 o'clock on a canvas and from the top in the brush.
- `addColorStop` threw `IndexSizeError` for NaN; a non-finite offset is a `TypeError`.
- Serialized alpha is the shortest number of two decimals, else three, that reads back as the same byte (`0.502` and `0.5` are both byte 128; `0.499` is byte 127, which is `0.498`).

**Verified.** html-canvas 1,597 files and 2,689 subtests (+95 files); css-color 216 to 230 and css-backgrounds 396 to 425 reftests; the engine, PDF and Scripting suites pass. 45 parser grammar tests and 31 canvas color tests.

**Not done.** A color that keeps its own space when read back: `color(srgb 1 0 1)`, `lab(29 39 20)`, `none`, `color-mix()` and relative colors serialize as sRGB hex today; the Typed OM `CSSRGB`/`CSSHSL`/`CSSColorValue` objects; `currentcolor` in a gradient's color stop; display-p3 canvas storage.
