# `overflow-clip-margin` has no per-side longhands

`overflow-clip-margin` is supported in the form every browser ships: one `<visual-box> || <length [0,∞]>`, applied to both axes that are cut without scrolling (`overflow: clip`, or paint containment), with the corner radii growing by the margin and a square corner staying square. The per-side longhands of the current Editor's Draft (`overflow-clip-margin-top` and the other physical sides, and the logical `-block-start`, `-inline-end` and so on) are not properties here, so a different margin on one side cannot be asked for.

Why left: nothing seen uses them, and each side would need its own visual box and its own corner arithmetic on top of the geometry in `RenderUtils.OverflowClipEdgeOf`. Tracking issue: [#2310](https://github.com/jhaygood86/PeachPDF/issues/2310).
