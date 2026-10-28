# In-place text editing (`ReplaceTextAsync`)

Tier B of the selection/editor roadmap, first slice: `HtmlDocument.ReplaceTextAsync`/`InsertTextAsync`/`DeleteTextAsync` change a laid-out document's
text and re-lay it out on the same tree; `TextEditResult` reports pages before/after, the first dirty page, the pages to repaint and the new caret.

**Load-bearing ideas.**
- **A word knows where it came from.** `CssRect.SourceStart`/`SourceLength` (set in `CssBox.AppendWordsFromText`) are the word's start and span in its
  owner box's own `Text`. A `TextRun`'s `TextWordSlice` then maps a position in the laid-out text back to `(text box, index)`, and `TextEditor.ToPosition`
  maps the caret forward again. Soft hyphens (removed from the word, present in the source) are walked over; a word that a small-caps split cut where
  one was removed is `-1` (unmappable) rather than wrong.
- **Work in the box's own text, not the DOM's.** `CollapseWhitespaceAcrossInlineBoundaries` strips leading spaces off `Text` at load, so the source of a box is
  already the collapsed text and the runs show exactly that; splicing `_text` is consistent with what the user sees, and the same collapse is run again.
- **Redo what loading does, per paragraph, in load order:** `CssBidiParagraphResolver.AssignBidiLevels(paragraphRoot)` (levels of every text box in a paragraph
  depend on all of it - new `ParagraphRootOf`), then `ParseToWords` on each ordinary text leaf, then `CollapseWhitespaceAcrossInlineBoundaries`.
- **The oracle is a fresh load.** Every edit test lays the same markup out from scratch and compares pages, extents, run texts and every character offset.
- Text boxes the edit empties are detached (`ParentBox = null`), middle boxes of a cross-box range are emptied and detached, generated content in
  between is left alone.

**Found by running it.**
- `PageRules.Count > 0` is true for every document (the default style sheet has `@page` rules), so refusing on it refused everything. `@page` margin boxes
  are drawn at paint time from the page count, and only a *running element* margin box is laid out, so the refusal list uses `RunningElements` instead.
- `ContentExtent.Height` is not "the document got taller"; a growth assertion belongs on an element's bounds. The comparison with a fresh load covers the extent.
- Whitespace-only text boxes between two *inline* boxes are kept at load (they stand for the space); only those between blocks are dropped.

**Spike claim corrected.** The spike note warned that a repeating `<thead>` measures taller on a repeat. `LayoutHarness.LayoutRepeatedlyAsync`'s tests show
`RestoreStructureFromAnyPreviousRun` fixes it; `ATableWithARepeatingHeader_LaysOutLikeAFreshLoadAfterAnEdit` (two successive edits over a header repeated on
several pages) matches a fresh load.

**Split and join (second slice).** `SplitParagraphAsync` clones the paragraph (`CreateBox(parent, tag)` + `InheritStyle(original, everything: true)`, as
`CorrectBlockSplitBadBox` does) and, climbing from the split text, each inline element it is inside, moving the following siblings into the clones;
`JoinWithNextParagraphAsync` reparents the next paragraph's children and detaches it. Trap found by running it: a join leaves two adjacent text boxes
where a fresh load has one, which shapes without kerning across the seam (0.24pt wider) - they must be merged, and the caret index must be taken
*before* the merge. Split at the very end leaves a paragraph with no text box, hence `TextEditResult.Block` for `InsertTextAsync(element, ...)`.

**Found in review.** Loading strips a text box's leading space when the run continues from the box before it, so deleting that earlier space lost
it for good (`ab <b> cd</b>` became `abcd`). `CssBox.LeadingWhitespaceCollapsed` now records the strip and `RedoText` puts the space back before collapsing
again (returning the boxes where it stays, whose caret index moves by one). A split also removed empty inlines that were empty *before* it (`<a id>` targets);
it now removes only the ones it emptied.

**Not done** - see `.claude/accepted-gaps/text-edits-relayout-the-whole-document-and-do-not-edit-structure.md`.

**Evidence.** `TextEditTests` (16): insert, delete, replace, range across inline elements and across paragraphs, emptied inline, growth, multi-page first-dirty-page,
empty element, typing a sequence from the returned caret, whitespace collapse, soft hyphen, RTL, `pre-wrap` newlines, repeating table header, refusals, option
off. Engine suite 16,523, `PeachBrackets.Tests` 84, `PeachPDF.Tests` 1,490 passed / 0 failed.
