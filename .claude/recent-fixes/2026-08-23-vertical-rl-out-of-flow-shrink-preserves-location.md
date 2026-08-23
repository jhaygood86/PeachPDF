# Vertical-rl out-of-flow shrink preserves Location.X

_Landed 2026-08-23._

[Issue #798](https://github.com/jhaygood86/PeachPDF/issues/798): `CssLayoutEngine.ShrinkAutoWidthTo`
(added for #761) shrinks a vertical box's auto width to its content's own block-axis extent. For
`vertical-rl` (`WritingModeFrame.BlockStartIsRight`), it did this by capturing the pre-shrink
`ActualRight`, overwriting `Location.X` with a content-derived value, then re-applying the captured
`ActualRight` — correct for an ordinary in-flow box, where `Location.X` is just wherever the parent's
block-axis cursor put it, genuinely free to move. For an absolutely/fixed-positioned box, `Location.X`
was already set by `CssBox.CommitBlockChildOffset` from the box's own CSS `left` offset *before* this
box's own content layout ran — a real, CSS-meaningful value, not a placeholder — so overwriting it
silently discarded the offset.

**First cut (gate on `!IsOutOfFlow`) was too broad.** The obvious minimal fix — skip the shrink entirely
whenever the box is out-of-flow — used `CssBox.IsOutOfFlow` (`IsFloated || Position is Absolute or
Fixed`), which also matches floats. A float's `Location.X` comes from `CommitBlockChildOffset`'s
*flow-stacking* branch (`CssLayoutEngine.FloatBox` avoidance), not a CSS offset — exactly as free to
move as an ordinary in-flow box's, and was never buggy. Gating on `IsOutOfFlow` would have silently
stopped floats from shrinking to content too. A code-review pass (two independent finder agents, one via
`code-review`, one custom) caught this from first principles by reading `CommitBlockChildOffset` itself
rather than trusting the `IsOutOfFlow` name. Narrowed to `Position.Value is PositionMode.Absolute or
PositionMode.Fixed`; a new test (`VerticalRl_FloatWithAutoWidth_StillShrinksToContent`) pins that floats
keep shrinking.

**Skipping the shrink entirely (rather than reconciling it) was itself checked and rejected.** A
diagnostic run showed `Position.Fixed` with a single-glyph auto-width span landing at **150pt** wide
(not content-sized at all — `CssLayoutEngine.GetBoxWidth`'s shrink-to-fit branch is gated on
`Position.Absolute` only, excluding `Fixed`), and `Position.Absolute` with a block-level child at
**~206pt** (the container's full available width). Position was correct; width silently stopped
shrinking to fit in exactly the cases PeachPDF's general (non-vertical-aware) width resolution doesn't
already produce a tight value. Real fix: `Location.X` stays pinned, and the box's already-laid-out
content — words and descendants, placed by `CreateVerticalLineBoxes`/`LayoutVerticalBlockChildren`
against a pre-shrink placeholder anchor (`ClientRight` at frame-construction time, unrelated to the true
`left` offset) before the true content extent was known — is reconciled against the pinned edge with a
uniform physical-X shift (`WritingModeFrame.ToPhysical` is a pure translation for a fixed frame, so the
gap between the two anchors is provably a constant delta, not a reordering).

**The shift primitive already existed, just coupled to moving `Location` too.** `CssBox.OffsetLeft`
already deep-translates a box's own `Rectangles`/`Words` and recurses into `Boxes` (skipping a descendant
whose containing block lies outside the translation root via `EscapesTranslationOf`), then moves the
box's own `Location.X` and fires `OnTranslated`/fragment-tree invalidation. Factored the
`Rectangles`/`Words` part out into `ShiftOwnLineGeometryLeft`, shared by the existing `OffsetLeft` and a
new `CssBox.OffsetContentLeft` that does everything `OffsetLeft` does *except* move `this.Location` —
exactly what's needed when the box's own position is pinned but its content isn't. `Word.Left`/`Top` are
absolute physical coordinates (not box-relative), confirmed by tracing `CssRect`'s backing field, so this
shift is load-bearing, not optional — without it the content stays painted at the old placeholder
position while the box's own reported bounds shrink around a different one.

**A third, unrelated bug surfaced while verifying the width fix, and got fixed in the same change** (the
same block-content diagnostic that exposed the `Position.Fixed`/`Position.Absolute` gaps above also hit
it): `CssBox.LayoutVerticalBlockChildren` measures a same-writing-mode block-level child's `childWidth`
from `ResolveOwnInlineSize`'s pre-content-layout (stretch-to-available) estimate — *before* that child's
own auto-width shrink, which only runs later inside `LayoutContentAtItsAssignedPosition`, has actually
narrowed it. Reproduced with a plain **in-flow** `vertical-rl` box (no positioning involved at all):
width stayed at ~555pt (full available width) regardless of #798. Fixed by re-reading the child's true
width (`childBox.ActualRight - childBox.Location.X`) immediately after its own content layout runs, so
the `logicalBlockOffset` accumulator reflects what the child actually occupies.

**A first cut of that fix also re-derived the child's `Location.X`, which double-shifted it.** The
reasoning seemed sound — mirror `ShrinkAutoWidthTo`'s own in-flow branch and move `Location.X` against
the corrected width for `vertical-rl` — but missed that the child, if it has its own recursive
`ShrinkAutoWidthTo` call (inline-only content dispatching to `CreateVerticalLineBoxes`, or block content
to a nested `LayoutVerticalBlockChildren`), has *already* done exactly that: it captured its own
pre-shrink `ActualRight` (the fixed block-start anchor this outer loop placed it against) before its own
content layout touched anything, and moved its own `Location.X` against that same anchor. Re-deriving
`Location.X` again here from the stale placeholder `childWidth` applied that shift a second time on top
of an already-correct position. A second review pass caught it by actually running the code: the new
test below only asserted width, which stayed correct throughout (translating a box moves `Location.X`
and `ActualRight` together, leaving `Size.Width` — and so the assertion — unchanged), while `Location.X`
silently landed the child's block-start edge **540pt past its own parent's content-box edge**. Fixed by
deleting the `OffsetLeft` call outright — the child needs no position correction from its parent at all,
`vertical-lr` or `vertical-rl` alike, only the accumulator does — and the test now also pins
`box.ActualRight` against `wrapper.ClientRight` so a reintroduced double-shift fails loudly.

Verified: rasterized the issue's own repro (both PDFium and MuPDF agree — the box sits tightly at (5pt,
5pt) from its container, sized to the glyph, not stretched or misplaced); full net8.0 suite (9112
passing, zero regressions); zero-warning `dotnet build -t:Rebuild`; every new/changed line covered
(checked directly against the coverage XML, not assumed). Six new tests in
`VerticalWritingModeLayoutIntegrationTests.cs` cover: absolute/fixed with auto-width inline content
(position *and* width), floats (unaffected), and both absolute and plain in-flow boxes with auto-width
block-level children (position *and* width, after the double-shift fix above).
