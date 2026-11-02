# WinUI: controls are painted by the engine and a click toggles them

`HtmlView` sets `PaintFormControls` and handles the click itself: `OnPointerReleased` asks `GetElementAt` for the element under the point and, when it is an `input` or `select`, calls `HtmlDocument.Activate` before it considers a link (a check box inside a `<a>` is the control's click, not the link's). The dirty regions repaint, and `RefreshAccessibility` takes the new tree, since a change makes the document build one.

- **The peer reads the live tree.** A `HtmlNodeAutomationPeer` holds the node it was made from, and after a toggle that node is from the old tree; `ToggleState` goes through `HtmlView.CurrentNode`, which finds the node by id in the document's current tree. Reading `_node.Checked` directly reported Off after a toggle.
- **`CollectFormFields` stays on.** The view still wants the fields; `PaintFormControls` wins for how a control is drawn.
- **Tested in a real window** (`AScreenReaderTogglesACheckBoxAndTheRadioButtonsOfAGroupFollow`): the peer's Toggle pattern flips a check box and moves a radio group.
- **Not done:** typing, the drop-down list and range dragging need the text-services bridge and a popup; the focus ring for a control is the page's `:focus-visible` style.
