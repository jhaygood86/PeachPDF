# In-place text edits re-lay out the whole document and never change the element structure

`HtmlDocument.ReplaceTextAsync` (and `InsertTextAsync`/`DeleteTextAsync`) change the text of existing text boxes, remove text boxes they empty, and
create one only through `InsertTextAsync(element, ...)`. They then run `PerformLayout` over the whole tree again.

Deliberately not done:
- **Resuming from the changed page.** The relayout is the whole fragmentation walk, 70-100 ms for 100 pages of text and over a second for hundreds
  (`.claude/recent-fixes/2026-10-27-tier-b-relayout-spike.md`). Resuming at page k needs a snapshot of the state `LayoutDocument` resets at its
  start; the building blocks (`BreakToken` records, `PassRewind`, `FragmentEmitter.InvalidateFrom`) exist.
- **Joining blocks, splitting elements, wrapping a range.** Deleting across paragraphs leaves two paragraphs (the DOM's `Range.deleteContents`, not an
  editor's backspace-joins-paragraphs); Enter does not split a `<p>`. They need box creation and reparenting, and re-running the load-time passes that
  wrap inline content in anonymous blocks.
- **Whitespace-only gaps load dropped.** A text node that was only white space between two blocks is removed at load and cannot come back; typing there
  has to go into one of the blocks (or `InsertTextAsync(element, ...)`).
- **Generated content, `svg`, `math`, `<br>`** have no editable text position.
- **Documents whose layout depends on every page** (footnotes, page floats, `target-counter(page)`, named pages, running elements, multicolumn,
  container queries, per-page widths) are refused, not re-laid-out approximately.
- **Failure mid-edit is not rolled back.** The text is spliced before the relayout; an exception from layout leaves the edited tree.
- A word a small-caps split cut where a soft hyphen was removed has no source offset (`CssRect.SourceStart == -1`) and refuses the edit.

Closing the first needs the snapshot above; the second the load-time structure passes made re-runnable for one block.
