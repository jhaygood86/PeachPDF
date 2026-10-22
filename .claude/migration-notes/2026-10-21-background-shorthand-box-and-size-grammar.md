# The `background` shorthand: one `<box>` sets origin and clip, and `/ <size>` needs a position

- `background: url(a.png) content-box` set only `background-origin`; the clip stayed `border-box`. A single box keyword now sets both `background-origin` and `background-clip` (CSS Backgrounds 3 §2.10); with two, the first is the origin and the second the clip, as before. The image is now clipped to that box (a dashed border no longer has the image under it), in every layer.
- `background: url(a.png) / 10px` applied the size. The grammar is `<bg-position> [ / <bg-size> ]?`, so the position must come before the slash; the declaration is now dropped as invalid, as in a browser. `url(a.png) 0 0 / 10px` is unchanged.
