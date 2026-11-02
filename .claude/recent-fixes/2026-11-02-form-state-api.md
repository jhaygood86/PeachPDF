# Form state lives in the element's attributes, and changes are repaint-only

`HtmlDocument.SetChecked`/`SetValue`/`Activate` write `checked`, `value` and `selected` on the control's `HtmlTag` and return the control's dirty regions (`StyleUpdater.DirtyRegionsOf`). Painting, the accessibility tree and the PDF classifier all already read those attributes, so there is no second store to keep in step; the cached accessibility tree is dropped.

- **No relayout.** A control's size is its intrinsic size or its CSS size, not its content, so changing the value cannot move anything. Text longer than the field is clipped by the painter (`ScreenFormControlPainter`), as in a browser.
- **A failed `SetValue` on a drop-down must not touch the options.** The first version cleared every `selected` flag while looking for the option, so asking for an option that did not exist left nothing selected; the match is found first and the flags change only if it exists.
- **Options can nest.** An `<option>` left unclosed contains the next one in the box tree, so `FormFieldMapper`'s direct-children scan sees one option. The state code walks the whole subtree; the mapper (which PDF fields use) keeps its behaviour for now.
- **Not done:** `:checked` and `:disabled` style rules are not re-matched after a change (the page repaints with the new mark, but a rule keyed on `:checked` is not re-applied); typing needs the host input bridges; `form` submission is not modelled.
