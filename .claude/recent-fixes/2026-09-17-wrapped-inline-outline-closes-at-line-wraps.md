# Wrapped inline outlines close at every line wrap (issue #1163)

`.claude/accepted-gaps/wrapped-inline-outlines-have-open-break-edges.md` (deleted by this change)
scoped this out of #1162's shared border/outline painter work as needing "an outline-specific
fragment-union/minimum-outline algorithm". It didn't: `HasLeftEdge`/`HasRightEdge`
(`BoxDecorationGeometry`, sourced from `FragmentEmitter.SliceGeometriesOf`'s inline-axis strip
comparison) is purely an inline-axis line-wrap signal, entirely separate from `HasTopEdge`/
`HasBottomEdge` (the page/column-break signal). The fix is one change at the outline-paint-entry
collection site in `FragmentPainter.PaintBoxContent`: force `HasLeftEdge`/`HasRightEdge` to `true` for
the outline's own paint entries only, leaving the same-loop border/background painting (which reads
`geometry.HasLeftEdge`/`HasRightEdge` directly, a few lines earlier in the same method) untouched.
`OutlineDrawHandler.DrawOutline` needed no changes at all — it already closes whichever edges it's told
are real; it had just never been told both were real on an interior line before.

## What was actually found by running it, not by reading it

The two pre-existing wrapped-inline outline tests
(`OutlineOnAWrappingInlineElement_...`/`RoundedOutlineOnAWrappingInlineElement_...` in
`OutlineStylePaintIntegrationTests.cs`) both asserted the *old* open-edge geometry, so both had to be
rewritten, not just re-verified — and doing that surfaced two non-obvious mechanics:

- **Forcing all four edges closed changes which `BoxEdgesDrawHandler` code path a line's outline
  takes**, not just its exact reach. A plain (non-rounded) box's interior line used to be `complete ==
  false` (open inline edges), so it painted through the per-side polygon drawer
  (`DrawGeneralSide`/`DrawPolygonCall`, `Stroked: false` fills at full outward reach). Once all four
  edges are true, `complete` flips to `true` for *every* line, routing even the non-rounded case
  through the single-ring drawer. For the **rounded** case specifically, that ring is a `Stroked: true`
  path traced along the stroke's *centerline* (`DrawRoundedBoxEdges`), not a filled donut — so
  `TestRecordingGraphics`'s point-derived `Bounds` sit at `offset + width/2` from the box edge, not the
  naively-expected `offset + width` a filled shape would give. This isn't new behavior from this
  change (the very same halving is why the pre-existing, unrelated
  `RoundedSolidOutline_ExpandsTheBorderRadiusWithItsOffsetAndWidth` test uses `offset + width/2`
  reach); it just newly applies to a wrapped inline's *interior* lines, which previously never took
  that code path at all.
- **The "large negative `outline-offset` paints nothing" wrapped-inline test could only ever have
  passed by accident.** Before this fix, an interior line had zero adjustable inline-axis edges to
  clamp against (`OutlineDrawHandler.ClampReach`'s `adjustableEdges == 0` branch returns the raw,
  unclamped reach), so a large negative offset could inflate a small line's rect into a
  negative-width/height rectangle with nothing to stop it, and `BoxEdgesDrawHandler.DrawBoxEdges`'s own
  `{Width: > 0, Height: > 0}` guard silently skipped painting. Once every line always has two
  adjustable edges per axis, `ClampReach`'s existing floor (`Math.Max(reach, (2*width - size) /
  adjustableEdges)`) guarantees a minimum `2 * outline-width` outer size on *every* line, so this can no
  longer degenerate to "paints nothing" — confirmed by rendering the exact fixture from the old test and
  observing three real (`8pt × 8pt` at `outline-width: 4pt`) rings instead of an empty log.

## What was deliberately not done

Scoped to `horizontal-tb` (and `sideways-rl`/`sideways-lr`, whose line boxes are laid out via the same
horizontal path — see `IsVerticalDecorationGeometry`) — a `vertical-rl`/`vertical-lr` wrapped inline's
outline is untouched, since that axis's own border/padding inset isn't even reserved yet
(`.claude/accepted-gaps/no-vertical-writing-mode-layout.md`'s #769 entry, amended with a residual note
rather than a new gap file). No change to page/column-break handling: `HasTopEdge`/`HasBottomEdge` are
read unmodified everywhere in this change, confirmed by a new
`OutlineOnABlockSpanningAPageBreak_StaysOpenAtTheBreak` test (mirroring
`BoxDecorationBreakPaintIntegrationTests`' own
`Slice_InlineSpanningAPageBreak_DrawsNoBorderAtThePageBreakEither`, but for a block box's own outline).

## Evidence

`dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0` passes in full (12343 passed, 9
pre-existing platform-gated skips, 0 failures). New/updated tests in `OutlineStylePaintIntegrationTests.cs`
cover: every line of a plain wrapped outline closing into its own ring; the same for a rounded one; a
combined border+outline fixture proving border's own open-edge geometry is unaffected by the outline
fix living in the same paint loop; the large-negative-offset clamp floor now applying per line; and the
page-break case staying open. `dotnet build PeachPDF.slnx -t:Rebuild` is clean (zero warnings).
