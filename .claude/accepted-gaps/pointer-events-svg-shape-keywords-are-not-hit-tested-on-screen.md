# SVG `pointer-events` shape keywords do not shape the on-screen hit test

`pointer-events` is parsed with its full SVG keyword set (`visiblePainted`, `visibleFill`, `visibleStroke`, `visible`, `painted`, `fill`, `stroke`, `all`). For link annotations an inline `<svg>`'s `<a>` is dropped when no shape under it can be hit under those keywords (`SvgRenderer.CanReceivePointerEvents`, which reads visibility and whether the shape has a fill or stroke). But the screen hit test (`GetElementAt`, `GetCursorAt`) works on HTML box fragments: an inline `<svg>` is one box, so a shape inside it is not a separate target and every keyword other than `none` and `auto` behaves as `auto` there. Hit-testing a point against a shape's fill and stroke geometry would need the SVG renderer to expose per-shape geometry to the hit test.

Tracked in #2154.

A link rectangle is also the whole `<a>`'s bounding box: when only some shapes under it can receive pointer events, the rectangle still covers the others.
