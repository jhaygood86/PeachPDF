# Vertical rtl block children anchor to bottom edge

_Landed 2026-08-20._

[Issue #778](https://github.com/jhaygood86/PeachPDF/issues/778): `CssBox.LayoutVerticalBlockChildren`
(added for #760) anchored every stacked block child's cross-axis (physical Y) position to `ClientTop`
unconditionally, matching `direction: ltr`. Under `direction: rtl`, CSS Writing Modes 4 places a
vertical box's inline-start at the physical *bottom* edge instead, so its block children should hang
from there — a shorter child's own unused space should appear as a gap at the top, not the bottom.

**Fixed with a "lay out everything forward, then reflect once" pass, not a naive edge swap.** A
child's own cross-axis extent (height) isn't knowable until *after* its own content lays out — unlike
its block-axis extent (width), which `ResolveOwnInlineSize` already resolves before content layout
runs, letting the existing right-edge (`vertical-rl`/block-axis) anchoring position X correctly up
front. So the child loop itself is untouched: every child is still placed flush against `ClientTop`,
growing down, exactly as `ltr` wants. Every stacked child is collected into a new
`WritingModeFrame.InlineStartIsBottom`-gated list (mirroring the existing `BlockStartIsRight`) and
reflected afterward, reusing `CssLayoutEngineTable.ReflectRowAxisForVerticalRl`'s own exact formula
(`delta = (min + max - farEdge) - nearEdge`, then `OffsetTop`/`OffsetLeft` to deep-translate the
child's own words/rectangles/descendants along with it and un-freeze any fragment slot an earlier
pass already froze) one axis over — the table engine solved the identical problem for a `vertical-rl`
table's own row axis (a row's own row-axis thickness isn't knowable until it's placed, either).

**Where the reflection runs mattered as much as the formula.** A first cut reflected inline, inside
`LayoutVerticalBlockChildren` itself, computing the far edge locally: `maxCrossExtent` for auto
height, or the raw `height:` CSS value (via `CssLayoutEngine.DefiniteContentHeight`, temporarily
widened to `internal` for the reuse) for a definite one. A four-angle post-change review (two
independent finder agents, converging on the same defect) caught that this anchors every child
against a *pre-clamp* edge: `min-height`/`max-height` are only applied later, in
`PerformLayoutEpilogue` → `CssLayoutEngine.ApplyHeight`, well after `LayoutVerticalBlockChildren`
returns. A `min-height` taller than the children's own combined content left every child hanging from
the box's content-only extent instead of its real, min-height-driven bottom — reintroducing a version
of #778's own symptom, just gated on `min-height`/`max-height` instead of plain auto/explicit height.
Under `ltr` this never mattered (children anchor to the fixed, position-independent `ClientTop`
regardless of how tall the box ends up), so it's a genuinely new failure mode this fix's own first cut
introduced, not a pre-existing gap it merely failed to close.

**Fixed by deferring the reflection to the epilogue.** `LayoutVerticalBlockChildren` now stashes the
stacked-children list on a new `CssBox._pendingCrossAxisRtlReflection` field instead of reflecting
immediately; `PerformLayoutEpilogue` consumes and clears it right after `CssLayoutEngine.ApplyHeight`
runs, using this box's own live `ClientTop`/`ClientBottom` as `min`/`max` — by that point `ApplyHeight`
has already resolved `min-height`/`max-height`/a percentage height against an indefinite containing
block, so the reflection needs no awareness of any of that itself, and `DefiniteContentHeight` reverted
back to `private` (no longer needed outside `CreateVerticalLineBoxes`). Verified byte-identical
rasterization (both PDFium and MuPDF) of the `writing_mode` showcase before and after moving the
reflection from `LayoutVerticalBlockChildren` into the epilogue — confirming the refactor changed
nothing for the cases the first cut already got right. New tests in
`VerticalWritingModeLayoutIntegrationTests.cs` pin auto-height reflection (a shorter and a taller
sibling), an explicit-height box taller than every child, `vertical-lr` + `direction: rtl` (the
cross-axis reflection is independent of which physical edge the block axis itself grows from), and the
`min-height`-taller-than-content case the review caught. Full net8.0 suite (9008 passing) and a
zero-warning `dotnet build -t:Rebuild` both pass.
