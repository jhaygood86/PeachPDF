# A block's final line now hangs its trailing space (issue #1014)

[css-text-3 §4.1.2](https://www.w3.org/TR/css-text-3/#white-space-phase-2) hangs the white space that
ends a line, so it does not contribute to the line's width. `CssBox.GetMinMaxSumWords` applied this at
a `<br>` only, so a block whose own final line ended in a collapsible space measured — and drew every
shrink-to-fit box around it — one space wider than the glyphs it holds.

## The load-bearing idea

The intrinsic walk carries **one** running `maxSum` across a whole subtree, so a given box's last word
is not the *line's* last word whenever a sibling's content follows it there. That is why the rule
cannot be applied where a box's words run out, and why the accepted-gap note this replaces concluded
the walk "is not structured to answer" where the line ends.

It already answers it in one place — the `<br>` branch — and the missing half is that **the walk has a
second, unnamed line terminator: the block-boundary epilogue.** A box that reset `maxSum` at the top of
its own call (`StartsNewLine`) is closing that line when the call returns. Three cases never reach that
epilogue at all, because `StartsNewLine` excludes them — a `display: table-cell` (the table engine
measures each cell with its own top-level call), a `white-space: nowrap` block, and an inline box
measured directly — and `GetMinMaxWidth` covers all three with one subtraction after the walk, a no-op
whenever the walk already applied the rule since both application points zero `trailingSpace`.

**What makes the subtraction safe is not what it first looks like.** The tempting justification —
"`maxSum` at the epilogue is this box's last line only" — is false, and the review pass falsified it:
a `white-space: nowrap` child does not reset, so `<div>AB CD</div><div>EF</div>` under `nowrap` sums
both lines into one `maxSum` (46.1836pt where Chromium says 32.9883). That is a separate, pre-existing
defect and it is *not* what the subtraction depends on. The real invariant is narrower and holds
regardless: **`trailingSpace` is non-zero only while the most recently measured word is still the tail
of `maxSum`.** Every path that puts something else on the line after that word zeroes it; the `<br>`
branch and the block reset zero it when the line ends. So the amount taken off is always currently in
`maxSum`.

Two paths did *not* uphold that invariant and had to be fixed with it — both found by the review pass,
by measuring rather than by reading: `maxSum += rowMax` (a flex row's items come back from their own
top-level `GetMinMaxWidth` as one number, not as words) and the explicit-width fold
(`Math.Max(maxSum, maxSumBeforeChild + explicitContentWidth)`, which replaces the measured tail with a
declared length whenever it raises the total). Both reach a line that has a hanging space on it only
under `white-space: nowrap`, which is why the first round missed them: `nowrap` is what stops the walk
treating an inline-level box as opening a line of its own.

Deliberately **not** zeroed: an empty inline child's own horizontal margins (`maxSum += childBox
.ActualMarginLeft + …`). Chromium drops the space there too — `AB <span style='margin-left:20pt'>
</span>` measures 33.1875pt, the same as with no space — so hanging it is the matching behaviour.

Deliberately **not** done: subtracting at the top-of-call reset, where a box's start closes the
preceding line. It is redundant in the ordinary block-sibling case (the preceding block's own epilogue
already hung its space, leaving `trailingSpace` at 0), and actively wrong for an `inline-block`, which
`StartsNewLine` also selects but which does *not* end the line before it.

## The half that was not in the intrinsic walk at all

Fixing `GetMinMaxSumWords` alone leaves the issue's own headline repro — a `float: left` around
`AB ` — still measuring 19.7930pt. `CssLayoutEngine.GetBoxWidth` has its own, independent
`width = box.Words.Sum(x => x.FullWidth)`, and `FullWidth` carries each word's trailing inter-word
gap. `GetFitContentWidth` folds that through `GetLargestChildWidth` into every shrink-to-fit box's
size, so the phantom space came back in by a second route. Same rule, same fix: the last word's gap
is the one that ends the box's content, and it hangs.

This was found by measuring, not by reading — `fit` came back 19.7930 for `AB <br>CD` while
`max-content` was a correct 13.1953, which is only possible if something other than the walk is
deciding the width. Reverting just this half afterwards confirmed it: `ABlocksFinalLine_HangsItsTrailingSpace`
fails with the walk fixed and `GetBoxWidth` untouched.

## Evidence

**Chromium agreement on all ten fixtures**, driven through the repo's existing Playwright dependency
(`page.Locator("#f").BoundingBoxAsync()` on a `float: left` at `font: 16px monospace`, one space =
6.5977pt). PeachPDF now matches Chromium 148 exactly on every one:

| fixture | before | after | Chromium |
|---|---|---|---|
| `AB ` | 19.7930 | **13.1953** | 13.1953 |
| `AB` | 13.1953 | 13.1953 | 13.1953 |
| `AB&nbsp;` | 19.7930 | 19.7930 | 19.7930 |
| `<span>AB </span><span>CD</span>` | 32.9883 | 32.9883 | 32.9883 |
| `<span>AB </span><span>CD </span>` | 39.5859 | **32.9883** | 32.9883 |
| `AB <br>CD` | 19.7930 | **13.1953** | 13.1953 |
| `<div>AB </div><div>CD</div>` | 19.7930 | **13.1953** | 13.1953 |
| `<div>AB CD</div><div>E </div>` | 32.9883 | 32.9883 | 32.9883 |
| `white-space: pre` / `pre-wrap`, `AB ` | 19.7930 | 19.7930 | 19.7930 |

The three unchanged-but-asserted rows are the ones that say the fix did not over-reach: a space
*between* two inlines is a real inter-word gap, `&nbsp;` never hangs, and a preserved space is its own
word (its `ActualWordSpacing` is 0, so nothing here can take it back off).

The two invariant-upholding fixes were measured against Chromium the same way, under `nowrap`:

| fixture | first round | corrected | Chromium |
|---|---|---|---|
| `AB <span style='display:inline-flex'>CD</span>` | 26.3906 | **32.9883** | 32.9883 |
| `AB <span style='display:inline-block;width:50pt'></span>` | 63.1953 | **69.7930** | 69.7852 |
| `AB <span style='display:inline-table;width:50pt'></span>` | 63.1953 | **69.7930** | 69.7852 |
| `AB <span style='margin-left:20pt'></span>` | 33.1953 | 33.1953 | 33.1875 |

(The 0.0078pt deltas are Chromium's own sub-pixel rounding of the declared 50pt/20pt, not a rule
difference — the no-space variants carry exactly the same offset.)

- Full suite 10 844 passed / 0 failed (net8.0); `dotnet build PeachPDF.slnx -t:Rebuild` 0 warnings.
- 100% line **and branch** coverage on every changed executable line, including both arms of the
  explicit-width guard.
- 11 new tests in `IntrinsicWidthWalkTests`; 3 confirmed failing against unmodified `main` and 3 more
  confirmed failing against the first round of this fix, before being kept. The other 5 are the
  over-subtraction guards above, which must pass either way.
- **All 113 showcases rasterized through PDFium and pixel-diffed page by page** against a baseline
  built from stashed code: **zero** pages differ. Byte-comparing the PDFs is useless here — all 113
  differ on embedded timestamps alone — so the diff has to be at the raster level.

## Found along the way, left out of scope

**A `white-space: nowrap` block does not start a new line in the intrinsic walk.** `StartsNewLine`
excludes any box with `white-space: nowrap`, so two block-level siblings under an inherited `nowrap`
have their lines summed instead of competing: `<div>AB CD</div><div>EF</div>` measures 46.1836pt
where Chromium measures 32.9883pt. Confirmed pre-existing (identical on unmodified `main`) and
untouched here — this change strictly improves it, since the second block's trailing space is now
hung rather than added on top. Filed as #1017 and recorded in
[.claude/accepted-gaps/nowrap-block-does-not-start-a-new-line-in-the-intrinsic-walk.md](../accepted-gaps/nowrap-block-does-not-start-a-new-line-in-the-intrinsic-walk.md),
which carries the measurements showing that deleting the `nowrap` clause on its own makes two
inline-level cases worse — `StartsNewLine` is wrong in a second way that the clause masks.

## The trap to know

Do not "simplify" this by hanging `trailingSpace` where a box's word loop ends. It looks like the
same rule one level down and it is not: `<span>AB </span><span>CD</span>` then measures 26.3906
instead of 32.9883, a space short of what it draws, and
`ATrailingSpaceBetweenTwoInlines_IsStillAnOrdinaryInterWordGap` is the test that says so. The rule is
about *lines*, and only two points in this walk know where one ended. Recorded as
[.claude/invariants/intrinsic-a-trailing-space-may-only-hang-where-a-line-is-known-to-end.md](../invariants/intrinsic-a-trailing-space-may-only-hang-where-a-line-is-known-to-end.md).
