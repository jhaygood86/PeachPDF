# A paint-only hover/focus change no longer reloads the document

Every pointer or focus change that touched a `:hover`/`:active`/`:focus` rule ran `ReloadAsync`: HTML
parse, CSS fetch + parse, index, cascade, box corrections, layout, new fragment tree - on a real
Tailwind page that is the cost of loading it, per mouse move. `ApplyStateAsync` now tries
`InteractionRestyler.TryApply` first and keeps `RestyleAsync` (the reload) as the all-or-nothing fallback.

**Load-bearing idea.** Cascade in place, reload on any surprise. Nothing is rolled back: the fallback
reload rebuilds the whole tree, so a half-restyled tree is simply discarded. That is why the design needs
no shadow boxes and no undo; do not add a path that gives up and keeps the tree.

**How it works.**
1. While cascading, `CssData` marks each `CssBox` whose candidate list holds a state-reading rule
   (`HasStatefulCandidates`; `HasRelationalStatefulCandidates` when the rule reads more than the box's own
   state - combinator, `:has()`, `:is()`, nesting - judged from the selector text in `RuleSharing`).
   Siblings that adopt a shared style copy the flags (`SiblingStyle.Stateful`), since they skip matching.
   The user-agent sheet's `:focus` rule reaches *every* box, which is why a box with only own-state rules
   is looked at only when its own ordinal is in `InteractionState.Difference`.
2. `CssData.CompareStatefulMatches` matches only the stateful candidate rules under the old and the new
   state (`container.Interaction` is swapped) and compares the matched sets. A thread-static probe
   (`_creationProbe`) makes the pseudo-element synthesis in `DoesSelectorMatch` record instead of
   creating; a difference in what would be created is verdict 2 -> fall back.
3. The boxes whose set differs (outermost only) are cascaded again with their subtree
   (`DomParser.RecascadeForState`, probe on, against `PreviousPassContainerSizes`).
4. Each box's old `ComputedStyle` (kept by reference before the cascade) is compared with the new one:
   areas first, then `CssUtils.SnapshotProperties` by name. Every changed name must be in
   `InteractionRestyler.PaintOnly`.
5. The derived caches the setters do **not** clear are invalidated by hand (`InvalidatePaintCaches`;
   `InvalidateBackgroundColor`/`InvalidateMixBlendMode` were added - background colour had none).
6. Dirty regions are `StyleUpdater.DirtyRegionsForExtents`: the fragments of each changed box and what is
   inside it, widened by the larger of the old and new paint reach (box-shadow, outline, text-shadow,
   blur/drop-shadow via `FragmentPainter.ShadowBleed` etc., and a generous bound for a transform).

**Found by running it.**
- `FormRestyleTests.ARestyleForAnotherReasonAlsoSatisfiesTheWaitingChange`: a pending form change (`NeedsRestyle`) is in
  the tags only after a reload, so the fast path must not run while `_restyleOrdinals` is non-empty.
- Every box carries the UA `:focus` rule, so "any stateful candidate" flagged the whole tree; the
  relational/own-state split fixed that.
- `transform`/`filter` going none <-> something changes the containing block of positioned descendants
  (layout!), so that flip falls back when the box has absolutely/fixed positioned descendants.

**Safety nets.** `HtmlDocumentOptions.IncrementalRestyle` (default on) and `PEACHPDF_FULL_RESTYLE=1`
force the old path. `HtmlDocument.VerifyIncrementalRestyle` (internal, per document) reloads after every
in-place restyle and throws if any box's computed style or any page's geometry differs; the engine tests
run the whole interaction corpus under it.

**Not done.** Edited and declarative documents (`NotApplied` as before). Transitions. `visibility`,
`background-image`, `content`, anything that can load a resource or change layout stays on the reload.
