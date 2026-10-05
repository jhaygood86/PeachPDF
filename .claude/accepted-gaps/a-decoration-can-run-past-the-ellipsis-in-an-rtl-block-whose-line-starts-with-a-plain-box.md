# A decoration can run past the ellipsis in an RTL block whose line starts with a plain box

Tracked as [#1631](https://github.com/jhaygood86/PeachPDF/issues/1631).

```html
<div style="direction:rtl; width:200px; overflow:hidden; white-space:nowrap; text-overflow:ellipsis">
  x <span style="text-decoration:underline">long Latin text ...</span>
</div>
```

PeachPDF draws the underline across the whole width (0-120 in the review's repro); Chrome draws 20-120.

**Cause:** a decoration finds its line's cut by replaying the word painter's decision over every box on
the line in **tree order**, the first box that cuts winning (`EllipsisCutOf`,
`FragmentPainter.TextOverflow.cs`; it mirrors `_linesAlreadyTruncated`). In an RTL block with Latin
runs the plain box `x ` comes first in tree order but sits at the visual left, so it plans a cut of its
own (anchor well off the box) and wins, while the decorated span holds the cut that is actually drawn.
The decoration is then clamped to the wrong anchor and effectively not clamped at all.

**Spec rule:** css-text-decor-3 §2 decorates the kept inline content, and css-overflow-4 `text-overflow`
truncates in visual order. Tree order is only visual order on a single-direction line.

**What is fine:** a pure LTR or pure RTL line, and Hebrew or other RTL runs inside an LTR block (the
review measured those against Chrome). The ellipsis placement has always been planned per box in
physical order, so it carries the same limitation; the decoration only inherits it.

**Why out of scope:** doing it right means ordering the contributing boxes of a line by visual position
(or recording the cut while painting and deferring the decoration), which the paint-time truncation
does not model.

User-facing note: the `text-overflow` row in `docs/html-css-support.md`. Closing the gap means deleting
this file and that sentence.
