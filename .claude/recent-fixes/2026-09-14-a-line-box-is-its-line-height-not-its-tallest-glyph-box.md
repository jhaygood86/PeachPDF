# A line box is its `line-height`, not its tallest glyph box

Found by rendering Acid2 against its reference image and Chrome (which still renders the reference
face pixel-for-pixel, 624 differing pixels out of 28,224 — antialiasing on the eyes) rather than by
reading code. Two of the face's own rows were the wrong height, and the resulting 1.8pt drift left a
white seam across the middle of the face where the absolutely-positioned `.eyes` (unaffected, `top:
5em`) no longer met the flow content below it.

## The load-bearing idea

CSS 2.1 [§10.8](https://www.w3.org/TR/CSS21/visudet.html#line-height): a non-replaced inline box
contributes **exactly its own `line-height`** to the line box. The font's ascent+descent — the
*content area*, which is what the box's background and border are painted from — is a separate
quantity, and when `line-height` is the smaller of the two the leading is negative and the glyphs
overflow the line on purpose. §10.8.1 adds the **strut**: every line box that holds content is also
at least as tall as an imaginary inline box carrying the *block's* own font and `line-height`.

`CssLayoutEngine` had neither. A word's rect is `ActualFont.Height` (right — that *is* the content
area, and `CssLineBox.UpdateRectangle` builds an inline box's decoration rectangle from it), and the
line's extent was `Math.Max` over those word rects, with `ActualLineHeight` only ever allowed to
*grow* the result. So the line's height was `max(line-height, font height)` and a declared
`line-height` below the font's own was silently discarded. The one place `ActualLineHeight` was read
took it from `box` — the innermost inline being flowed — never from `blockBox`, so the strut did not
exist either.

The rule now lives in one place, `CssLayoutEngine.LineBoxExtentOf(box, blockBox)`: the largest
`line-height` among the block's strut and *every* inline box the text sits inside — `box` and its
inline ancestors up to `blockBox`, which is exactly the set of inline boxes on the line, since
`FlowBox` only ever recurses through inline boxes. Reading just the two ends of that chain leaves a
`line-height` declared on an intermediate `<span>` inert whenever the span holds no direct text of
its own. `MaxBottom` is raised by `word.Bottom` **only for a replaced word** (`CssRect.IsImage`: an
image, an inline `<svg>`, MathML, a form control, a vector list-marker glyph), which is the case
§10.8 does size from the element's own box; a text word no longer grows the line past its
`line-height`.

The extent is applied where a word is placed, not where a line is created, because §9.4.2 keeps a
line box holding no content at zero height — seeding it at line creation gives an empty block a
strut's worth of height out of nothing (`EmptyBlock_GetsNoStrut_AndStaysZeroHeight` pins this).

`CreateVerticalLineBoxes` — the separate line-layout engine for vertical writing modes — took a
column's cross-axis thickness straight from the word's glyph footprint and never consulted
`line-height` at all, so neither half of this applied there and the same document laid out under two
different line-box models depending on `writing-mode`. It now calls the same `LineBoxExtentOf`, with
the same `IsImage` carve-out, so the two engines cannot drift.

`DerivedStyle.ActualLineHeight` was already correct and is untouched — see
[`2026-09-08-line-height-normal-font-metrics.md`](2026-09-08-line-height-normal-font-metrics.md),
which resolved the `normal` keyword from real font metrics. This was purely a consumer-side bug.

## Fixing layout exposed a paint bug underneath it

The layout fix alone made every box's *height* right and the rendering still wrong: the showcase's
shaded one-line-box-tall bands measured 26.7pt where Chrome says 18pt. `FragmentEmitter.ExtentOf`'s
`BoundsEndAtItsContent` arm grows a fragment's bottom to cover whatever its content actually reached,
and a block's text lives in an **anonymous inline child** whose own rect is the glyph content area —
so the block's painted border box was re-grown right back to the glyph ink, undoing the fix at paint
time. This was invisible before, because a box's bounds had always already covered its glyphs.

`ExtentOf` now skips **inline-level** in-flow children when extending: §10.6.3 sizes a block from the
line boxes its inline content produces, and the box's own bounds already record those. The skip is
*not* applied to a captured instance continuing through a nested fragmentainer
(`Draft.BoundsStatedByACapturedContinuation`, new) — its bounds have not had a height applied yet, so
it has no line boxes to fall back on and its inline content is all the fragment can be measured from.

**That distinction was found by running it, not by reasoning.** The first attempt skipped inline
children unconditionally and broke 11 tests, every one of them a multi-column/`box-decoration-break`
case (`FragmentEmitterTests.ABoxSplitAcrossColumns_*`,
`BoxDecorationBreakLayoutIntegrationTests.*AtAColumnBoundary*`,
`StraddlingListMarkerTests.AnItemCrossingAColumnBoundary_*`) — i.e. exactly the captured-continuation
arm. Gating on which of `BoundsEndAtItsContent`'s two reasons applies cleared all 11 with no other
change. The issue-#569 page-grid arm this leaves alone is about a flex/grid item whose content is
*block-level* children, so it is untouched either way — see
[`2026-07-31-a-flex-items-pinned-bounds-no-longer-cut-off-its-own-decoration.md`](2026-07-31-a-flex-items-pinned-bounds-no-longer-cut-off-its-own-decoration.md).

## The review pass caught a regression the 11,000-test suite did not

The first version applied the extent **once per word, at the top of the loop iteration** — i.e.
*before* the wrap decision that can move that same word onto a new line. A word that wrapped landed on
a line the growth had never seen, and only the *next* word on that line re-applied it. If there was no
next word, the line contributed no height at all: **every block whose last line holds exactly one
word came out one whole line-height short**, at default `line-height: normal` as much as at a tight
one. `<div style="width:40px">aaaa bb</div>` measured 10.5pt instead of 21pt.

The suite did not catch it and neither did the rasterized showcase, because the multi-line samples all
happened to end with two words on the last line. Worse, the new test written *for this fix*
(`ShortLineHeight_StacksEveryLineByThatHeight_NotByTheFontsHeight`) used `"aaa bbb ccc ddd eee fff"` —
whose last line is `eee|fff` — so it passed by a coincidence of fixture text. Dropping `fff` makes the
unfixed code fail it.

The fix is to call the growth at **two** points per word, which the helper's own comment states:
before the wrap decision, because `DomUtils.GetLastLeft/RightIntersectingFloatBox` and the wrap itself
read `MaxBottom` as the bottom of the line being *closed*; and again right after `word.Top` is
assigned, against the cursor's post-wrap `CurrentY`. Deleting either call fails tests now: the
theories carry an explicit one-word-last-line case (`"aaaa bb"`, `"aaa bbb ccc ddd eee"`) plus a
`line-height: normal` counterpart asserted against the box's own resolved line height.

**The lesson worth keeping: a fixture's text is part of its coverage.** A wrapped-text assertion that
does not control how many words land on the *last* line is not testing the last line.

## Deliberately not done

- **No half-leading, and no real baseline alignment.** §10.8.1 centres an inline box's leading
  (half above the content area, half below) around the baseline; this engine top-aligns words on a
  line and `ApplyVerticalAlignment`'s "baseline" is `max(rect.Top)`. Making the line's *height*
  correct does not require that, and reworking the alignment model is a much larger change with a
  large blast radius across the suite. A line mixing two very different font sizes is still aligned
  by top rather than by baseline — pre-existing, unchanged here.
- **`MaxBottom` is still not recomputed after `ApplyVerticalAlignment`**, so `vertical-align` still
  does not feed back into the block's height. Also pre-existing and out of scope.

## Evidence

- New `LineHeightLineBoxExtentTests` (21 tests). Confirmed against the unfixed code that the
  defect-targeting ones fail (all three `line-height` spellings, the stacking theories, the strut
  case) while the guards — taller `line-height`, a taller inline child, replaced content, the empty
  block — pass both before and after, which is what makes them guards rather than restatements of
  the fix. Removing either `GrowLineToItsExtent` call fails 4 of them.
  Every expectation is a *specified* value, and the three that need the font to be taller than the
  declared line-height first assert that it is, so they cannot pass vacuously on a machine whose
  fallback font is short.
- Cross-checked every case against headless Chrome on the same markup before writing the
  assertions, and took every expectation from it: the seven single-block cases
  (`12/12/12/14/12/24/20` CSS px), the four nested-inline-ancestor cases (`40/40/40/12`), and the
  vertical-writing-mode ones (`6/14/30/12`). The fixed engine reproduces all fifteen exactly.
- Full suite: `dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0` — 11,347 passed,
  0 failed, 9 skipped (pre-existing platform skips), and green on four consecutive runs.
  `BoxDecorationBreakPaintIntegrationTests.Clone_WrappingInline_DrawsEveryBorderEdgeOnEveryLine`
  failed once mid-development and could not be reproduced across five later full runs or in
  isolation; recorded here rather than dismissed, since this repo already warns that `FontFactory`'s
  process-wide static caches make order-dependent flakiness real.
- `dotnet build PeachPDF.slnx -t:Rebuild` — 0 warnings.
- Diff coverage 100% on the changed lines (`diff-cover` against `origin/main`).
- New `line_height_declared` showcase, rasterized with **both** PDFium and MuPDF: the painted band
  heights are `[18.3, 24.4, 36.1, 36.1, 19.4, 48.3, 30.6, 48.3]` (PDFium) and
  `[18.0, 23.5, 36.0, 35.5, 19.0, 48.0, 29.5, 48.0]` (MuPDF) against Chrome's
  `[18, 24, 36, 36, 19.2, 48, 30, 48]` — agreement within one rasterized pixel. Before the paint
  half of the fix the same measurement read `[26.7, 27.8, 36.1, 36.1, 23.3, 48.3, 30.6, 48.3]`,
  which is what caught it.
- Acid2: the rows this governs (`.forehead`, `.nose`, `.chin`) now match the reference image
  exactly, and the white seam across the face is gone. The rows that still differ are the separate
  defects tracked elsewhere — the absolutely-positioned `blockquote` not containing its float, `ul`'s
  margin not collapsing through `.parser-container`, and the missing HTML5 stray-`</p>` element —
  plus the two documented Acid2 gaps.
