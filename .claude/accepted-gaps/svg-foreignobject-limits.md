# SVG `<foreignObject>` limits

`<foreignObject>` renders HTML only inside an inline `<svg>` in an HTML document. Not done, tracked in #1653:

- standalone SVG (`<img>`, `data:`, CSS images) builds nothing for it, so a `<switch>` falls through;
- `<a href>` inside the HTML gets no link annotation;
- the content is laid out once as an isolated block and never split across pages (inline SVG is monolithic); it clips to the `width`×`height` viewport;
- nothing inside it joins the tagged-PDF structure;
- percentage `width`/`height` and SVG 2 `auto` are not resolved (the element paints nothing);
- at `PixelsPerInch` ≠ 72 the HTML text scales like ordinary HTML text while the SVG's own shapes do not (an existing split between the two, visible here because both share one box).

Why a nested layout and not a nested document: the HTML is laid out by the ordinary box pipeline (`RunningElementLayout.LayoutRunningElementFor`, the same routine running elements use) from `CssBoxSvg.MeasureWordsSize`, because image/font loading is async and paint must stay synchronous.
