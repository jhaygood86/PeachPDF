# The engine reports text it drew and then clipped away

Detection only — nothing about what is painted changes.

A glyph emitted and then truncated by the PDF clip operator is still IN the content stream, so a
reader that parses the stream finds it and reports the document complete, while a renderer that
honours the clip shows only part of the word. Both are correct; the difference is not recorded in
the file. Only the painter, at the moment it decides to draw, knows the word's rect was wider than
the clip it was drawn into — so the engine is the only thing that can report it.

## Where, and why there

`FragmentPainter.Text.cs`'s `PaintWordSequence` already computes
`var clip = g.GetClip(); clip.Intersect(wordFragment.Rect);`. The guard below it handles only the
FULL case — a zero-area intersection, word never drawn — which reading the output back already
catches as missing text. Partial clipping had no decision point at all. The check therefore sits
immediately after that guard, comparing two rects the method was already holding: no extra geometry
work on the paint path.

The collector is on `HtmlContainerInt`, not the painter, because a painter instance is per page and
the report is per render — `PageClipOverride` on the same type is the precedent in the other
direction. `PdfGenerator.AddPdfPages` drains it after the page loop, before the container is
disposed, onto the public `PeachPdfDocument.ClipReport`. Two methods lose `static`
(`PaintWordSequence`, `PaintLineWithEllipsis`) because they now need the container.

## What measuring it turned up

- **A multi-word run in a narrow box is not a clean partial clip**, and the first fixture conflated
  two findings. The later words fall entirely outside, hit the existing full-clip guard, are never
  drawn, and reading the output back reports them missing — correctly. Full-cull and partial-clip
  are different results, and the control case has to be a SINGLE word whose rect starts inside the
  box.
- **It is quiet on ordinary documents.** A plain paragraph and a plain table report nothing on
  upstream, which is what makes the rate readable. Across 26 real templates: 0 documents report any
  clipped text and none of them move — as expected for a detection-only change.
- **The tolerance (0.5pt) is a starting point, not a result.** The existing `VisibilityClipEpsilon`
  is 1e-6 and would report every word flush against a clip edge. The report carries `DrawnWidth`,
  `VisibleWidth` and `KeptFraction` precisely so the threshold can be settled by measurement.

- **The report accumulates, and had to be made to.** `AddPdfPages` is a repeatable public API —
  a caller appends pages to an existing document across several calls, each building its own
  container and its own report — so assigning `document.ClipReport` wholesale discarded every
  earlier call's findings. `PeachPdfDocument.PageCount` accumulates across those same calls via the
  underlying `PdfDocument`; a report whose whole purpose is not to lose information quietly should
  not be the one member that does.
- **A word can be reported with `VisibleWidth == DrawnWidth`.** The record fires when EITHER
  dimension was reduced past the tolerance, so a box short enough to cut a line's height but wide
  enough to keep the whole word is recorded — correctly, it is the same silent loss — with its width
  untouched. The first version of the XML doc said "always less than `DrawnWidth`", which is wrong
  for exactly that case.

## Deliberately not reported

`text-overflow: ellipsis` (truncation the author asked for and the reader can see) and whitespace
(a clipped space is not actionable). And the report UNDER-reports: it measures against `RGraphics`'s
tracked rect stack, which a `border-radius`/`clip-path` clip and the page-level
`XGraphics.IntersectClip` never reach. "Nothing reported" is weaker than "nothing was clipped", and
both the API docs and the reader-facing docs say so.

## Evidence

`ClipReportTests`, six fixtures: the single-word control (reported, with geometry), the multi-word
contrast, an ordinary document (silent), `text-overflow: ellipsis` (excluded), `KeptFraction`
bounded and below 1, and a wide-enough `overflow: hidden` box (silent — the property alone must not
report). Disabling the report site fails two of them. Full suite green on net8.0 (10,258), 0 new
build warnings.
