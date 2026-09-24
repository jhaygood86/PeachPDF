# SVG definition content inherits from the definition's ancestors (#1277)

**Symptom:** `<svg fill="#fff"><defs><pattern>…<rect/>…` painted black squares; a `<mask>` whose content
relied on an inherited `fill="white"` hid the element it masked; markers ignored an ancestor's
`fill`/`stroke`. Font-relative lengths in that content (and in gradient coordinates) always resolved
against the initial 16px font.

**Cause:** `CollectDefinitions` built gradients/markers/patterns/masks eagerly during the id-collecting
walk, with `InheritedPaint.Initial`/`FontContext.Default` and no `_lengthBasis`; `ResolveClipPath` did the
same lazily. `ISvgSourceNode` has no parent link, and at that point in the walk neither the root font
(`rem`), the root viewport, nor the complete id registry existed, so there was nothing to inherit *from*.

**Fix — the load-bearing idea:** don't build during the walk. `CollectDefinitions` now records each
definition with the chain of ancestors between the root and it (`DeferredDefinition`), and
`BuildDeferredDefinitions` builds them after the root context is final, in document order.
`ResolveAncestorContext` replays the chain the way pass 2 walks down (context-only `ApplyCommon` +
`ComputeFontContext` per ancestor, and the nested-`<svg>`/`<symbol>` viewport swap via `EnterViewport`),
then `EnterDefinition` layers the definition element's own properties on top. Replaying a recorded path
rather than threading a context through the collect walk is what makes it cheap: a document with no
definitions pays nothing, and each definition pays O(depth).

**Traps found while doing it:**
- `ApplyCommon` resolves `clip-path: url(#id)` as a side effect, and building a clipPath now replays an
  ancestor chain *through `ApplyCommon`* — so the root/ancestor/definition context calls run with
  `_contextOnly` set, which skips that side effect. Without it, `<g clip-path="url(#c)"><clipPath id="c">`
  recurses. `_resolvingClipPaths` additionally guards a shape inside a clipPath that clips by that clipPath.
- Inheritance makes cycles reachable that were previously only reachable by hand-authoring:
  `<svg fill="url(#p)"><pattern id="p">…</pattern></svg>` makes the pattern's content paint the pattern,
  and `marker-end` on an ancestor puts the marker inside itself. The builder resets a self-referencing
  inherited pattern paint to `none` and drops inherited `marker-*` references to the marker itself (a reference to
  a *different* marker is ordinary inheritance); the renderer additionally caps pattern-tile/marker nesting
  (`MaxDefinitionNesting` = 4, kept low because a cycle still fans out k^depth) as a backstop for indirect cycles (a → b → a).
- The replayed ancestor chain is memoized per ancestor node (`_ancestorContexts`): each step is a full
  presentation-property cascade, and sibling definitions (thousands of gradients under one `<defs>`) share
  their ancestors' instances.
- `ResolveUrlPaintKind` used to ask `_document.Patterns`/`Gradients` whether an id is a pattern; with the
  deferred build neither is populated yet when an earlier definition's content is built, so it asks
  `_nodesById` (the registry, complete before any build). This also fixes definition content painting with a pattern defined *later* in the document, which
  classified as a gradient ref and painted nothing.
- Gradients moved into the deferred build too, only so their `userSpaceOnUse` coordinates get a
  `_lengthBasis` (`ParseGradientCoordinate` gained an optional basis; objectBoundingBox mode is unchanged).
  Side effect: a `%` user-space gradient coordinate inside a nested `<svg>` now resolves against that
  viewport, not the root's.
- `<filter>` stays eager: nothing in a filter inherits from its ancestors.

**Evidence:** `SvgDefinitionInheritanceTests` (21 builder-level cases, each asserting a literal inherited
value) and three `SvgIntegrationTests` cases; the issue repro, an inherited-white mask, marker
fill/stroke inheritance, a self-referencing pattern and an `em` gradient rasterized through both PDFium and
MuPDF and agree. The former accepted gap (definition content + gradient font-relative coordinates) is closed.
