# Host forms: textarea input, field menus, pickers, range drag, Text-pattern selection

One PR for what was left of the form-control work after the textarea control landed. Everything host-side was built and tested only as far as a
window test (WinUI) or the Android-free logic (Android) can: nothing has been tried with a real mouse, finger, IME, Narrator or TalkBack.

- **Textarea input.** Both hosts route Enter / Up / Down / Home / End to the new `GetEditIndexByLine` / `GetEditLineRange`, keep line breaks on paste and from
  an input method (`ForField(type, text)`), and a press places the caret at once. Android's `InputTypeFor("textarea")` is multi-line with no action key.
- **Caret into view.** `ScreenFormControlPainter.ScrollToCaret` keeps `FormEditSession.ScrollTop` so the caret's line shows; it is paint-time state, and
  the painter initialises the session the way `SessionOf` does when the field has just been focused (without that the first `GetEditPlace` reset the scroll
  to 0 and the caret rectangle was outside the box). Wheel scrolling of a textarea is not done.
- **Field menu.** WinUI: right-tap / hold -> `MenuFlyout`; Android: long press -> floating `ActionMode` (cut/copy/paste/select all). The menu is a popup, so
  WinUI's `OnLostFocus` is ignored while it is open (`_fieldMenuOpen`) or the field would lose its focus and every item would act on nothing.
- **Pickers.** `SetRangeAtPoint` inverts `PaintRange`'s thumb mapping; `GetSelectOptions` lists options. WinUI shows flyouts (menu, calendar, clock,
  colour picker); Android a `PopupMenu` and date/time dialogs. No file picker anywhere, no colour picker on Android.
- **Text pattern.** `GetSelection`/`Select` for the focused field, and `GetAttributeValue`/`FindAttribute` for culture, heading style and read-only.
  `RangeFromChild` had been comparing provider wrappers by reference (never equal); it now resolves the peer with `PeerFromProvider`.
- **Live regions.** `HtmlView.ApplyUpdate(UpdateResult)` (WinUI and Android) is how an application that edits the document tells the view to repaint and
  announce; before, nothing could change text in a running host.
- **Trap (test host).** A WinUI test that leaves a `Flyout` open crashes the test host at exit with 0xC000027B, and the runner prints `total`/`failed: 0`
  for the tests that finished, so a `grep` for failures hides it - always read the whole summary. `HtmlView.CloseFlyouts` (also run on unload) fixes it.
- **Not done:** file picker; Android colour picker; wheel scroll of a textarea; text-pattern selection of page text; Text-pattern font and colour attributes;
  `<option>` end tags are not implied by the parser (a following `<option>` nests inside the previous one and its text is included in the label).
