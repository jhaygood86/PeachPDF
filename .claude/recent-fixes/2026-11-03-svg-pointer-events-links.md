# pointer-events inside inline SVG decides whether an `<a>` writes its link

HTML `pointer-events: none` already dropped link annotations; the SVG `<a>` path (`SvgRenderer.CollectLinks`) never looked at it. `SvgElement.PointerEvents`/`SvgDocument.PointerEvents` now keep only what an element *declares* (null = inherit) and the link walk does the inheritance from the host box down, so no entry was needed in the inherited-paint record. An `<a>` keeps its link when some shape (or text) under it can be hit for its effective keyword: `none` never, `auto`/`visiblePainted` need visibility and a fill or stroke, `visible*` need visibility, `painted` needs paint, `fill`/`stroke`/`all` always. An empty `<a>` keeps its link unless `none`.

Not done: shape-level hit testing for the screen APIs, and a tighter link rectangle (see the accepted gap).
