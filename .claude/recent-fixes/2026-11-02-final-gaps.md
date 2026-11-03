# The last small gaps of the host-forms work

- **Implied end tags.** `HtmlUtils.CanEndTagBeOmitted` now also covers `li` (by `li`), `dt`/`dd` (by `dt`/`dd`), `th` like `td`, `thead`/`tbody` (by
  `tbody`/`tfoot`), and a `tbody`/`thead`/`tfoot` start tag ends an open `td`/`th`/`tr`. `li`, `dt` and `dd` were added to the tags that end a
  paragraph, because the tree-construction rule closes a `<p>` still open in the previous item. The check is on the *current* box only (as for `p`), so
  an item left open inside an unclosed `<b>` is not closed; the spec's reconstruction of formatting elements is not implemented.
- **Underline / strikethrough.** `AccessibilityNode.Underline` and `Strikethrough` walk up the boxes for a `text-decoration-line`, because a decoration set
  on an element is drawn on the text inside it. UI Automation's two style attributes are 0 for none and 1 for a single line.
- **Page selection across blocks.** The one-block limit was only a guard in `HtmlViewText`: `TextRunQueries.Find` joins runs with the same line breaks
  the reading text uses, so a multi-line phrase is found and counted like any other. A trailing line break on a range is trimmed before the search, since
  the document's own text has no break at the end of a block.
- **Flyouts.** `ShowFlyout` hides the open flyout and shows the next from its `Closed` event (`_pendingFlyout`) instead of calling `Hide()` and `ShowAt()` back
  to back, which crashed the test host (0xC000027B). The window tests now open every kind in a row with nothing between them and ran clean four times.
- **Form submission.** `HtmlDocument.CreateFormSubmission(submitter)` collects the entries (document order, the form owner by ancestor or `form=`, skipping
  disabled controls and those in a disabled fieldset, only the used button, a file input's files or the empty file the spec sends), and
  `FormSubmission` serializes them (urlencoded with `+` for space, multipart with escaped names, text/plain) and builds the `get` address. Files are
  `ChosenFile(Name, ContentType, Content)` held per input by source ordinal and set by `SetFiles`; `SetFileNames` keeps the bytes of a name that stays.
  Both hosts read the file bytes when a picker returns and raise `FormSubmitting` when a submit button is clicked/tapped (WinUI also on Enter/Space and UI
  Automation Invoke of a button). Traps: a `<button>` is not a `CssBoxFormField`, so the submit code reads tags, not box kinds; `\n` in a Python
  one-liner passed through a shell heredoc turned into a real line break, so scripts that touch C# string escapes are written to files.
- **Not done:** implicit submission by Enter in a text field, `reset` buttons, constraint validation before a submit, image-button coordinates
  (`name.x`/`name.y`), and the formatting-element reconstruction mentioned above.
