# Host forms follow-ups: option end tags, textarea wheel, file picker, Android colour, font attributes, page selection

What the previous host-forms PR listed as not done. Host-side parts are tested the same way as there (window tests on WinUI, logic tests and a build on
Android); nothing was tried with a real wheel, picker dialog, finger, Narrator or TalkBack.

- **`</option>` / `</optgroup>`** are now implied by `HtmlUtils.CanEndTagBeOmitted` (an `option` start tag ends an open option; `optgroup` ends an open
  option or optgroup), so `<option>a<option>b` is two options instead of one nested in the other with its text included in the label.
  `<li>`, `<dt>`, `<dd>`, `<th>`, `<thead>`/`<tbody>` are still not implied (the architecture doc's old example list said `</li>`; corrected).
- **Textarea wheel.** The scroll position moved from the edit session to the container (`TextAreaScroll`, by source ordinal) so a text area scrolls
  whether or not it has the focus. `FormEditSession.FollowCaret` replaces "always keep the caret in view": it is set by the focus and by every selection
  or text change and cleared by the paint that follows, so a wheel can scroll away from the caret and the next edit brings it back.
  `ScrollTextArea(element, points, canvas)` clamps itself (it needs the canvas to know how many lines there are) and reports not applied at the ends so
  the host can scroll the page instead. Trap: the element a host holds may be from before a restyle (a change of focus loads the source again), so the
  live box is found again by source ordinal; `element.Box` is stale after `SetFocusAsync`.
- **File input.** The document keeps only the names (attribute `peachpdf-chosen-files`, one per line, kept across reparses by `SetControlAttribute`):
  `SetFileNames`, `GetFileInputInfo`, the painter's "No file chosen" / name / "n files" label, and `GetFormState(...).Value` as "a, b". WinUI uses
  `FileOpenPicker` initialised with the window from `XamlRoot.ContentIslandEnvironment.AppWindowId`; a picker that cannot open counts as nothing chosen.
  Android uses `ACTION_OPEN_DOCUMENT` and cannot get the result itself, so the application forwards `OnActivityResult` to `HandleActivityResult`.
- **Android colour.** Three `SeekBar`s over a swatch in an `AlertDialog`; there is no system colour picker to use.
- **Node style.** `AccessibilityNode` gained `FontFamily`, `FontSize`, `FontWeight`, `IsItalic`, `ForegroundColor`, `BackgroundColor`, read from the box
  in `ApplyStyle` for element and text nodes. Trap: `Font.Size` is already in points; dividing it by `LayoutUnitsPerPoint` happens to be right in a PDF
  (1.0) and wrong in a host at 96 dpi (it came out 15 for 20pt). The WinUI Text pattern maps them to UI Automation's font, colour (as a COLORREF,
  0x00BBGGRR) and italic attributes.
- **Page-text selection.** The reading text and the document's runs have different layouts, so offsets are not mapped; a selection inside one block is
  found by its phrase and its place among the non-overlapping occurrences (`TextRunQueries.Find` steps by the query length), and `Select` does the
  reverse through `FindText` and `DocumentSelection.Select`. A selection that crosses blocks is not reported.
- **Not done:** underline/strikethrough text attributes; multi-block page selection through the pattern; mapping a file input's chosen files to anything
  but names (so no form submission of file contents).
