# `text-decoration-style: double` draws two strokes

`TextDecorationStyleMapper` maps every style to an `RDashStyle`, and `double` has no dash pattern that
could express it, so it resolved to `RDashStyle.Solid` and painted one line — output identical to
`solid`. `docs/html-css-support.md` listed all five styles as supported without qualification, so this
was a documented capability that silently did nothing.

Found while comparing report-shaped CSS against Chrome: the accounting convention of a double rule
under a grand total came out as a single line, with nothing distinguishing the total from the subtotal
above it.

## The load-bearing idea

`double` is not a pen — it is two strokes of the pen the other styles use. So the mapper is left alone
(it still supplies the pen each stroke is drawn with) and the second `DrawLine` is added at the point
the segment is stroked, `FragmentPainter.StrokeDecorationSegment`. That keeps it downstream of
`DecorationSegments.Subtract`, so a double line breaks around an atomic inline and skips glyph ink the
same way a single one does, with no second code path to keep in step.

## Which way the pair grows — measured, not reasoned

The first draft argued this from each line's "anchoring edge" and got `line-through` wrong. Chrome 141,
rasterized at 300dpi, does something simpler and uniform: it keeps the first stroke exactly where the
single stroke sits and adds the second **below** for an underline *and* a line-through, and **above**
for an overline. A line-through does not straddle.

| 16px font | solid | double |
| --- | --- | --- |
| underline | rows 172–174 | 172–174 + 181–183 |
| overline | 122–124 | **112–115** + 122–124 |
| line-through | 153–155 | 153–155 + **159–161** |

The underline direction also happens to be the only one its own anchor allows — its top edge is what
`ResolveAutomaticUnderlineCenterOffset` keeps below the alphabetic baseline, so growing upward would push
the second stroke back through the glyphs — but that argument does not decide line-through, and inventing
one for it produced a straddle no browser draws. Measure first.

## Geometry — since closed by issue #1121

This originally drew two strokes at the full resolved thickness with a gap of the same thickness (a 3×
total), a deliberate deviation from css-backgrounds-3's `border-style: double` ("the sum of the two
lines and the space between them equals" the total) reasoned about at the time: dividing the initial
1px thickness three ways would have rendered two ⅓px hairlines, fainter than the `solid` underline
`double` is meant to be a heavier version of.

Issue #1121 closed this properly instead of leaving it as an accepted gap: each stroke is now
`Max(thickness / 3, one CSS pixel)` (`FragmentPainter.DoubleStrokeWidth`) — spec-literal (an exact
third, total sums back to the declared thickness) once a third is wide enough to stay visible, and only
wider than that below the floor. No accepted-gap file remains for it.

## The upward-growing overline used to leave the page — since closed by issue #1124

A double overline flush against the top of a page has no room above the text, so its upper stroke used
to fall outside the page clip: on a Letter page with zero margins the clip was y 0..792 and the strokes
were emitted at **794.0** and **792.0**. Moving the pair at paint time to force a fit was ruled out at
the time, since it would put an overline somewhere it was not asked to be, silently, on the documents
where an author can see it least.

Issue #1124 closed this from the other direction instead: layout now reserves the extra ascent-side
headroom a `double` overline needs (`CssLayoutEngine.LineBoxContributionOf`, via
`FragmentPainter.DoubleOverlineExtraReachAbove`), so ordinary pagination leaves room for both strokes
without ever needing to reposition anything after the fact. No accepted-gap file remains for it.

The transferable part: **`TestRecordingGraphics` never applies clipping**, so every draw-call test in
this area is blind to a stroke that leaves the page. `TextDecorationDoublePdfClipTests` generates a real
PDF and reads the stroke coordinates back out of the content stream against the page's own clip path —
which is a `W* n` path, not a `re W n` rectangle, and needs its bounds taken from the preceding
moveto/lineto points.

## Deliberately not done

- **`wavy` still paints solid** — issue #1114, with
  `.claude/accepted-gaps/text-decoration-style-wavy-paints-solid.md` and a corrected `docs/**` row. It
  needs a stroked `RGraphicsPath`, which has a coordinate-space question (`PixelsPerPoint`-divided path
  against a layout-space pen width) that `double` does not.
- **SVG text decorations are unchanged** and remain a single stroke for every style. They share the
  mapper but not the painter; noted in both the mapper's comment and the docs row.

## Evidence

- `TextDecorationDoubleStyleTests`, 8 cases. Reverting `FragmentPainter.Decorations.cs` to `origin/main`
  fails the 4 `double` cases and leaves the 4 other-style cases passing — the contrast that catches a
  change which simply doubled every decoration.
- `TextDecorationDoublePdfClipTests`, 3 cases, against a real generated PDF: both strokes inside the page
  clip with headroom, the upper one outside it without, and a double *underline* unaffected either way as
  the control that keeps it about the overline's direction rather than about doubling.
- Full suite on net8.0, run twice: 11,872 passed, 1 failed, that one being
  `AnonymousBoxDefaultingTests.AListItemCostsLittleMoreThanAPlainBlock`, which fails identically on
  clean `main`.
- Diff coverage 100%.

## Ink measurement is flaky, in two places, and neither is this change

Twice during this work an ink-skipping assertion failed and could not be reproduced:

- locally, `GraphicsAdapterInkCrossingsTests.EachDescenderInARun_ReportsInkAtItsOwnPenPosition`, only
  under `--collect:"XPlat Code Coverage"` — "both descenders should be found, got 1";
- in CI, `TextDecorationSkipInkTests.Underline_All_SkipsTheSameWayAuto_Does` on **net11.0/Windows only**,
  with net8.0 and net10.0 green on the same runner and the same fonts. It renders `skip-ink: auto` and
  `skip-ink: all` as two separate documents and compares the segment counts; `auto` found two descenders
  and `all` found one.

Both are the same symptom — a descender's ink not found — and neither can be reached from here.
`StrokeDecorationSegment` runs *after* `DecorationSegments.Subtract` has decided how many segments there
are, and for every style but `double` it makes the identical `DrawLine` call the old code made, so the
stroke count is exactly the segment count either way. `SkipsInk` treats `auto` and `all` identically, so
nothing in this change could make those two documents disagree with each other in particular. A
same-base branch touching the same painter (the underline-baseline work) went green on net11/Windows.

The likely cause is the process-wide `FontFactory` cache CLAUDE.md warns about: two documents rendered
in the same process can resolve the same family to different cached state depending on what else is
running in parallel, and ink measurement is the one thing that reads real outlines. Worth a dedicated
look — a test that is 11/12 reliable is a test whose failures get explained away.
