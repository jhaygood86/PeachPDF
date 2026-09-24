# An empty inline sizes its line (#1310)

**Symptom (#1310):** `text<span style="position: relative; font-size: 40pt"><span style="position:
absolute">…</span></span>more` sized its line from the 10pt text, so the badge's zero-width containing
block (`EmptyInlineContainingBlockFor`, #1299) was one 40pt font height tall and stuck out above the
line. It was never specific to positioned inlines: any inline that places no word was left out.

**Cause:** a line grows per word (`FlowBox`'s `GrowLineToItsExtent`, walking the word's inline
ancestors). An inline with no word in its subtree is reached by nothing.

**Fix:** after `FlowBox` recurses into a plain `display: inline` child that consumed no word ordinal
(`WordOrdinal == childStartOrdinal`, on a pass that opens it) and holds no atomic inline
(`HoldsAtomicInlineContent`), `PlaceEmptyInline` takes the half-leading extent of that inline and of its
inline ancestors up to the block, and puts it on a line by the break opportunity nearest it:
- **No opportunity since the line's last word** (`aaa<span></span> bbb`, `text<span></span>` at the end):
  it goes with that word, so `GrowLineForEmptyInlines` grows the current line now.
- **After a space, or at a line's start** (`aaa <span></span>bbb`): it comes after the opportunity and
  goes with the next word, which may wrap. So it is held in `CssLineBoxCoordinates.PendingEmptyInlineExtent`.
  The next word's `GrowLineToItsExtent` folds it in on whichever line the word lands on, and it is cleared
  once the word lands. `CreateLineBoxes` gives anything still held at the end of the flow to the last
  line, through `GrowLineForEmptyInlines` again.

`GrowLineForEmptyInlines` does nothing to a line with no content (`IsAtLineStart`), since §9.4.2 keeps that
at zero height. The shared tail of line growth moved into a static `CommitLineExtent`.

**Traps:**
- **Growing a line outside word placement bypasses fragmentation.** Every word is asked
  `WouldStraddleFragmentainer` when it is placed, against the line as it is then. A taller word placed
  later is asked itself, but an empty inline has no word to ask. So a line grown by one at the foot of a
  page ran 27pt past the page end, and nothing moved it. The first cut did exactly that, and the
  post-change review found it (`<div style="height: 770pt"></div><p>text<span 40pt></span></p>`: the line
  ended at 849.2 on an 822pt page). `GrowLineForEmptyInlines` now asks each word on the line again,
  shifted down by how far the grown line moves its baseline and then put back. If one straddles, it takes
  the same break at the line's start that a word takes. A "held for the next word" design only escaped
  this because the next word is asked after the extent is folded in, which is why the reviewer's control
  (a word after the span) was right all along.
- **Atomic inlines consume no word ordinal.** inline-flex, inline-table, inline-grid and an atomic
  inline-block are placed without one. So "took no ordinal" alone called `<span 40pt><inline-flex/></span>`
  empty and carried its extent to the next word's line, which was wrong in a new way compared with `main`.
- **Which side of the break opportunity.** The first cut held every empty inline for the next word, which
  is right after a space and wrong before one: `aaa<span 40pt></span> bbbb` put the span on `bbbb`'s line.
  LayoutNG keeps what comes before the break point on the current line. `PendingWordSeparator` is the
  signal, since it is set exactly when a space has been passed since the last word.
- **A `<br>` keeps its extent on the line it ends** and does not restore it the way a wrapping word does. So
  a held extent is cleared there, or it would also grow the line after the break.
- **The re-check needs the "fits nowhere" exemption too, measured on the line.** `WouldStraddleFragmentainer`
  exempts a word too tall for any fragmentainer by the word's own height. A line grown by
  `<span style="font-size: 1000pt"></span>` is too deep while its word is not, so without
  `FitsNoFragmentainerFromLineTop` every pass broke before the line. The resume slot moved forward each
  time, so the no-progress backstop never fired, and the run hit the pass cap and truncated everything
  after it. Found by `/code-review`. The line now overflows, as a too-tall word does (css-break-3 §2).
  A third `/code-review` found the same loop on the word path: in `<span 1000pt></span>text` the held
  extent is folded in as `text` is placed, and `FlowBox`'s own straddle check exempts only the word's
  height. So the exemption is now the shared `LineFitsNoFragmentainer`, asked on both paths. On the word
  path it only changes a line that would otherwise break forever.
- **A line too deep for any fragmentainer must step the cursor over, asked of the line.** A fourth
  `/code-review` found that the overflowing line never moved the pass's fragmentainer cursor. The #435
  step-over only fires for a word too tall for any fragmentainer, and the word on a deep line is short,
  sitting at the line's baseline, often wholly inside a later band. So it never straddles, and no word-level
  test reaches it. Measured: after `<span 1000pt></span>text<br>l1…`, `l1` started at 822, overlapping the
  deep line, which ends at 1231, and the fragment tree had one page. `StepOverADeepLine` steps the cursor
  to where the line ends whenever an empty inline has made the line deeper than any fragmentainer. It runs
  from `GrowLineForEmptyInlines`, and from the word path when the word took held empty inlines. The page
  split then matches a 1200pt SVG in the same place (blank slot 0 aside: the deep line has no ink there).
  The first attempt re-added the step-over only where the exemption fired; a word that never straddles
  never gets there, and the probe caught it.
- **The exemption lives on `CssRect` (`LineFitsNoFragmentainer`)** so it measures against the same insets
  as `WouldStraddleFragmentainer`, including a repeating `<tfoot>`'s reservation (`BandEndInsetOf`), as the
  same review pointed out. The loop it predicted did not reproduce in a table cell, with or without the
  reservation, so no test pins it; it is kept for consistency with the check it exempts from.
- **Every place that hands held empty inlines to a line records them there.** A fifth `/code-review`:
  the atomic-inline fold added the held extent to the line but not the boxes to `CssLineBox.EmptyInlines`,
  so an empty inline taken by an inline-flex before a page break was placed again after it. All four hand-offs
  (a word landing, a `<br>`, an atomic inline, the end of the flow) now go through
  `HandHeldEmptyInlinesToTheLine`, which carries `PendingEmptyInlines` with the extent.
- **Text is re-checked where its own `vertical-align` puts it**, as images already were. The same review:
  top-aligned text was shifted with the baseline and broke a line whose drawn content fits. CSS
  Fragmentation strictly asks about the line *box*, but this engine decides by the content it draws (a
  large `line-height` whose glyphs fit stays put), and the re-check follows the engine.
- **An atomic inline takes held empty inlines too.** An inline-flex, inline-table, inline-grid or atomic
  inline-block places no word, so held extents went past it to the next word, onto a later line when that
  word wrapped. `FoldHeldEmptyInlinesIntoAtomicInlinesLine` gives them to the line the atomic landed on.
  Found by the third `/code-review`.
- **An empty inline given to the line before a break sits at the resume ordinal.** It places no word, so in
  `aaa<span 40pt></span> bbbb` its ordinal is `bbbb`'s. When `bbbb`'s line breaks to the next page, the
  resumed pass reaches the span again and would place it on that page's first line as well. Each line
  records the empty inlines given to it (`CssLineBox.EmptyInlines`). The resumed pass skips exactly those
  held by the last line an earlier fragmentainer kept (`EmptyInlinesBeforeResume`); `DiscardLineBoxesFrom`
  has trimmed the list to the kept lines, so that is the line before the resume point. Found by
  `/code-review`.

  The first fix carried the ordinal of the last commit on the `InlineBreakToken`, and skipped every empty
  inline at the resume ordinal when it matched. A second `/code-review` showed that several empty inlines
  can share one ordinal while belonging to different lines. In `aaa<span></span> <b><span 40pt></span></b>
  <b>bbbb</b>`, the first span stays on the kept line and the 40pt one goes with `bbbb` to the discarded
  one. An inline-block that wraps to open a line places no word either, so a span committed after it
  shares `bbb`'s ordinal while belonging to the discarded line. Both lost their height on resume. The
  line, not the ordinal, is what knows. Case 1 needs the extra `<b>`s because of #1322: a space between
  two empty inlines before bare text is not a wrap opportunity at all.
- **Images are re-checked where alignment will put them.** At flow time a replaced element sits at its
  line's top, and `ApplyVerticalAlignment` moves it later. So the re-check asks it at its baseline
  position (bottom margin edge on the baseline), or against the line box for `bottom`/`middle`, and skips
  a `top`-aligned one. The first cut skipped images entirely, so a line whose only word was an image
  crossed the page end. Found by `/code-review`.

**The ancestor walk** only matters where the empty inline lands on a later line than all of its
parent's words: `<span 40pt>aaa <span 10pt></span></span>bbbb` with `bbbb` wrapping. The 10pt span goes
to line 2, so the 40pt span has a fragment there too. Two earlier fixtures meant to test it put a `<br>`
in the parent, and that word counts its owner on both sides of the break, so they never needed the walk.
The mutation run caught both.

**Not done:**
- **Vertical writing modes (#1316):** `CreateVerticalLineBoxes` walks a flat word list and never sees an
  empty inline.
- **`::first-line` (#1318):** an empty inline contributes its own font, not the one it inherits from
  `::first-line`. `LineBoxContributionOf`'s ancestor walk already did that for a text-bearing span
  (`text<span>x</span>more` under a smaller `::first-line` gives a 48pt line on `main`), so this matches
  it rather than introducing it.
- **The empty inline's own `vertical-align` (#1308):** it contributes as a baseline-aligned box.
- **An inline holding only an atomic inline** is left as before: nothing counts its own strut.
- **The containing block of a positioned empty inline held for a word that wraps** is still computed on
  the old line (`AnchorSetAsideBoxes` only looks for a following word on the same line), while the line
  height now goes to the new line. Unchanged from #1299. The two only disagree in that shape.

All three issues are recorded in `.claude/accepted-gaps/`.

**Evidence:** `EmptyInlineLineHeightTests` (30). Each step-over call fails a test when removed. The word-path exemption and both atomic folds each fail
a test when reverted. Both resume cases fail when the skip is widened back to
"every empty inline at the resume ordinal", and the double-count test fails without the skip or the
record. Each guard was reverted one at a time and fails at least
one test: placing at all, holding versus committing, holding at a line's start, the ancestor walk, the
clear on landing (missing from the first cut; the first mutation run found it), the clear at a `<br>`,
the end-of-flow grow, the empty-line guard, the straddle re-check, the atomic-content test, the
too-deep exemption, both halves of the resume skip, and each image-alignment arm. The too-deep test
first asserted on line geometry and passed with the guard removed. The truncation only shows in what
reaches a page, so the test now reads the fragment tree. One
mutation survives: the `return` after `PlaceEmptyInline` takes a break. The next word on the line then
straddles too and sets the same break, so the result cannot be observed; the return only saves work.
The full net8.0 suite passes. Diff coverage is 100%. All 163 showcases were rendered on a `main` worktree
and on the branch and raster-diffed with PDFium: none differed. The `positioned_inline` showcase then got
an "empty inline with a larger font" section, which is the only page that differs; PDFium and MuPDF agree
on it.
