# `StartsNewLine` asks whether the box is block-level, not what its `white-space` is

Issue #1017, and the gap file
`.claude/accepted-gaps/nowrap-block-does-not-start-a-new-line-in-the-intrinsic-walk.md`, now deleted.

`CssBox.StartsNewLine` — the predicate `GetMinMaxSumWords` resets its running line total on — used to
read:

```csharp
box.DerivedStyle.ActualDisplay != Keywords.Inline
&& box.DerivedStyle.ActualDisplay != Keywords.TableCell
&& box.WhiteSpace.Value != Whitespace.NoWrap;
```

It now reads the display alone:

```csharp
box.DerivedStyle.ActualDisplay is not (Keywords.Inline or Keywords.InlineBlock
    or Keywords.InlineTable or Keywords.InlineFlex or Keywords.InlineGrid
    or Keywords.TableCell);
```

`white-space` says whether a box's content wraps *within* a line; it says nothing about whether the
box begins one. A block-level box always does ([CSS 2.1
§9.4.1](https://www.w3.org/TR/CSS21/visuren.html#block-formatting)). The load-bearing idea is that the
predicate had **two** wrong answers, and each partly hid the other: it said a `nowrap` block-level box
does not open a line (so its line was ADDED to the previous sibling's, growing linearly with the
sibling count), and it said every inline-level box does (so an inherited `nowrap` was the only thing
putting an `inline-block`/`-flex`/`-table`/`-grid` back on the line it actually shares). Fixing either
alone makes measurable cases worse — the gap file recorded that, measured, before this change existed.

Float widths at `font: 16px monospace` (one character advances 6.5977pt), Chromium 148 for reference:

| markup | before | after | Chromium |
|---|---|---|---|
| `<div>AB CD</div><div style="white-space:nowrap">EF</div>` | 46.1836 | 32.9883 | 32.9883 |
| …plus a third `<div>GH</div>` sibling, all under `nowrap` | 59.3789 | 32.9883 | 32.9883 |
| two `border-left: 10pt solid` siblings under `nowrap` | 56.1836 | 42.9883 | 42.7383¹ |
| `AB <span style="display:inline-flex">CD</span>` | 19.7930 | 32.9883 | 32.9883 |
| `AB <span style="display:inline-block;width:50pt">` | 50.0000 | 69.7930 | 69.7852 |

¹ Chromium rounds a border width down to a whole device pixel (10pt = 13.333px → 13px = 9.75pt); the
0.25pt is that rounding, not a measurement disagreement.

## What running it turned up

- **The predicate-only fix does not regress an inline-level box that follows a block-level sibling,
  and the reason is the parser, not this walk.** `<div>AB CD</div><span style="display:inline-block">EF</span>`
  would be 46.18 if the walk simply summed, because a flat walk has no notion of the anonymous block
  boundary that CSS 2.1 §9.2.1.1 puts there. It measures 32.99 because `DomParser` really does wrap the
  trailing inline content in an anonymous block, which *is* block-level and does open the line.
  Measured before assuming it — this was the one shape that looked like it could break.
  `AnInlineLevelBoxAfterABlockSibling_StillGetsItsOwnLine` pins it, precisely because nothing else
  would notice if anonymous-block generation changed.
- **`ActualDisplay` is NOT a sufficient oracle for "is this box block-level", and assuming it was
  is what the review pass caught.** It applies CSS 2.1 §9.7 for a float and
  `DomParser.BlockifyPositionedBox` handles `position: absolute`/`fixed` — but
  `NormalizeFlexOrGridItem` deliberately blockifies only the *table-internal* set, leaving an
  inline-level flex/grid item's computed display alone (see
  [.claude/accepted-gaps/inline-level-flex-and-grid-items-are-not-blockified.md](../accepted-gaps/inline-level-flex-and-grid-items-are-not-blockified.md),
  issue #1003 — a deviation that file described as invisible to layout, and this change is what made
  it visible). So the first version of this fix read a single-line flex COLUMN of `inline-block` items
  as summing onto one line: 72.5742pt against the 39.5859pt the same container gives for plain block
  items. `StartsNewLine` now asks `IsFlexOrGridItem` first, which reads the PARENT's display, because
  css-display-3 §2.7 blockifies an item whatever its own computed value says.
  `AFlexOrGridItem_StartsItsOwnLine_WhateverItsOwnDisplayIs` pins all four inline-level displays plus
  bare `inline`, against a block-item control.
- **That fix also corrected a case that was already wrong.** A *bare* `display: inline` item in a flex
  column measured 72.5742pt on the merge base too — it is an anonymous flex item and is blockified the
  same way — and now measures 39.5859pt with the rest.
- **The whole 115-showcase corpus is unchanged.** Generated before and after; every object and every
  stream of all 115 PDFs matches once run-to-run randomness is normalized away.
  **Normalizing that randomness is the whole difficulty, and there are four independent sources of
  it** — each of which produced a false positive on the way to this result, so a future sweep should
  start from the list rather than rediscover it: the randomized 6-letter **font subset tag**
  (`/JVGELM+Arial`), the **creation date** in both PDF-Info (`D:2026...`) and XMP (ISO-8601) form,
  per-annotation **`/NM` GUIDs** on every link, and the document **`/ID`**. The subset tag alone made
  two files (`marker_styling`, `svg_vertical_text`) differ under MuPDF rasterization while PDFium
  rendered them byte-identically; the GUIDs alone made nine files differ at object level while
  PDFium found zero differing pages across all of their 23 pages. So a MuPDF-only raster sweep of
  this corpus has a false-positive floor, and so does a naive object diff: compare normalized
  objects AND cross-check the candidates with PDFium.
- **No showcase exercises the fixed shape at all**, which is why the corpus being unchanged is
  evidence of no regression rather than evidence of no fix. The fixtures carry the fix.

## Deliberately not done

A **float** inside a `white-space: nowrap` shrink-to-fit box loses its contribution to the line:
`<div style="float:left; white-space:nowrap">XY <span style="float:left">ZZZZ</span></div>` measured
46.1836pt (Chromium's value) before and measures 26.3906pt now. That is a genuine **regression from this
change**, and it was right by accident — the old predicate excused any `nowrap` box from opening a line,
so the inner float was summed onto the line for a reason that had nothing to do with floats. Without
`nowrap` the same shape measured 26.3906pt before this change too, so the defect class is pre-existing
and the `nowrap` path has simply joined it. Not fixed here because "a float adds to the line" is not
unconditionally true (it does not, between two block-level siblings) and the flat walk cannot tell which
formatting context a child is in. Tracked as #1033 and recorded in
[.claude/accepted-gaps/a-float-does-not-contribute-to-the-line-in-the-intrinsic-walk.md](../accepted-gaps/a-float-does-not-contribute-to-the-line-in-the-intrinsic-walk.md).

An atomic inline-level box whose content is block-level is still measured by the flat walk rather than
in isolation, so `AB <span style="display:inline-block"><div>CD</div></span>` measures 19.7930 where
the two parts add to 32.9883. Unchanged by this fix (same number before and after) and a different
defect — the block-level *child's* reset is what discards the outer line, not the atomic inline's own
boundary. `inline-flex` already gets that same markup right, because `IsFlexRow` measures an item via
its own top-level `GetMinMaxWidth`, so the fix is to apply that existing mechanism rather than build
one. Tracked as #1032 and recorded in
[.claude/accepted-gaps/atomic-inline-with-block-content-is-not-measured-in-isolation.md](../accepted-gaps/atomic-inline-with-block-content-is-not-measured-in-isolation.md).

## Evidence

`IntrinsicWidthWalkTests`, 30 fixtures. Seven are new: a `nowrap` block sibling, three of them, the
per-line border scoping, an `inline-flex` child without `nowrap`, an inline-level child's explicit
width over `inline-block`/`-table`/`-grid`, the anonymous-block contrast case, and the flex/grid-item
theory over all four inline-level displays plus bare `inline`. Non-`nowrap` halves were added to the
two existing fixtures that could previously only be reached under `nowrap`.

Nine fail against the merge base. Note what the flex/grid-item theory does and does not prove: only its
`inline` case fails against the merge base, because the OLD predicate already had the four atomic-inline
displays opening a line (for the wrong reason). It guards against the regression the review pass caught
in the first version of this fix, which is a regression relative to the fix-in-progress rather than to
`main` — that is exactly why it had to be written from the measurement rather than from the diff.

Full suite green on net8.0 (11,071 passed / 0 failed / 9 skipped), solution rebuild with 0 warnings,
100% diff coverage on the changed production lines (148,915 and 138,128 hits).
