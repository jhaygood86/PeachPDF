# In-place text edits re-lay out the whole document and never change the element structure

`HtmlDocument.ReplaceTextAsync` (and `InsertTextAsync`/`DeleteTextAsync`) change the text of existing text boxes, remove text boxes they empty, and
create one only through `InsertTextAsync(element, ...)`. They then run `PerformLayout` over the whole tree again.

Deliberately not done:
- **Resuming is not always possible.** `HtmlContainerInt.TryResumeLayoutAt` declines (the whole document is laid out again, `ResumedFromPage` is null) when the
  edited paragraph was placed in the first two passes, when the document has per-page widths, footnotes, page floats, container queries, `target-counter(page)`,
  per-page margin/size overrides, reserved blank slots, flex or grid content (their content extent cannot be reproduced), a page width not given by the options, when no pass entry names the paragraph, and when no earlier pass was entered at a clean block boundary (every break before it falls inside a paragraph). A resumed layout
  starts from the pass before the one that placed the paragraph, so a keep-with-next run that reaches back further than that is handled only by the in-run rewind.
- **Wrapping a range, and structure edits beyond a plain paragraph.** `SplitParagraphAsync`/`JoinWithNextParagraphAsync` handle only a block in a block
  with inline-only content (no list item, table cell, flex/grid item, float, generated content, or block holding blocks). The new paragraph copies the old
  one's style, so structural selectors (`:nth-child`, `:last-child`, `:first-line` effects of a different first line) are not re-evaluated, and a join
  merges adjacent plain texts only at the top level of the paragraph. Deleting a range across paragraphs still leaves two paragraphs (`Range.deleteContents`).
  Nothing wraps a range in a new element or re-runs the anonymous-block passes for a block that holds mixed content.
- **Whitespace-only gaps load dropped.** A text node that was only white space between two blocks is removed at load and cannot come back; typing there
  has to go into one of the blocks (or `InsertTextAsync(element, ...)`).
- **Generated content, `svg`, `math`, `<br>`** have no editable text position.
- **Documents whose layout depends on every page** (footnotes, page floats, `target-counter(page)`, named pages, running elements, multicolumn,
  container queries, per-page widths) are refused, not re-laid-out approximately.
- **Failure mid-edit is not rolled back.** The text is spliced before the relayout; an exception from layout leaves the edited tree.
- A word a small-caps split cut where a soft hyphen was removed has no source offset (`CssRect.SourceStart == -1`) and refuses the edit.

- **A split paragraph is not re-cascaded.** It adopts the original's whole computed style (`AdoptComputedStyle`; `InheritStyle(everything: true)` dropped
  `min-height` and others), so structural selectors are not evaluated for its new position; both halves share the `HtmlTag`, so an `id` is duplicated; and the
  layout caches that depend on tree shape (quote depth, finalized counter names) are not reset on the paragraph or its later siblings.
