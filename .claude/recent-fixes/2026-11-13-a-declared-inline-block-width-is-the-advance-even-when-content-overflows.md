# A declared inline-block width is the line advance even when its content overflows

`display: inline-block; width: 0` (the WPT references' trick for text that overflows a zero-width anchor) advanced the line by the width of its text, so what followed it sat after the overflow instead of at the box's own edge. Two places only ever *raised* the cursor to the declared width: `ReserveAtomicInlineUsedWidth` (a box that recursed through `FlowBox`) and the declared-width reservation in `FlowBox`'s child loop (a leaf box that holds its words). Both now set the cursor to the declared width exactly when the box kept to its line, a leaf only when it holds words so an empty box measured by an intrinsic pass is not touched (restricting it to words fixed a lost `grid-container-scrollbars-sizing-002`).

`width: 0` itself was not the problem: `LaysOutAsAnAtomicBox` returns false for `available <= 0` and I tried treating a declared zero as atomic; that changed nothing for these boxes and was reverted.

Also tried and reverted: registering the declared-width rectangle even when the box placed words. It made `line-height-applies-to-012` fail (the rectangle took `ActualHeight` before it was final) and did not help any target test.

## Not done
`direction: rtl` on the inline-block (the css-lists `list-style-type-string-*` references) still advances by the text width: the bidi reorder works from word widths and mirrors them, with no notion of the box's declared width, so a bidi isolate of declared width 0 needs its own treatment.
`break-boundary-2-chars-002` flipped to fail by a single pixel at one grey level (sub-pixel noise between `pre` and `normal` runs).
