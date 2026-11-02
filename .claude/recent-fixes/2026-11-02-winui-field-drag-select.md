# WinUI: a press places the caret and a drag selects inside a text field

The caret used to be placed on release (`OnPointerReleased` -> `FocusTextFieldAt`), so there was nothing to drag from. A left press on a text field now
focuses it and places the caret (`_fieldPressed`); moves while pressed call `ExtendFieldSelectionAsync` with the press index as the anchor, and the
release skips its own focus/activate/link step when the press already handled the field. Touch is unchanged (caret on release).

- Trap: re-placing the caret on release would collapse the drag's selection, hence the `wasField` skip.
- The index comes from `GetEditIndexAt`, which is async, so the anchor is only known once the focus work finishes; a move before that is ignored, and
  overlapping moves are dropped (`_fieldDragBusy`) rather than queued.
- Not done: scrolling a long value while dragging past the field's edge; the test drives `ExtendFieldSelectionAsync` directly because a
  `PointerRoutedEventArgs` cannot be built.
