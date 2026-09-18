# The line box, not the word, is the fragmentainer-membership unit

Closes issue #1054 (negative leading only overflowed a line box downward, never upward) and is the
foundational PR a larger batch of fixes depends on. Landed as three sequenced commits, each verified
before moving to the next.

## The load-bearing idea

`Fragmentation.FragmentEmitter.BuildDraft` used to ask `ClaimsWord` once per word, using that word's
own rectangle. It now groups a box's words by the `CssLineBox` that owns them (a new `CssRect.Line`
back-reference, set in `CssLineBox.ReportExistanceOf`) and asks one `ClaimsLine` verdict per line,
reusing that box's own per-line rectangle (`box.Rectangles[line]`) — the same rectangle already
computed a few lines above for the box's decoration-rectangle list. A word with no owning line (in
practice, only an outside `::marker`'s own phantom word, positioned directly by
`CssBoxMarker.PerformLayoutImp` rather than flowed onto a line) falls back to the old per-word
`ClaimsWord` path, kept verbatim under a new name.

With every word on a line sharing one fragmentainer verdict, the two floors that existed solely to
keep negative-leading ink from crossing a fragmentainer boundary — `HalfLeadingOffsetOf`'s
`Math.Max(0, …)` at flow time, and `ApplyVerticalAlignment`'s matching `escape` push-down after the
line closes — have nothing left to protect and are deleted. CSS 2.1 §10.8.1 now applies exactly:
negative leading overflows a line box on both sides.

## What was found by running it, not by reading it

- **Stage 2 (grouping words by line, still calling the same `ClaimsWord` test) was provably inert**:
  the full suite (12,314 tests) passed unchanged before touching the floor at all. This is expected —
  for any line without negative leading, a box's line rectangle and the union of its words'
  rectangles agree exactly.
- **Three tests failed after removing the floor, and none of them was a real regression** — each was
  quietly relying on the floor for its own expected numbers:
  - `BaselineAlignmentLayoutIntegrationTests.ALineHeightShorterThanTheFont_KeepsItsInkInsideItsLineBox`
    stated the old floored behavior by design (per its own docstring) and was inverted.
  - `LineHeightNormal_LeavesItsInkWhereTheFlowPutIt` failed by ~0.1pt. `line-height: normal` resolves
    from `RFont.NormalLineHeight` (typo/hhea ascent+descent+line-gap), a metric derived independently
    of `RFont.Height` (ascent+descent) that the glyph content area itself uses — so Arial's own
    "normal" leading is a small negative fraction of a point, not exactly zero, and the floor was
    masking it. Re-asserted against the actual half-leading formula instead of an assumed zero.
  - `FlexReplacedElementPageBreakIntegrationTests.ItemAtDefaultAlignment_TextStraddlingPageBoundary_MovesToTheNextPage`
    failed because its `font-size:16pt;line-height:16pt` fixture is *also* negative leading for Arial.
    Traced with the fixture's own real numbers (font height 21.28pt, ascent 17pt) rather than assumed
    ones: the line still correctly relocates to the next page (unchanged), but its ink now escapes the
    negative half-leading above that page's content top too, landing ~2.6pt earlier than the old
    floored assertion expected. Re-asserted against the computed half-leading.
- **A suspected duplicate-claim risk at the exact page-boundary case turned out not to exist.** Before
  writing `LineBoxFragmentMembershipIntegrationTests`, hand-tracing `ClaimsLine`'s region-overlap test
  suggested a line resumed onto a new page, whose escaped ink numerically falls back into the
  *previous* page's raw Y-range, could be claimed by both pages. A diagnostic test proved this wrong:
  the discarded first attempt's words are marked `AwaitsTheNextFragmentainer`, which `BuildDraft`
  already skips unconditionally for any slot before the word is finally placed — so the previous
  page's own walk never considers it a candidate at all, regardless of where the geometry test would
  have put it. Confirmed empirically (`pages=1`, all three words claimed by slot 1 only) rather than
  trusted from the trace.
- **The plan's own open design question — whether the emitter's monolithic tie-break
  (`FallsPast`/`MonolithicContent.FitsNoFragmentainer`) should be judged per box or aggregated across
  every box sharing a physical line — resolves to per box by construction**, not by a separate
  decision: `ClaimsLine` is called from inside one box's own `BuildDraft` frame, using that box's own
  `Rectangles[line]`, and never sees a sibling box's geometry. `AnOversizedReplacedElementNearAPageBoundary_DoesNotStrandTheOrdinaryTextBesideIt`
  pins this: an image taller than a whole page shares a line with ordinary text, the image is clipped
  to its first page only (issue #484's existing rule), and the text beside it is claimed normally
  rather than being dragged into the image's "stays exactly where it is" verdict.

## Deliberately not done

- Issue #1048 (the broader "line/word/nested-fragment geometry" tracking issue) is **not** claimed
  closed by this change — it covers vertical-writing-mode float exclusion, table/multicolumn nested
  capture, and cross-platform font-metric coverage that this PR does not touch.
- Issue #1047 (a `break-inside: avoid` relocation racing a stale-slot rebuild) is unrelated and left
  for a separate PR, per the plan.

## Evidence

- `dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0` — 12,319 passed, 0 failed, 9
  skipped (pre-existing platform skips), both after stage 2 (inert) and after stage 3 (the real fix).
- `diff-cover` against `main`: **100%** on the 22 changed/added lines across
  `CssRect.cs`/`CssLineBox.cs`/`FragmentEmitter.cs`/`CssLayoutEngine.cs`.
- `dotnet build PeachPDF.slnx -t:Rebuild` — 0 warnings.
- Rasterized a negative-leading line (`line-height: 8pt` on a 24pt font, with a background so the
  declared line box and the actual ink are visually distinguishable) placed at the very top of a page,
  through both PDFium and MuPDF: both agree, the text is not clipped, and it visibly extends above and
  below the thin background band.
