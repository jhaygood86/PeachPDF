# `transform-style: preserve-3d` renders in a shared 3D space (it used to be treated as `flat`)

**Before:** `transform-style` was accepted and ignored: every element was flattened into its parent's plane. A cube built from six faces
under a rotating `preserve-3d` parent was painted face by face in document order (the last face on top, whatever its depth), a face that
turned away still showed, and a `perspective` reached only the direct children of the element that set it, so a grandchild was drawn
without it.

**Now:** an element whose used `transform-style` is `preserve-3d` starts a 3D rendering context. Its children (and their `preserve-3d`
descendants) are planes in one space: their transforms accumulate down the tree, a `perspective` above the context reaches every plane in it,
each plane honours its own `backface-visibility`, and the planes are depth-sorted and resolved per pixel where they intersect. A cube, a
carousel or a flip card written for a browser now looks like it does there. The result is composed as a bitmap at `RasterizationDpi` (text
stays selectable), like a single element in perspective. A grouping property on the `preserve-3d` element (`overflow` other than `visible`,
`opacity` below 1, `filter`, `backdrop-filter`, `clip-path`, a `mix-blend-mode` other than `normal`) still forces it flat, as in browsers, and
`preserve-3d` now establishes a stacking context.

What a document author can notice: a page that set `transform-style: preserve-3d` for a browser and used nested 3D transforms changes
appearance (nearer planes cover farther ones and hidden back faces disappear); a context whose planes all stay parallel to the page at one
depth is unchanged and stays vector; a document targeting PDF/A-1 or PDF/X-1a/X-3 that has a real 3D context is rejected unless it asks for
transparency flattening, as for any other element drawn in perspective.

Verified at the previous release tag (v0.9.19): `docs/html-css-support.md` listed `transform-style` (with `perspective`, `perspective-origin` and
`backface-visibility`) under unsupported CSS features.
